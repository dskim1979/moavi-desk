# MOAVI Desk on FreeRDP's Android session screen: adds MoaviBar.java (the bar) and moavi_android.c (the 장치 menu's
# drives during the session) to freeRDPCore and hooks them in:
# - SessionActivity: MoaviBar.attach after the views are set up, accessors for the bar, the clipboard switch
# - android_freerdp.c: the device channel always (DeviceRedirection) and handed to moavi_android.c when it connects
# - the native library's CMakeLists: moavi_android.c
# Run by installer/android/build-freerdp.sh on the FreeRDP checkout; each step does nothing the second time.
#   python3 patch.py <freerdp-source>
import os
import shutil
import sys

src = sys.argv[1]
main = os.path.join(src, 'client', 'Android', 'Studio', 'freeRDPCore', 'src', 'main')
pkg = os.path.join(main, 'java', 'com', 'freerdp', 'freerdpcore', 'presentation')
cpp = os.path.join(main, 'cpp')
here = os.path.dirname(os.path.abspath(__file__))
shutil.copy(os.path.join(here, 'MoaviBar.java'), pkg)
shutil.copy(os.path.join(here, 'moavi_android.c'), cpp)


def patch(path, marker, steps):
    s = open(path, encoding='utf-8').read()
    if marker in s:
        return
    for kind, anchor, text, start in steps:
        base = s.index(start) if start else 0
        i = s.index(anchor, base)
        if kind == 'after':
            j = s.index('\n', i) + 1
            s = s[:j] + text + s[j:]
        else:
            s = s[:i] + text + s[i:]
    open(path, 'w', encoding='utf-8').write(s)
    print('patched', path)


patch(os.path.join(pkg, 'SessionActivity.java'), 'moaviInstance', [
    ('after', 'private SessionInputManager inputManager;',
     '\t/* MOAVI Desk (MoaviBar) */\n'
     '\tpublic long moaviInstance()\n\t{\n\t\treturn session != null ? session.getInstance() : 0;\n\t}\n'
     '\tpublic void moaviToggleKeyboard()\n\t{\n\t\tif (inputManager != null)\n\t\t\tinputManager.toggleKeyboard();\n\t}\n', None),
    ('after', 'railManager = new RailWindowManager(this, findViewById(R.id.railContainer), sessionView);',
     '\t\tMoaviBar.attach(this);\n', None),
    # the 장치 menu's clipboard switch: nothing goes either way while it is off
    ('after', 'Log.v(TAG, "OnRemoteClipboardChanged: " + data);', '\t\tif (!MoaviBar.clipboardOn)\n\t\t\treturn;\n', None),
    ('after', 'Log.v(TAG, "onClipboardChanged: " + data);', '\t\tif (!MoaviBar.clipboardOn)\n\t\t\treturn;\n', None),
])

patch(os.path.join(cpp, 'android_freerdp.c'), 'moavi_rdpdr_connected', [
    ('before', 'static void android_OnChannelConnectedEventHandler(void* context,',
     '#include <freerdp/client/rdpdr.h>\n'
     '/* MOAVI Desk: moavi_android.c */\n'
     'void moavi_rdpdr_connected(rdpContext* context, RdpdrClientContext* rdpdr);\n\n', None),
    ('before', '\telse if (strcmp(e->name, RDPGFX_DVC_CHANNEL_NAME) == 0)',
     '\telse if (strcmp(e->name, RDPDR_SVC_CHANNEL_NAME) == 0)\n'
     '\t{\n\t\tmoavi_rdpdr_connected(&afc->common.context, (RdpdrClientContext*)e->pInterface);\n'
     '\t\tfreerdp_client_OnChannelConnectedEventHandler(context, e);\n\t}\n',
     'static void android_OnChannelConnectedEventHandler(void* context,'),
    ('before', '\telse\n\t\tfreerdp_client_OnChannelDisconnectedEventHandler(context, e);',
     '\telse if (strcmp(e->name, RDPDR_SVC_CHANNEL_NAME) == 0)\n'
     '\t{\n\t\tmoavi_rdpdr_connected(&afc->common.context, NULL);\n'
     '\t\tfreerdp_client_OnChannelDisconnectedEventHandler(context, e);\n\t}\n',
     'static void android_OnChannelDisconnectedEventHandler(void* context,'),
    # the device channel always, so a drive can come in during the session
    ('after', 'rdpSettings* settings = instance->context->settings;',
     '\tif (settings && !freerdp_settings_set_bool(settings, FreeRDP_DeviceRedirection, TRUE))\n\t\treturn FALSE;\n',
     'static BOOL android_pre_connect(freerdp* instance)'),
])

patch(os.path.join(cpp, 'CMakeLists.txt'), 'moavi_android.c', [
    ('before', '    android_rail.c\n', '    moavi_android.c\n', None),
])
print('MOAVI Desk hooked into', main)
