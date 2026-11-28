/*
 * SPDX-FileCopyrightText: Copyright (주)매커스시스템즈
 * SPDX-License-Identifier: MIT
 *
 * libmoavi-rdp: FreeRDP 3 for MOAVI Desk's own RDP window on macOS (src/MoaviDesk.Mac/RdpView.cs).
 * The C# side gives the FreeRDP options (the same as for sdl-freerdp, FreeRdpArgs), runs moavi_rdp_run on a
 * thread of its own and draws the frame buffer (BGRA32) when a frame is done; mouse, keys (RDP scancodes),
 * text clipboard and window size go back through the functions below.
 */

#include <stdlib.h>
#include <string.h>

#include <winpr/crt.h>
#include <winpr/synch.h>
#include <winpr/string.h>
#include <winpr/user.h>

#include <freerdp/freerdp.h>
#include <freerdp/client.h>
#include <freerdp/client/cmdline.h>
#include <freerdp/client/channels.h>
#include <freerdp/client/cliprdr.h>
#include <freerdp/client/disp.h>
#include <freerdp/client/rdpdr.h>
#include <freerdp/channels/rdpdr.h>
#include <freerdp/gdi/gdi.h>
#include <freerdp/channels/channels.h>
#include <freerdp/event.h>

#define EXPORT __attribute__((visibility("default")))

typedef void (*moavi_frame_cb)(void* user, int x, int y, int w, int h);
typedef void (*moavi_size_cb)(void* user, int w, int h);
typedef void (*moavi_text_cb)(void* user, const char* utf8);
typedef void (*moavi_state_cb)(void* user, int state, const char* message);   /* 1 connected, 2 ended */

typedef struct
{
	rdpClientContext common;
	moavi_frame_cb frame;
	moavi_size_cb size;
	moavi_text_cb clip;
	moavi_state_cb state;
	void* user;
	CliprdrClientContext* cliprdr;
	DispClientContext* disp;
	CRITICAL_SECTION lock;
	char* local_text;        /* this Mac's clipboard, offered to the desktop */
	BOOL remote_has_text;
	BOOL clip_on;            /* the 장치 menu's clipboard switch (the channel stays, nothing goes through when off) */
	RdpdrClientContext* rdpdr;
	struct
	{
		int token;           /* the C# side's key */
		UINT32 type;
		char* name;
		char* path;
		uint32_t id;         /* rdpdr's, once registered */
		BOOL registered;
	} dev[64];
	int ndev;
} moavi_context;

static moavi_context* M(rdpContext* c) { return (moavi_context*)c; }

/* --- clipboard (text both ways, CF_UNICODETEXT) ------------------------------------------------- */
static UINT clip_send_format_list(moavi_context* m)
{
	CLIPRDR_FORMAT fmt = { CF_UNICODETEXT, NULL };
	CLIPRDR_FORMAT_LIST list = { 0 };
	list.common.msgType = CB_FORMAT_LIST;
	list.numFormats = m->clip_on && m->local_text ? 1 : 0;
	list.formats = &fmt;
	return m->cliprdr->ClientFormatList(m->cliprdr, &list);
}

static UINT clip_monitor_ready(CliprdrClientContext* ctx, const CLIPRDR_MONITOR_READY* ready)
{
	moavi_context* m = (moavi_context*)ctx->custom;
	CLIPRDR_GENERAL_CAPABILITY_SET general = { 0 };
	general.capabilitySetType = CB_CAPSTYPE_GENERAL;
	general.capabilitySetLength = CB_CAPSTYPE_GENERAL_LEN;
	general.version = CB_CAPS_VERSION_2;
	general.generalFlags = CB_USE_LONG_FORMAT_NAMES;
	CLIPRDR_CAPABILITIES caps = { 0 };
	caps.cCapabilitiesSets = 1;
	caps.capabilitySets = (CLIPRDR_CAPABILITY_SET*)&general;
	UINT rc = ctx->ClientCapabilities(ctx, &caps);
	if (rc != CHANNEL_RC_OK)
		return rc;
	return clip_send_format_list(m);
}

static UINT clip_server_caps(CliprdrClientContext* ctx, const CLIPRDR_CAPABILITIES* caps)
{
	return CHANNEL_RC_OK;
}

