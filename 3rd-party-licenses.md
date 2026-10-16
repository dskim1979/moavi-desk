# 오픈소스 고지 (Open-source notices)

MOAVI Desk 클라이언트는 [LICENSE.md](LICENSE.md)(MIT)에 따라 배포됩니다.
아래 오픈소스 구성요소를 포함하거나 함께 사용합니다.

## 포함된 라이브러리

| 구성요소 | 라이선스 |
|---|---|
| [Avalonia](https://github.com/AvaloniaUI/Avalonia) | MIT |
| [YamlDotNet](https://github.com/aaubry/YamlDotNet) | MIT |
| [cv4pve-vdi](https://github.com/Corsinvest/cv4pve-vdi) (Corsinvest Srl): 키오스크 잠금, 설정 저장, 화면 도우미 등 일부 코드의 출처 | MIT |

## 함께 쓰는 프로그램

| 구성요소 | 라이선스 | 비고 |
|---|---|---|
| [virt-viewer / remote-viewer](https://gitlab.com/virt-viewer/virt-viewer) | GPL-2.0-or-later | SPICE·VNC 화면 표시. 별도 프로그램으로 실행되며 클라이언트와 링크되지 않습니다 |
| [UsbDk](https://github.com/daynix/UsbDk) | Apache-2.0 | SPICE USB 리디렉션 드라이버 |

설치 프로그램(MSI)은 virt-viewer를 수정하지 않은 채 설치 폴더의 `virt-viewer\`에 함께 넣고, 소스를 얻는 방법을
`virt-viewer\SOURCE.txt`에 둡니다. UsbDk는 넣지 않습니다. 자세한 내용은 [README.md](README.md)의 설치 프로그램 절.
