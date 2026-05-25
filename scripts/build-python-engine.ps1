$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$source = Join-Path $root "app\SpoofGUI\EngineSource\main.py"
$dist = Join-Path $root "app\SpoofGUI\Engine"
$work = Join-Path $root "build\pyinstaller-work"
$spec = Join-Path $root "build\pyinstaller-spec"

python -m pip install -r (Join-Path $root "_reference\SNI-Spoofing\requirements.txt")

$pydivertDir = python -c "import pydivert, os; print(os.path.join(os.path.dirname(pydivert.__file__), 'windivert_dll'))"
$pydivertDir = $pydivertDir.Trim()

python -m PyInstaller `
  --clean `
  --onefile `
  --noconsole `
  --add-binary "$pydivertDir\WinDivert64.dll;pydivert/windivert_dll" `
  --add-binary "$pydivertDir\WinDivert64.sys;pydivert/windivert_dll" `
  --name "SpoofGUI.SniSpoofEngine" `
  --distpath $dist `
  --workpath $work `
  --specpath $spec `
  $source
