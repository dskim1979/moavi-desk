/*
 * SPDX-FileCopyrightText: Copyright (주)매커스시스템즈
 * SPDX-License-Identifier: MIT
 *
 * MOAVI Desk on FreeRDP's Android session screen: the 장치 menu adds and takes away the phone's storage during the
 * session, as the macOS window does (installer/macos/moavi-rdp). patch.py adds this file to freeRDPCore's native
 * library and has android_freerdp.c hand over the device channel (moavi_rdpdr_connected) when it connects.
 * A drive asked for before the channel is up waits and goes in when it connects.
 */
#include <jni.h>
#include <stdlib.h>
#include <string.h>

#include <winpr/crt.h>
#include <winpr/synch.h>
#include <freerdp/freerdp.h>
#include <freerdp/settings.h>
#include <freerdp/client/rdpdr.h>
#include <freerdp/channels/rdpdr.h>

typedef struct
{
	int token;
	char* name;
	char* path;
	uint32_t id;
	BOOL registered;
	rdpContext* context; /* NULL: waiting for the next device channel */
} moavi_drive;

static moavi_drive drives[32];
static int ndrives = 0;
static struct
{
	rdpContext* context;
	RdpdrClientContext* rdpdr;
} channels[8];
static CRITICAL_SECTION lock;
static INIT_ONCE once = INIT_ONCE_STATIC_INIT;

static BOOL CALLBACK init_lock(PINIT_ONCE o, PVOID p, PVOID* c)
{
	InitializeCriticalSection(&lock);
	return TRUE;
}

static RdpdrClientContext* rdpdr_of(rdpContext* context)
{
	for (int i = 0; i < 8; i++)
		if (channels[i].context == context)
			return channels[i].rdpdr;
	return NULL;
}

static void drive_register(moavi_drive* d, RdpdrClientContext* rdpdr)
{
	if (d->registered || !rdpdr)
		return;
	const char* args[2] = { d->name, d->path };
	RDPDR_DEVICE* dev = freerdp_device_new(RDPDR_DTYP_FILESYSTEM, 2, args);
	if (!dev)
		return;
	uint32_t id = 0;
	if (rdpdr->RdpdrRegisterDevice(rdpdr, dev, &id) == CHANNEL_RC_OK)
	{
		d->id = id;
		d->registered = TRUE;
	}
	freerdp_device_free(dev);
}

/* android_freerdp.c: the device channel of a session connected / went away (rdpdr = NULL) */
void moavi_rdpdr_connected(rdpContext* context, RdpdrClientContext* rdpdr)
{
	InitOnceExecuteOnce(&once, init_lock, NULL, NULL);
	EnterCriticalSection(&lock);
	int slot = -1;
	for (int i = 0; i < 8; i++)
		if (channels[i].context == context || (slot < 0 && !channels[i].context))
			slot = i;
	if (slot >= 0)
	{
		channels[slot].context = rdpdr ? context : NULL;
		channels[slot].rdpdr = rdpdr;
	}
	if (rdpdr)
	{
		for (int i = 0; i < ndrives; i++)
		{
			if (!drives[i].context)
				drives[i].context = context;
			if (drives[i].context == context)
				drive_register(&drives[i], rdpdr);
		}
	}
	else
	{
		/* the session is over: forget its drives */
		for (int i = 0; i < ndrives;)
		{
			if (drives[i].context == context)
			{
				free(drives[i].name);
				free(drives[i].path);
				drives[i] = drives[--ndrives];
			}
			else
				i++;
		}
	}
	LeaveCriticalSection(&lock);
}

/* MoaviBar.drive(instance, token, name, path, on): instance 0 = for the session about to connect */
JNIEXPORT void JNICALL Java_com_freerdp_freerdpcore_presentation_MoaviBar_drive(JNIEnv* env, jclass cls, jlong instance,
                                                                                 jint token, jstring jname, jstring jpath,
                                                                                 jboolean on)
{
	InitOnceExecuteOnce(&once, init_lock, NULL, NULL);
	freerdp* inst = (freerdp*)(intptr_t)instance;
	rdpContext* context = inst ? inst->context : NULL;
	const char* name = (*env)->GetStringUTFChars(env, jname, NULL);
	const char* path = (*env)->GetStringUTFChars(env, jpath, NULL);
	EnterCriticalSection(&lock);
	int i = 0;
	while (i < ndrives && !(drives[i].token == token && drives[i].context == context))
		i++;
	if (on)
	{
		if (i == ndrives && ndrives < 32)
		{
			drives[i].token = token;
			drives[i].name = _strdup(name);
			drives[i].path = _strdup(path);
			drives[i].registered = FALSE;
			drives[i].context = context;
			ndrives++;
		}
		if (i < ndrives && context)
			drive_register(&drives[i], rdpdr_of(context));
	}
	else if (i < ndrives)
	{
		RdpdrClientContext* rdpdr = context ? rdpdr_of(context) : NULL;
		if (drives[i].registered && rdpdr)
		{
			const uint32_t ids[1] = { drives[i].id };
			(void)rdpdr->RdpdrUnregisterDevice(rdpdr, 1, ids);
		}
		free(drives[i].name);
		free(drives[i].path);
		drives[i] = drives[--ndrives];
	}
	LeaveCriticalSection(&lock);
	(*env)->ReleaseStringUTFChars(env, jname, name);
	(*env)->ReleaseStringUTFChars(env, jpath, path);
}
