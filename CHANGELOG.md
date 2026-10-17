# 변경 기록

## [Unreleased]

### 추가
- RDP 우선 접속: 풀이 RDP를 허용하면 `.rdp` 파일을 Windows 원격 데스크톱 연결(mstsc)로 엽니다(H.264 · UDP).
  RDP 서버가 없는 Linux 데스크톱이면 SPICE · VNC로 자동 전환합니다. 설정의 접속 방식에 RDP 추가.
- 연구자 네이티브 SSH(Warpgate): 풀이 "연구자 네이티브 SSH"를 허용하면, 데스크톱 목록을 새로 고칠 때
  `%USERPROFILE%\.ssh\config` 의 관리 블록(BEGIN/END MOAVI)에 데스크톱별 `Host moavi-<이름>` 항목을
  자동으로 써 넣습니다(블록 밖은 건드리지 않음). PuTTY · scp · WinSCP · VS Code Remote-SSH 로 데스크톱
  이름만 고르면 Warpgate를 거쳐 접속됩니다(대상 IP 숨김, 세션 녹화). 공개키만 올리며 개인키는 다루지
  않습니다. 실제 Warpgate + 클러스터에서 검증 필요(`SshConfigSync`).

## [0.9.0] - 2026-10-16

### 추가
- MOAVI Desk 서버에 로그인(ID·비밀번호, 필요할 때 OTP)하고 배정된 데스크톱을 카드로 보기
- SPICE(.vv 파일)와 VNC(콘솔 웹소켓 브리지)로 remote-viewer 접속, 뷰어를 닫으면 세션 종료
- 데스크톱 전원: 시작, 다시 시작, 종료
- 설정: 서버 주소·포트·HTTPS와 접속 테스트, 뷰어 경로와 프로토콜, LAB 단말 키, 키오스크 잠금
- 로그인 화면의 서버 연결 상태 표시(서버 주소는 표시하지 않음)
- LAB PC: 단말 키로 현재 수업 데스크톱을 바로 열고, 닫으면 로그아웃
- 엔진이 알려 주는 최신·최소 버전에 따라 업데이트 안내
- 한국어·영어 화면
