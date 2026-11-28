# MOAVI Desk's bar on FreeRDP's Android session screen (MoaviBar.java): adds the file to freeRDPCore and hooks it
# into SessionActivity. Run by installer/android/build-freerdp.sh on the FreeRDP checkout; does nothing the second time.
#   python3 patch.py <freerdp-source>
import os
import shutil
import sys

src = sys.argv[1]
pkg = os.path.join(src, 'client', 'Android', 'Studio', 'freeRDPCore', 'src', 'main', 'java', 'com', 'freerdp',
                   'freerdpcore', 'presentation')
here = os.path.dirname(os.path.abspath(__file__))
shutil.copy(os.path.join(here, 'MoaviBar.java'), pkg)

path = os.path.join(pkg, 'SessionActivity.java')
s = open(path, encoding='utf-8').read()
if 'moaviInstance' not in s:
    def insert_after(anchor, text):
        global s
        i = s.index(anchor)
        j = s.index('\n', i) + 1
        s = s[:j] + text + s[j:]

    insert_after('private SessionInputManager inputManager;',
                 '\t/* MOAVI Desk (MoaviBar) */\n'
                 '\tpublic long moaviInstance()\n\t{\n\t\treturn session != null ? session.getInstance() : 0;\n\t}\n'
                 '\tpublic void moaviToggleKeyboard()\n\t{\n\t\tif (inputManager != null)\n\t\t\tinputManager.toggleKeyboard();\n\t}\n')
    insert_after('railManager = new RailWindowManager(this, findViewById(R.id.railContainer), sessionView);',
                 '\t\tMoaviBar.attach(this);\n')
    open(path, 'w', encoding='utf-8').write(s)
print('MoaviBar hooked into', path)
