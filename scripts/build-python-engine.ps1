param(
    [ValidateSet("amd64", "x86")]
    [string]$Arch = "amd64",
    [string]$PythonExe = ""
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$source = Join-Path $root "app\SpoofGUI\EngineSource\main.py"
$requirements = Join-Path $root "app\SpoofGUI\EngineSource\requirements.txt"
$dist = Join-Path $root "app\SpoofGUI\Engine"
$work = Join-Path $root "build\pyinstaller-work-$Arch"
$spec = Join-Path $root "build\pyinstaller-spec-$Arch"

$pyExe = if ($PythonExe) { $PythonExe } else { "python" }

function Invoke-Py {
    param([string[]]$PyArgs)
    & $pyExe @PyArgs
    if ($LASTEXITCODE -ne 0) { throw "python failed: $($PyArgs -join ' ')" }
}

function Read-Py {
    param([string]$Code)
    return (& $pyExe @("-c", $Code)).Trim()
}

$reportedBits = Read-Py "import struct;print(struct.calcsize('P')*8)"
$expectedBits = if ($Arch -eq "x86") { "32" } else { "64" }
if ($reportedBits -ne $expectedBits) {
    throw "Active Python is $reportedBits-bit but $Arch needs $expectedBits-bit. Pass -PythonExe with a matching interpreter (a 32-bit Python for x86)."
}

$reportedVersion = Read-Py "import sys;print('%d.%d' % sys.version_info[:2])"
if ($reportedVersion -ne "3.11") {
    Write-Warning "Building the engine with Python $reportedVersion. The shipped engine is built with 3.11; other versions can produce an engine that starts but passes no traffic. Use: uv venv --seed --python 3.11 build\engine-venv-311 and pass -PythonExe build\engine-venv-311\Scripts\python.exe"
}

Invoke-Py @("-m", "pip", "install", "pyinstaller")
Invoke-Py @("-m", "pip", "install", "-r", $requirements)

$pydivertDir = Read-Py "import pydivert, os; print(os.path.join(os.path.dirname(pydivert.__file__), 'windivert_dll'))"
$winDivertFiles = Get-ChildItem -Path $pydivertDir -Include *.dll, *.sys -File -Recurse

$addBinaryArgs = @()
foreach ($file in $winDivertFiles) {
    $addBinaryArgs += @("--add-binary", "$($file.FullName);pydivert/windivert_dll")
}

$pyInstallerArgs = @("-m", "PyInstaller", "--clean", "--onefile", "--console") +
    $addBinaryArgs +
    @("--name", "SpoofGUI.SniSpoofEngine", "--distpath", $dist, "--workpath", $work, "--specpath", $spec, $source)

Invoke-Py $pyInstallerArgs

foreach ($file in $winDivertFiles) {
    Copy-Item -Force $file.FullName (Join-Path $dist $file.Name)
}
