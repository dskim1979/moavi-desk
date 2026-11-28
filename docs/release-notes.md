## 설치 방법

### Windows (`MOAVI-Desk-@VERSION@-x64.msi`, `moavi-desk-@VERSION@-win-x64.exe`)
- **MSI**: 내려받아 실행하고 안내에 따라 설치합니다. 이전 버전은 자동으로 바뀝니다.
- **무인 설치**(관리자 명령 프롬프트):
  `msiexec /i MOAVI-Desk-@VERSION@-x64.msi /qn SERVER=desk.example.com PORT=443 HTTPS=1`
  (실습실 PC는 `KIOSK=1 ENROLL_CODE=…`를 더합니다)
- **exe**: 설치 없이 바로 실행하는 단일 파일입니다. SPICE/VNC용 virt-viewer는 들어 있지 않습니다.

### macOS (`moavi-desk-@VERSION@-macos-arm64.dmg` Apple Silicon, `…-macos-x64.dmg` Intel)
1. `.dmg`를 열고 MOAVI Desk를 응용 프로그램 폴더로 끌어 놓습니다.
2. 서명되지 않은 빌드는 처음 한 번 Finder에서 MOAVI Desk를 우클릭 → 열기 → 열기를 누릅니다.
   막히면 터미널에서 `xattr -dr com.apple.quarantine "/Applications/MOAVI Desk.app"`을 실행합니다.
3. RDP는 앱 안에 들어 있습니다. SPICE/VNC를 쓰려면 `brew install virt-viewer`가 필요합니다.

### Android (`moavi-desk-@VERSION@-android.apk`, Android 10 이상)
1. 휴대폰에서 APK를 내려받습니다.
2. 처음 설치할 때 "이 출처의 앱 설치 허용"을 켭니다(설정 → 앱 → 특별한 앱 접근 → 출처를 알 수 없는 앱 설치).
3. APK를 눌러 설치합니다. PC에서 설치할 때는 `adb install -r moavi-desk-@VERSION@-android.apk`를 씁니다.