static UINT clip_server_format_list(CliprdrClientContext* ctx, const CLIPRDR_FORMAT_LIST* list)
{
	moavi_context* m = (moavi_context*)ctx->custom;
	BOOL text = FALSE;
	for (UINT32 i = 0; i < list->numFormats; i++)
		if (list->formats[i].formatId == CF_UNICODETEXT)
			text = TRUE;
	CLIPRDR_FORMAT_LIST_RESPONSE resp = { 0 };
	resp.common.msgType = CB_FORMAT_LIST_RESPONSE;
	resp.common.msgFlags = CB_RESPONSE_OK;
	UINT rc = ctx->ClientFormatListResponse(ctx, &resp);
	m->remote_has_text = text;
	if (text && m->clip_on)
	{
		/* the desktop copied text: fetch it at once so this Mac's clipboard has it */
		CLIPRDR_FORMAT_DATA_REQUEST req = { 0 };
		req.common.msgType = CB_FORMAT_DATA_REQUEST;
		req.requestedFormatId = CF_UNICODETEXT;
		rc = ctx->ClientFormatDataRequest(ctx, &req);
	}
	return rc;
}

static UINT clip_server_format_list_response(CliprdrClientContext* ctx, const CLIPRDR_FORMAT_LIST_RESPONSE* r)
{
	return CHANNEL_RC_OK;
}

static UINT clip_server_data_request(CliprdrClientContext* ctx, const CLIPRDR_FORMAT_DATA_REQUEST* req)
{
	moavi_context* m = (moavi_context*)ctx->custom;
	CLIPRDR_FORMAT_DATA_RESPONSE resp = { 0 };
	resp.common.msgType = CB_FORMAT_DATA_RESPONSE;
	WCHAR* wide = NULL;
	size_t chars = 0;
	EnterCriticalSection(&m->lock);
	if (req->requestedFormatId == CF_UNICODETEXT && m->local_text && m->clip_on)
		wide = ConvertUtf8ToWCharAlloc(m->local_text, &chars);
	LeaveCriticalSection(&m->lock);
	if (wide)
	{
		resp.common.msgFlags = CB_RESPONSE_OK;
		resp.common.dataLen = (UINT32)((chars + 1) * sizeof(WCHAR));
		resp.requestedFormatData = (const BYTE*)wide;
	}
	else
		resp.common.msgFlags = CB_RESPONSE_FAIL;
	UINT rc = ctx->ClientFormatDataResponse(ctx, &resp);
	free(wide);
	return rc;
}

static UINT clip_server_data_response(CliprdrClientContext* ctx, const CLIPRDR_FORMAT_DATA_RESPONSE* resp)
{
	moavi_context* m = (moavi_context*)ctx->custom;
	if (!m->clip_on || resp->common.msgFlags != CB_RESPONSE_OK || !resp->requestedFormatData || resp->common.dataLen < 2)
		return CHANNEL_RC_OK;
	char* utf8 = ConvertWCharNToUtf8Alloc((const WCHAR*)resp->requestedFormatData, resp->common.dataLen / sizeof(WCHAR), NULL);
	if (utf8)
	{
		if (m->clip)
			m->clip(m->user, utf8);
		free(utf8);
	}
	return CHANNEL_RC_OK;
}

/* --- devices (drives, printers) added and taken away during the session ----------------------------- */
static void dev_register(moavi_context* m, int i)
{
	if (!m->rdpdr || m->dev[i].registered)
		return;
	const char* args[2] = { m->dev[i].name, m->dev[i].path ? m->dev[i].path : "" };
	RDPDR_DEVICE* d = freerdp_device_new(m->dev[i].type, m->dev[i].type == RDPDR_DTYP_FILESYSTEM ? 2 : 1, args);
	if (!d)
		return;
	uint32_t id = 0;
	if (m->rdpdr->RdpdrRegisterDevice(m->rdpdr, d, &id) == CHANNEL_RC_OK)
	{
		m->dev[i].id = id;
		m->dev[i].registered = TRUE;
	}
	freerdp_device_free(d);
}

static void dev_register_all(moavi_context* m)
{
	EnterCriticalSection(&m->lock);
	for (int i = 0; i < m->ndev; i++)
		dev_register(m, i);
	LeaveCriticalSection(&m->lock);
}

