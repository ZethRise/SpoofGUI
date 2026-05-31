param(
    [ValidateSet("amd64", "x86")]
    [string[]]$Arch = @("x86", "amd64"),
    [string]$Version = "1.0.3",
    [string]$SingBoxVersion = "1.13.12",
    [string]$WintunVersion = "0.14.1",
    [switch]$SkipEngine,
    [switch]$SkipFetch,
    [switch]$PortableOnly,
    [string]$PythonExeAmd64 = "",
    [string]$PythonExeX86 = ""
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$appCsproj = Join-Path $root "app\SpoofGUI\SpoofGUI.csproj"
$engineSource = Join-Path $root "app\SpoofGUI\EngineSource"
$engineDir = Join-Path $root "app\SpoofGUI\Engine"
$xrayDir = Join-Path $root "app\SpoofGUI\Xray"
$distDir = Join-Path $root "dist"
$stageRoot = Join-Path $distDir "stage"
$launcherDir = Join-Path $root "launcher"

function Find-Iscc {
    foreach ($candidate in @(
            "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
            "C:\Program Files\Inno Setup 6\ISCC.exe")) {
        if (Test-Path $candidate) { return $candidate }
    }
    throw "Inno Setup compiler (ISCC.exe) not found."
}

function Import-VcEnv {
    param([string]$VcArch)
    $vswhere = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\Installer\vswhere.exe"
    if (-not (Test-Path $vswhere)) { throw "vswhere.exe not found; install Visual Studio C++ build tools." }
    $vsPath = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
    if (-not $vsPath) { throw "MSVC C++ build tools not found." }
    $vcvars = Join-Path $vsPath "VC\Auxiliary\Build\vcvarsall.bat"
    if (-not (Test-Path $vcvars)) { throw "vcvarsall.bat not found under $vsPath." }
    $captured = cmd /c "`"$vcvars`" $VcArch >nul 2>&1 && set"
    foreach ($line in $captured) {
        if ($line -match "^([^=]+)=(.*)$") {
            Set-Item -Path "env:$($matches[1])" -Value $matches[2]
        }
    }
}

function Expand-DownloadedZip {
    param([string]$Url, [string]$ExtractTo)
    $zipPath = Join-Path $env:TEMP ([System.IO.Path]::GetRandomFileName() + ".zip")
    Invoke-WebRequest -Uri $Url -OutFile $zipPath -UseBasicParsing
    if (Test-Path $ExtractTo) { Remove-Item -Recurse -Force $ExtractTo }
    Expand-Archive -Path $zipPath -DestinationPath $ExtractTo -Force
    Remove-Item -Force $zipPath
}

function Get-Xray {
    param([string]$ArchName)
    $asset = if ($ArchName -eq "x86") { "Xray-windows-32.zip" } else { "Xray-windows-64.zip" }
    $url = "https://github.com/XTLS/Xray-core/releases/latest/download/$asset"
    $tmp = Join-Path $env:TEMP "spoofgui-xray-$ArchName"
    Expand-DownloadedZip -Url $url -ExtractTo $tmp
    Copy-Item -Force (Join-Path $tmp "xray.exe") (Join-Path $xrayDir "xray.exe")
    Remove-Item -Recurse -Force $tmp
}

function Get-SingBox {
    param([string]$ArchName)
    $goArch = if ($ArchName -eq "x86") { "386" } else { "amd64" }
    $url = "https://github.com/SagerNet/sing-box/releases/download/v$SingBoxVersion/sing-box-$SingBoxVersion-windows-$goArch.zip"
    $tmp = Join-Path $env:TEMP "spoofgui-singbox-$ArchName"
    Expand-DownloadedZip -Url $url -ExtractTo $tmp
    $exe = Get-ChildItem -Recurse -Path $tmp -Filter "sing-box.exe" | Select-Object -First 1
    Copy-Item -Force $exe.FullName (Join-Path $engineDir "sing-box.exe")
    Remove-Item -Recurse -Force $tmp
}

function Get-Wintun {
    param([string]$ArchName)
    $url = "https://www.wintun.net/builds/wintun-$WintunVersion.zip"
    $tmp = Join-Path $env:TEMP "spoofgui-wintun-$ArchName"
    Expand-DownloadedZip -Url $url -ExtractTo $tmp
    $dll = Join-Path $tmp "wintun\bin\$ArchName\wintun.dll"
    if (-not (Test-Path $dll)) { throw "wintun.dll for $ArchName not found in archive." }
    Copy-Item -Force $dll (Join-Path $engineDir "wintun.dll")
    Copy-Item -Force $dll (Join-Path $xrayDir "wintun.dll")
    Remove-Item -Recurse -Force $tmp
}

function Build-Launcher {
    param([string]$ArchName, [string]$OutExe)
    $vcArch = if ($ArchName -eq "x86") { "x86" } else { "x64" }
    Import-VcEnv -VcArch $vcArch
    Copy-Item -Force (Join-Path $root "app\SpoofGUI\Assets\SpoofGUI.ico") (Join-Path $launcherDir "SpoofGUI.ico")
    Push-Location $launcherDir
    try {
        & rc /nologo /fo launcher.res launcher.rc
        if ($LASTEXITCODE -ne 0) { throw "rc.exe failed for $ArchName launcher." }
        & cl /nologo /O2 /MT /EHsc launcher.cpp launcher.res /Fe:$OutExe /link /SUBSYSTEM:WINDOWS shell32.lib user32.lib
        if ($LASTEXITCODE -ne 0) { throw "cl.exe failed for $ArchName launcher." }
        Remove-Item -Force launcher.res, launcher.obj -ErrorAction SilentlyContinue
    }
    finally { Pop-Location }
}

function Assert-Exists {
    param([string]$Path, [string]$What)
    if (-not (Test-Path $Path)) { throw "Missing $What : $Path" }
}

if (Get-Process -Name "SpoofGUI" -ErrorAction SilentlyContinue) {
    throw "SpoofGUI.exe is running. Close the app before building release packages."
}
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { throw "dotnet SDK not found in PATH." }
$iscc = if ($PortableOnly) { $null } else { Find-Iscc }

New-Item -ItemType Directory -Force -Path $distDir | Out-Null
New-Item -ItemType Directory -Force -Path $stageRoot | Out-Null

foreach ($a in $Arch) {
    Write-Host "=== Building SpoofGUI $Version ($a) ==="
    $rid = if ($a -eq "x86") { "win-x86" } else { "win-x64" }
    $platform = if ($a -eq "x86") { "x86" } else { "x64" }
    $publishDir = Join-Path $distDir "publish-$a"
    $stageArch = Join-Path $stageRoot $a

    if (-not $SkipEngine) {
        $pythonExe = if ($a -eq "x86") { $PythonExeX86 } else { $PythonExeAmd64 }
        & (Join-Path $PSScriptRoot "build-python-engine.ps1") -Arch $a -PythonExe $pythonExe
        if ($LASTEXITCODE -ne 0) { throw "Python engine build failed for $a." }
    }

    Assert-Exists (Join-Path $engineDir "SpoofGUI.SniSpoofEngine.exe") "engine exe"

    if (-not $SkipFetch) {
        Get-Xray -ArchName $a
        Get-SingBox -ArchName $a
        Get-Wintun -ArchName $a
    }

    Assert-Exists (Join-Path $xrayDir "xray.exe") "xray.exe"
    Assert-Exists (Join-Path $engineDir "sing-box.exe") "sing-box.exe"
    Assert-Exists (Join-Path $engineDir "wintun.dll") "wintun.dll"

    if (Test-Path $publishDir) { Remove-Item -Recurse -Force $publishDir }
    & dotnet publish $appCsproj -c Release -p:Platform=$platform -r $rid --self-contained true `
        -p:PublishSingleFile=false -p:PublishTrimmed=false -o $publishDir
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed for $a." }

    Assert-Exists (Join-Path $publishDir "SpoofGUI.exe") "published SpoofGUI.exe"
    Assert-Exists (Join-Path $publishDir "Xray\xray.exe") "published Xray\xray.exe"
    Assert-Exists (Join-Path $publishDir "engine\SpoofGUI.SniSpoofEngine.exe") "published engine exe"
    Assert-Exists (Join-Path $publishDir "engine\sing-box.exe") "published sing-box.exe"
    Assert-Exists (Join-Path $publishDir "engine\wintun.dll") "published wintun.dll"

    $sourceDest = Join-Path $publishDir "source\SNI-Spoofing"
    robocopy $engineSource $sourceDest /E /XD .git __pycache__ /XF *.pyc | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "Failed to copy SNI-Spoofing source for $a." }

    if (Test-Path $stageArch) { Remove-Item -Recurse -Force $stageArch }
    $appSub = Join-Path $stageArch "app"
    New-Item -ItemType Directory -Force -Path $appSub | Out-Null
    Copy-Item -Path (Join-Path $publishDir "*") -Destination $appSub -Recurse -Force
    Build-Launcher -ArchName $a -OutExe (Join-Path $stageArch "SpoofGUI.exe")
    Assert-Exists (Join-Path $stageArch "SpoofGUI.exe") "launcher SpoofGUI.exe"

    $zipPath = Join-Path $distDir "SpoofGUI-Portable-$a.zip"
    if (Test-Path $zipPath) { Remove-Item -Force $zipPath }
    Compress-Archive -Path (Join-Path $stageArch "*") -DestinationPath $zipPath -CompressionLevel Optimal
    Write-Host "Portable: $zipPath"

    if ($PortableOnly)
    {
        Write-Host "Setup skipped (-PortableOnly)"
        continue
    }

    $env:SPOOFGUI_VERSION = $Version
    $env:SPOOFGUI_ROOT = $root
    $env:SPOOFGUI_STAGE_DIR = $stageArch
    $env:SPOOFGUI_DIST_DIR = $distDir
    $env:SPOOFGUI_ARCH = $a
    $setupOld = Join-Path $distDir "SpoofGUI-Setup-$a.exe"
    if (Test-Path $setupOld) { Remove-Item -Force $setupOld }
    & $iscc (Join-Path $root "installer\SpoofGUI.iss")
    if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed for $a." }
    Write-Host "Setup: $setupOld"
}

Write-Host ""
Write-Host "Done. Artifacts in $distDir"
