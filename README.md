# MOAVI Desk Client (Windows)

<img src="docs/moavi.svg" alt="" height="48" align="left">

MOAVI Desk 사용자가 설치해 쓰는 Windows 클라이언트입니다. 브라우저(HTML5 콘솔) 없이 내 데스크톱을
SPICE 또는 VNC로 엽니다. (주)매커스시스템즈 MOAVI Desk 서버에 붙습니다.

| 로그인 | 설정 | 내 데스크톱 |
|---|---|---|
| ![로그인](docs/login.png) | ![설정](docs/settings.png) | ![내 데스크톱](docs/desktops.png) |

## 하는 일

- **서버 설정**: 설정(톱니바퀴)에서 서버 주소(IP 또는 도메인), 포트, HTTPS를 정하고 **접속 테스트**로
  MOAVI Desk 서버가 응답하는지(응답 시간) 확인한 뒤 저장합니다. 키오스크 모드에서는 관리자 비밀번호가 있어야 열립니다.
- **로그인**: 사용자 이름과 비밀번호만 입력합니다. 서버 주소는 화면에 나오지 않고, 대신 서버 연결 상태
  (연결됨 / 연결할 수 없음 / 설정 안 됨)를 15초마다 확인해 보여 줍니다. 계정이 OTP를 쓰면 OTP 칸이 열립니다.
- **내 데스크톱**: 배정된 데스크톱을 카드로 보여 줍니다(상태, 사양, 프로토콜). 20초마다 새로 고칩니다.
  메뉴에서 시작, 다시 시작, 종료를 할 수 있습니다.
- **접속**
  - SPICE: 엔진이 준 `.vv` 파일로 remote-viewer를 엽니다. USB, 오디오, 다중 모니터를 쓸 수 있습니다.
  - VNC: 엔진의 콘솔 웹소켓을 로컬 TCP 포트로 이어 주고(브리지), remote-viewer가 그 포트에 붙습니다.
  - 뷰어를 닫으면 세션이 끝납니다 (`/api/vlab/sessions/<id>/end`).
- **LAB PC**: 설정에 단말 키(`mdt_…`, 관리자 콘솔 → 단말 관리)를 넣으면, 로그인하자마자 그 실습실의
  현재 수업 데스크톱이 전체 화면으로 열립니다. 뷰어를 닫으면 로그아웃되어 다음 사람이 씁니다.
  키오스크 모드(전체 화면, 종료 시 관리자 비밀번호)와 함께 쓰고, 단말은 MAC으로 등록됩니다.
- **업데이트**: 엔진의 `/api/vlab/client/config`가 알려 주는 최신 버전이면 다운로드 버튼을 보여 주고,
  최소 버전보다 낮으면 로그인을 막고 다운로드 페이지를 엽니다.
- **원격지원 요청**: 화이트라벨의 지원 URL, 없으면 https://support.makussystems.co.kr/ 을 엽니다.
- 한국어와 영어. 설정은 `%USERPROFILE%\.moavi-desk\config`에 저장합니다.

## 빌드

```
dotnet build src/MoaviDesk/MoaviDesk.csproj -c Release
dotnet publish src/MoaviDesk/MoaviDesk.csproj -c Release -r win-x64
```

.NET 10 SDK가 필요합니다. 결과물은 `moavi-desk.exe` 한 파일입니다.

## 설치 프로그램 (MSI)

`installer/`에 WiX 6 프로젝트(`MoaviDesk.Installer.wixproj`, SDK 스타일이라 `dotnet build`로 빌드)가 있습니다.

```
powershell -ExecutionPolicy Bypass -File installer/build.ps1
powershell -ExecutionPolicy Bypass -File installer/build.ps1 -VirtViewerMsi virt-viewer-x64-11.0-1.0.msi -Version 0.9.1
```

`build.ps1`은 `moavi-desk.exe`를 게시(`artifacts/publish/win-x64`)하고 MSI(`artifacts/msi/MOAVI-Desk-<버전>-x64.msi`)를 만듭니다.

- 이름 MOAVI Desk, 제조사 (주)매커스시스템즈, 컴퓨터 전체 설치(per-machine), 한국어 설치 화면.
- `C:\Program Files\MOAVI Desk\moavi-desk.exe`(self-contained라 .NET 설치가 필요 없음), 시작 메뉴 바로 가기,
  LICENSE.md와 3rd-party-licenses.md. 새 버전 MSI는 이전 버전을 자동으로 바꿉니다(MajorUpgrade).
