# Builds moavi-desk.exe (self-contained, win-x64) and the MOAVI Desk MSI.
#
#   ./installer/build.ps1                                   # MSI without virt-viewer
#   ./installer/build.ps1 -VirtViewerMsi virt-viewer-x64-11.0-1.0.msi -Version 0.9.1
#
# Output: artifacts/publish/win-x64/moavi-desk.exe and artifacts/msi/MOAVI-Desk-<version>-x64.msi
#
# The MSI takes these public properties for an unattended install on a room's PCs (README, Package.wxs):
#   msiexec /i MOAVI-Desk-<version>-x64.msi /qn SERVER=desk.example.com PORT=443 HTTPS=1 KIOSK=1 ENROLL_CODE=MDE-XXXX-XXXX-XXXX
#   (+ ENROLL_TOKEN=, SERVER_CERT_SHA256=<hex>, KIOSK_PASSWORD_HASH=<iterations.salt.hash>; moavi-desk-enroll.json
#   beside the .msi fills in the rest) -> %ProgramData%\MOAVI Desk\config and ...\agent; the MSI also installs the
#   MOAVI Desk Agent service (moavi-desk.exe --agent)
param(
    [string]$Version,
    [string]$VirtViewerMsi,
    [string]$Configuration = 'Release',
    # Code signing hook: set once a certificate exists (see Invoke-Sign below)
    [switch]$Sign
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$artifacts = Join-Path $root 'artifacts'
$publishDir = Join-Path $artifacts 'publish/win-x64'
$versionArgs = @()
if ($Version) { $versionArgs += "/p:Version=$Version" }

function Invoke-Sign([string[]]$files) {
    if (-not $Sign) { return }
    # TODO(code signing): no certificate yet. When one exists (Microsoft Trusted Signing or OV/EV),
    # sign here, e.g.
    #   signtool sign /fd sha256 /tr http://timestamp.digicert.com /td sha256 /sha1 $env:MOAVI_SIGN_THUMBPRINT <file>
    # or, with Trusted Signing, signtool ... /dlib Azure.CodeSigning.Dlib.dll /dmdf metadata.json <file>
    throw "Signing requested but not configured yet (installer/build.ps1, Invoke-Sign)"
}

# 1. moavi-desk.exe
dotnet publish (Join-Path $root 'src/MoaviDesk/MoaviDesk.csproj') `
    -c $Configuration -r win-x64 -o $publishDir @versionArgs
if ($LASTEXITCODE) { throw "dotnet publish failed" }
Invoke-Sign (Join-Path $publishDir 'moavi-desk.exe')

# 2. virt-viewer: unpack its MSI (administrative install) and keep the tree that holds bin\remote-viewer.exe
$wixArgs = @()
if ($VirtViewerMsi) {
    $unpacked = Join-Path $artifacts 'virt-viewer-admin'
    $virtViewer = Join-Path $artifacts 'virt-viewer'
    Remove-Item -Recurse -Force $unpacked, $virtViewer -ErrorAction SilentlyContinue
    $p = Start-Process msiexec.exe -Wait -PassThru -ArgumentList '/a', "`"$((Resolve-Path $VirtViewerMsi).Path)`"", '/qn', "TARGETDIR=`"$unpacked`""
    if ($p.ExitCode) { throw "msiexec /a failed ($($p.ExitCode))" }
    $viewer = Get-ChildItem $unpacked -Recurse -Filter remote-viewer.exe | Select-Object -First 1
    if (-not $viewer) { throw "remote-viewer.exe not found in $VirtViewerMsi" }
    Copy-Item -Recurse $viewer.Directory.Parent.FullName $virtViewer
    Copy-Item (Join-Path $PSScriptRoot 'virt-viewer/SOURCE.txt') $virtViewer
    $wixArgs += "/p:VirtViewerDir=$virtViewer"
}

# 3. MSI
dotnet build (Join-Path $PSScriptRoot 'MoaviDesk.Installer.wixproj') -c $Configuration `
    "/p:PublishDir=$publishDir/" "/p:OutputPath=$artifacts/msi/" @versionArgs @wixArgs
if ($LASTEXITCODE) { throw "MSI build failed" }
$msi = Get-ChildItem (Join-Path $artifacts 'msi') -Filter *.msi
Invoke-Sign $msi.FullName
$msi | ForEach-Object { Write-Host "MSI: $($_.FullName) ($([math]::Round($_.Length / 1MB, 1)) MB)" }
