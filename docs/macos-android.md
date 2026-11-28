# MOAVI Desk for macOS and Android

Windows 클라이언트와 같은 화면(로그인, 데스크톱 목록·세부 패널, 설정)을 `src/MoaviDesk.Core`에서 함께 씁니다.
RDP 연결부만 플랫폼별로 다릅니다.

| | Windows | macOS | Android |
|---|---|---|---|
| 프로젝트 | `src/MoaviDesk` | `src/MoaviDesk.Mac` | `src/MoaviDesk.Android` |
| RDP | Windows 원격 데스크톱 컨트롤(MOAVI Desk 창) | FreeRDP 3(`libmoavi-rdp`, MOAVI Desk 창 안) | FreeRDP 3 Android 세션 화면(APK 안에 포함, H.264) |
| 장치 연결 방식(rdpgw) | `GatewayEncryptedAuthCookie` | `/gateway:…,access-token:` | 같음 |
| Warpgate | 티켓 = 사용자 이름 | 티켓 = 사용자 이름, `/sec:tls` | 같음 |
| SPICE / VNC | 포함된 remote-viewer | `brew install virt-viewer` | VNC: 앱 안 뷰어, SPICE: aSPICE 앱으로 넘김 |
| 비밀번호 기억 | DPAPI | 키체인 | Android Keystore |
| 배포 | MSI | `.dmg`(arm64, x64) | APK |

`.rdp`를 FreeRDP 옵션으로 바꾸는 곳은 `src/MoaviDesk.Core/Services/FreeRdpArgs.cs`입니다.

## 빌드

CI(`.github/workflows/ci.yml`)의 `macos`, `android` 작업이 산출물을 만듭니다(Actions → 실행 → Artifacts).
FreeRDP는 소스에서 빌드하고 버전별로 캐시합니다. 첫 실행은 오래 걸립니다.

- macOS: `installer/macos/build-freerdp.sh` → `installer/macos/build.sh osx-arm64` (macOS, Xcode 명령줄 도구, Homebrew의 autoconf automake libtool meson ninja nasm)
- Android: `installer/android/build-freerdp.sh` → `dotnet publish src/MoaviDesk.Android -c Release -f net10.0-android` (Android SDK, JDK 17, `dotnet workload install android`)

### 서명

| 비밀값(GitHub Secrets) | 쓰임 |
|---|---|
| `MACOS_CERT_P12`(base64), `MACOS_CERT_PASSWORD`, `MACOS_SIGN_IDENTITY` | Developer ID Application 서명 |
| `APPLE_ID`, `APPLE_TEAM_ID`, `APPLE_APP_PASSWORD` | 공증(notarytool) |
| `ANDROID_KEYSTORE_BASE64`, `ANDROID_KEYSTORE_PASSWORD`, `ANDROID_KEY_ALIAS` | APK 서명 |

없으면 macOS 앱은 임시(ad hoc) 서명, APK는 디버그 키로 서명됩니다.

## 실제 기기에서 확인하기

### macOS
1. Actions의 `moavi-desk-macos` 산출물에서 Apple Silicon은 `…-macos-arm64.dmg`, Intel Mac은 `…-macos-x64.dmg`를 받습니다.
2. `.dmg`를 열고 MOAVI Desk를 응용 프로그램으로 끌어 놓습니다.
3. 서명 전에는 처음 한 번 Finder에서 우클릭 → 열기 → 열기를 누릅니다.
   막히면 터미널에서 `xattr -dr com.apple.quarantine "/Applications/MOAVI Desk.app"`를 실행합니다.
4. 로그인 → 데스크톱 선택 → 접속(RDP)을 누르면 FreeRDP 창이 뜹니다. 아래를 확인합니다.
   - 직접 연결, 장치 연결 방식(RD Gateway 토큰), Warpgate 경유 풀에서 각각 접속되는지
   - 처음 접속할 때 인증서 확인 창이 뜨는지
   - 클립보드, 홈 폴더 드라이브, 소리가 되는지(풀 정책·데스크톱별 장치 체크박스에 따라)
   - 창을 닫으면 목록의 세션 수가 줄어드는지
   - 마이크를 켜면 macOS가 마이크 권한을 묻는지
   - SPICE/VNC: `brew install virt-viewer` 뒤 접속 ▸ SPICE

### Android
1. Actions의 `moavi-desk-android` 산출물에서 `moavi-desk-…-android.apk`를 받아 휴대폰으로 옮깁니다.
2. 설정에서 "출처를 알 수 없는 앱 설치"를 허용하고 설치합니다.
   PC에서 설치할 때는 `adb install -r moavi-desk-…-android.apk`를 씁니다.
3. 에뮬레이터(x86_64)도 같은 APK로 확인할 수 있습니다.
4. 로그인 화면 → 설정(서버 주소) → 로그인 → 목록에서 접속을 누르면 FreeRDP 세션 화면이 열립니다.
   - 직접 연결, 장치 연결 방식, Warpgate를 각각 확인합니다.
   - 세션 화면에서 뒤로 가기·연결 끊기로 나오면 목록의 세션 수가 줄어드는지 확인합니다.
   - 휴대폰의 뒤로 가기 버튼은 맨 위 화면(대화상자, 설정)을 닫습니다.
5. 문제가 생기면 `adb logcat | grep -iE "freerdp|moavi|mono"`로 로그를 봅니다.

## 키보드(macOS 원격 창)
- ⌘는 Ctrl로 보냅니다(⌘C, ⌘V로 복사·붙여넣기).
- 오른쪽 ⌘와 막대의 "한/영" 버튼은 한/영 전환입니다.
- Control을 누른 채 클릭하면 오른쪽 클릭입니다.

## 휴대폰
- 화면이 좁으면 데스크톱을 누를 때 세부 패널(정보, 공유 폴더, 장치 선택, 시작·종료)이 목록 위에 열립니다.
- "목록으로" 또는 뒤로 가기로 닫습니다.
- VNC 화면에서는 탭이 클릭이고, 길게 누르면 오른쪽 클릭, 끌면 드래그입니다.
- 막대의 "키보드"를 누르면 휴대폰 자판이 뜹니다.

## 아직 안 되는 것
- macOS 원격 창의 "장치" 메뉴는 이번 연결의 상태만 보여 줍니다. 바꾸려면 세부 패널에서 고친 뒤 다시 접속합니다.
- Android의 RDP 화면은 FreeRDP 세션 화면이라, 위쪽 MOAVI Desk 막대가 없습니다.
- 릴리스 워크플로가 macOS·Android까지 올리는지는 다음 태그에서 확인합니다.