- `-VirtViewerMsi`를 주면 virt-viewer MSI를 관리 설치(`msiexec /a`)로 풀어 `<설치 폴더>\virt-viewer\`
  (`bin\remote-viewer.exe` 등)에 별도 기능(feature)으로 넣습니다. 클라이언트(`Services/Viewer.cs`의
  `FindViewer`)는 이 위치를 가장 먼저 찾고, 없으면 `C:\Program Files\VirtViewer*`를 찾습니다.
  주지 않으면 virt-viewer 없이 만들어지며, 이때는 사용자가 virt-viewer를 따로 설치해야 합니다.

### CI와 릴리스

- `.github/workflows/ci.yml`: main에 push/PR 하면 Windows에서 빌드하고 `moavi-desk.exe`와 MSI(virt-viewer 없음)를
  아티팩트로 올립니다.
- `.github/workflows/release.yml`: `v*` 태그를 push 하면 virt-viewer MSI를 받아 넣은 MSI와 exe를 만들어
  GitHub 릴리스에 붙입니다. 다른 곳(WinGet, AUR 등)에는 올리지 않습니다. `VIRT_VIEWER_SHA256`은 아직 비어 있어
  받은 파일의 해시만 출력합니다. 첫 릴리스 때 확인한 해시로 고정하세요.

### 코드 서명 (아직 없음)

인증서(Microsoft Trusted Signing 또는 OV/EV)가 생기면 두 곳을 채웁니다.

- `installer/build.ps1`의 `Invoke-Sign`: `-Sign`을 주면 exe(게시 직후)와 MSI(빌드 직후)에 서명합니다. 지금은
  설정되지 않았다는 오류를 냅니다.
- `installer/MoaviDesk.Installer.wixproj`의 `SignOutput`과 `SignMsi` 타깃: WiX가 MSI를 만들 때 서명하려면 여기에
  `signtool sign /fd sha256 /tr <타임스탬프 URL> /td sha256 ...`를 넣습니다.

서명 전에는 exe와 MSI를 실행할 때 SmartScreen 경고가 뜹니다.

### 함께 설치하는 프로그램과 라이선스

| 구성 요소 | 받는 곳 | 라이선스 | MSI에 넣는 방법 |
|---|---|---|---|
| virt-viewer 11.0 (remote-viewer) x64 | https://releases.pagure.org/virt-viewer/virt-viewer-x64-11.0-1.0.msi (공식 Windows 설치 파일, 프로젝트: https://gitlab.com/virt-viewer/virt-viewer) | GPLv2 이상. 같이 들어 있는 GTK, GLib, spice-gtk, usbredir, libusb, GStreamer 등은 LGPL 및 기타 오픈소스 | 수정하지 않고 `virt-viewer\` 폴더에 별도 기능으로 설치. `virt-viewer\SOURCE.txt`(원본: `installer/virt-viewer/SOURCE.txt`)에 소스 위치와 서면 소스 제공 약속을 둠 |
| UsbDk 1.0.22 x64 | https://github.com/daynix/UsbDk/releases (`UsbDk_1.0.22_x64.msi`) | Apache-2.0 | 넣지 않음. 드라이버라 별도 MSI로 설치해야 하며, SPICE USB 리디렉션을 쓰는 PC에만 필요 |

- MOAVI Desk는 remote-viewer를 별도 프로세스로 실행만 하고 링크하지 않습니다. 그래서 GPL/LGPL 구성 요소는
  "함께 배포되는 별개의 프로그램"으로 두고, 바이너리를 고치지 않으며, 소스를 얻는 방법(위 URL과 3년간 서면 요청 시
  제공)을 설치 폴더(`virt-viewer\SOURCE.txt`)와 설치 화면의 라이선스(`installer/License.rtf`)에 적습니다.
- virt-viewer 버전을 올리면 `release.yml`의 `VIRT_VIEWER_URL`/`VIRT_VIEWER_SHA256`과 `SOURCE.txt`의 버전, URL을 같이 바꿉니다.
- UsbDk를 한 번에 설치하려면 WiX Burn 번들(setup.exe: UsbDk MSI + MOAVI Desk MSI)이 필요합니다. 아직 만들지 않았습니다.

## 남은 일

- UsbDk를 같이 설치하는 Burn 번들(setup.exe).
- 코드 서명(위 참고).
- `VIRT_VIEWER_SHA256` 고정.
- 실제 클러스터에서 SPICE/VNC 접속 확인.

## 라이선스

MIT ([LICENSE.md](LICENSE.md)). 포함된 오픈소스 구성요소는 [3rd-party-licenses.md](3rd-party-licenses.md)에 있습니다.