static void on_channel_connected(void* context, const ChannelConnectedEventArgs* e)
{
	moavi_context* m = M((rdpContext*)context);
	if (strcmp(e->name, CLIPRDR_SVC_CHANNEL_NAME) == 0)
	{
		CliprdrClientContext* c = (CliprdrClientContext*)e->pInterface;
		m->cliprdr = c;
		c->custom = m;
		c->MonitorReady = clip_monitor_ready;
		c->ServerCapabilities = clip_server_caps;
		c->ServerFormatList = clip_server_format_list;
		c->ServerFormatListResponse = clip_server_format_list_response;
		c->ServerFormatDataRequest = clip_server_data_request;
		c->ServerFormatDataResponse = clip_server_data_response;
	}
	else if (strcmp(e->name, DISP_DVC_CHANNEL_NAME) == 0)
		m->disp = (DispClientContext*)e->pInterface;
	else if (strcmp(e->name, RDPDR_SVC_CHANNEL_NAME) == 0)
	{
		m->rdpdr = (RdpdrClientContext*)e->pInterface;
		dev_register_all(m);
	}
	else
		freerdp_client_OnChannelConnectedEventHandler(context, e);
}

static void on_channel_disconnected(void* context, const ChannelDisconnectedEventArgs* e)
{
	moavi_context* m = M((rdpContext*)context);
	if (strcmp(e->name, CLIPRDR_SVC_CHANNEL_NAME) == 0)
		m->cliprdr = NULL;
	else if (strcmp(e->name, DISP_DVC_CHANNEL_NAME) == 0)
		m->disp = NULL;
	else if (strcmp(e->name, RDPDR_SVC_CHANNEL_NAME) == 0)
		m->rdpdr = NULL;
	else
		freerdp_client_OnChannelDisconnectedEventHandler(context, e);
}

/* --- connection ----------------------------------------------------------------------------------- */
static BOOL pre_connect(freerdp* instance)
{
	rdpSettings* s = instance->context->settings;
	if (!freerdp_settings_set_uint32(s, FreeRDP_OsMajorType, OSMAJORTYPE_MACINTOSH) ||
	    !freerdp_settings_set_uint32(s, FreeRDP_OsMinorType, OSMINORTYPE_MACINTOSH))
		return FALSE;
	if (PubSub_SubscribeChannelConnected(instance->context->pubSub, on_channel_connected) < 0 ||
	    PubSub_SubscribeChannelDisconnected(instance->context->pubSub, on_channel_disconnected) < 0)
		return FALSE;
	return TRUE;
}

static BOOL end_paint(rdpContext* context)
{
	moavi_context* m = M(context);
	rdpGdi* gdi = context->gdi;
	if (!gdi || !gdi->primary || !gdi->primary->hdc->hwnd)
		return TRUE;
	HGDI_WND hwnd = gdi->primary->hdc->hwnd;
	if (hwnd->invalid->null || hwnd->ninvalid < 1)
		return TRUE;
	INT32 x1 = INT32_MAX, y1 = INT32_MAX, x2 = 0, y2 = 0;
	for (INT32 i = 0; i < hwnd->ninvalid; i++)
	{
		const GDI_RGN* r = &hwnd->cinvalid[i];
		if (r->x < x1) x1 = r->x;
		if (r->y < y1) y1 = r->y;
		if (r->x + r->w > x2) x2 = r->x + r->w;
		if (r->y + r->h > y2) y2 = r->y + r->h;
	}
	hwnd->invalid->null = TRUE;
	hwnd->ninvalid = 0;
	if (m->frame)
		m->frame(m->user, x1, y1, x2 - x1, y2 - y1);
	return TRUE;
}

static BOOL desktop_resize(rdpContext* context)
{
	moavi_context* m = M(context);
	rdpSettings* s = context->settings;
	UINT32 w = freerdp_settings_get_uint32(s, FreeRDP_DesktopWidth);
	UINT32 h = freerdp_settings_get_uint32(s, FreeRDP_DesktopHeight);
	if (!gdi_resize(context->gdi, w, h))
		return FALSE;
	if (m->size)
		m->size(m->user, (int)w, (int)h);
	return TRUE;
}

static BOOL post_connect(freerdp* instance)
{
	moavi_context* m = M(instance->context);
	if (!gdi_init(instance, PIXEL_FORMAT_BGRA32))
		return FALSE;
	instance->context->update->EndPaint = end_paint;
	instance->context->update->DesktopResize = desktop_resize;
	if (m->size)
		m->size(m->user, (int)instance->context->gdi->width, (int)instance->context->gdi->height);
	if (m->state)
		m->state(m->user, 1, "");
	return TRUE;
}

static void post_disconnect(freerdp* instance)
{
	gdi_free(instance);
}

/* a desktop certificate seen for the first time is trusted (/cert:tofu stores it); a changed one is refused */
static DWORD verify_certificate(freerdp* instance, const char* host, UINT16 port, const char* cn, const char* subject,
                                const char* issuer, const char* fingerprint, DWORD flags)
{
	return (flags & VERIFY_CERT_FLAG_CHANGED) ? 0 : 2;
}

static DWORD verify_changed_certificate(freerdp* instance, const char* host, UINT16 port, const char* cn,
                                        const char* subject, const char* issuer, const char* fingerprint,
                                        const char* old_subject, const char* old_issuer, const char* old_fingerprint,
                                        DWORD flags)
{
	return 0;
}

/* never FreeRDP's console prompt: the connection goes on with what MOAVI Desk gave it (the desktop's own logon
 * screen asks when nothing was given; NLA without a password ends with FreeRDP's error, shown in the bar) */
static BOOL authenticate(freerdp* instance, char** username, char** password, char** domain, rdp_auth_reason reason)
{
	return TRUE;
}

static BOOL client_new(freerdp* instance, rdpContext* context)
{
	moavi_context* m = M(context);
	InitializeCriticalSection(&m->lock);
	instance->PreConnect = pre_connect;
	instance->PostConnect = post_connect;
	instance->PostDisconnect = post_disconnect;
	instance->VerifyCertificateEx = verify_certificate;
	instance->AuthenticateEx = authenticate;
	instance->VerifyChangedCertificateEx = verify_changed_certificate;
	return TRUE;
}

static void client_free(freerdp* instance, rdpContext* context)
{
	moavi_context* m = M(context);
	free(m->local_text);
	for (int i = 0; i < m->ndev; i++)
	{
		free(m->dev[i].name);
		free(m->dev[i].path);
	}
	DeleteCriticalSection(&m->lock);
}

/* --- the API -------------------------------------------------------------------------------------- */
EXPORT void* moavi_rdp_new(int argc, char** argv, moavi_frame_cb frame, moavi_size_cb size, moavi_text_cb clip,
                           moavi_state_cb state, void* user)
{
	RDP_CLIENT_ENTRY_POINTS ep = { 0 };
	ep.Version = RDP_CLIENT_INTERFACE_VERSION;
	ep.Size = sizeof(RDP_CLIENT_ENTRY_POINTS_V1);
	ep.ContextSize = sizeof(moavi_context);
	ep.ClientNew = client_new;
	ep.ClientFree = client_free;
	rdpContext* context = freerdp_client_context_new(&ep);
	if (!context)
		return NULL;
	moavi_context* m = M(context);
	m->frame = frame;
	m->size = size;
	m->clip = clip;
	m->state = state;
	m->user = user;
	m->clip_on = TRUE;
	if (freerdp_client_settings_parse_command_line(context->settings, argc, argv, FALSE) != 0)
	{
		freerdp_client_context_free(context);
		return NULL;
	}
	/* the device channel always, so the 장치 menu can add a drive or a printer during the session */
	if (!freerdp_settings_set_bool(context->settings, FreeRDP_DeviceRedirection, TRUE))
	{
		freerdp_client_context_free(context);
		return NULL;
	}
	return context;
}

/* blocks until the session ends; returns FreeRDP's last error (0 = a normal end) */
EXPORT int moavi_rdp_run(void* handle)
{
	rdpContext* context = (rdpContext*)handle;
	moavi_context* m = M(context);
	freerdp* instance = context->instance;
	UINT32 error = 0;
	if (!freerdp_connect(instance))
	{
		error = freerdp_get_last_error(context);
		if (m->state)
			m->state(m->user, 2, freerdp_get_last_error_string(error));
		return error ? (int)error : -1;
	}
	while (!freerdp_shall_disconnect_context(context))
	{
		HANDLE handles[MAXIMUM_WAIT_OBJECTS] = { 0 };
		DWORD n = freerdp_get_event_handles(context, handles, ARRAYSIZE(handles));
		if (n == 0)
			break;
		if (WaitForMultipleObjects(n, handles, FALSE, 100) == WAIT_FAILED)
			break;
		if (!freerdp_check_event_handles(context))
		{
			error = freerdp_get_last_error(context);
			break;
		}
	}
	UINT32 info = freerdp_error_info(instance);
	freerdp_disconnect(instance);
	if (m->state)
		m->state(m->user, 2, error ? freerdp_get_last_error_string(error) : "");
	return (int)(error ? error : info);
}

EXPORT unsigned char* moavi_rdp_buffer(void* handle, int* width, int* height, int* stride)
{
	rdpGdi* gdi = ((rdpContext*)handle)->gdi;
	if (!gdi)
		return NULL;
	*width = (int)gdi->width;
	*height = (int)gdi->height;
	*stride = (int)gdi->stride;
	return gdi->primary_buffer;
}

EXPORT void moavi_rdp_mouse(void* handle, int flags, int x, int y)
{
	rdpContext* c = (rdpContext*)handle;
	if (c->input)
		freerdp_input_send_mouse_event(c->input, (UINT16)flags, (UINT16)x, (UINT16)y);
}

EXPORT void moavi_rdp_key(void* handle, int down, int scancode)
{
	rdpContext* c = (rdpContext*)handle;
	if (c->input)
		freerdp_input_send_keyboard_event_ex(c->input, down ? TRUE : FALSE, FALSE, (UINT32)scancode);
}

EXPORT void moavi_rdp_unicode(void* handle, int down, int code)
{
	rdpContext* c = (rdpContext*)handle;
	if (c->input)
		freerdp_input_send_unicode_keyboard_event(c->input, down ? 0 : KBD_FLAGS_RELEASE, (UINT16)code);
}

EXPORT void moavi_rdp_clipboard(void* handle, const char* utf8)
{
	moavi_context* m = M((rdpContext*)handle);
	EnterCriticalSection(&m->lock);
	free(m->local_text);
	m->local_text = utf8 ? _strdup(utf8) : NULL;
	LeaveCriticalSection(&m->lock);
	if (m->cliprdr)
		clip_send_format_list(m);
}

/* the window's new size (points × scale): the desktop follows when it has the display channel */
EXPORT void moavi_rdp_resize(void* handle, int width, int height, int scale)
{
	moavi_context* m = M((rdpContext*)handle);
	if (!m->disp || width < 200 || height < 200)
		return;
	DISPLAY_CONTROL_MONITOR_LAYOUT layout = { 0 };
	layout.Flags = DISPLAY_CONTROL_MONITOR_PRIMARY;
	layout.Width = (UINT32)(width & ~1);
	layout.Height = (UINT32)height;
	layout.DesktopScaleFactor = (UINT32)scale;
	layout.DeviceScaleFactor = scale >= 200 ? 180 : 100;
	m->disp->SendMonitorLayout(m->disp, 1, &layout);
}

/* a drive (name, a folder of this Mac) or a printer (its CUPS name) for the desktop; on = FALSE takes it away.
 * Before the device channel is up the device waits and goes in the first device list. */
EXPORT void moavi_rdp_device(void* handle, int token, int printer, const char* name, const char* path, int on)
{
	moavi_context* m = M((rdpContext*)handle);
	EnterCriticalSection(&m->lock);
	int i = 0;
	while (i < m->ndev && m->dev[i].token != token)
		i++;
	if (on)
	{
		if (i == m->ndev && m->ndev < 64)
		{
			m->dev[i].token = token;
			m->dev[i].type = printer ? RDPDR_DTYP_PRINT : RDPDR_DTYP_FILESYSTEM;
			m->dev[i].name = _strdup(name);
			m->dev[i].path = path ? _strdup(path) : NULL;
			m->dev[i].registered = FALSE;
			m->ndev++;
		}
		if (i < m->ndev)
			dev_register(m, i);
	}
	else if (i < m->ndev)
	{
		if (m->dev[i].registered && m->rdpdr)
		{
			const uint32_t ids[1] = { m->dev[i].id };
			m->rdpdr->RdpdrUnregisterDevice(m->rdpdr, 1, ids);
		}
		free(m->dev[i].name);
		free(m->dev[i].path);
		m->dev[i] = m->dev[m->ndev - 1];
		m->ndev--;
	}
	LeaveCriticalSection(&m->lock);
}

EXPORT void moavi_rdp_clipboard_on(void* handle, int on)
{
	moavi_context* m = M((rdpContext*)handle);
	m->clip_on = on ? TRUE : FALSE;
	if (m->cliprdr)
		clip_send_format_list(m);
}

EXPORT void moavi_rdp_disconnect(void* handle)
{
	freerdp_abort_connect_context((rdpContext*)handle);
}

EXPORT void moavi_rdp_free(void* handle)
{
	freerdp_client_context_free((rdpContext*)handle);
}
