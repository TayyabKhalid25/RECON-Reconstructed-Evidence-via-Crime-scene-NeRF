<#
.SYNOPSIS
  Batch-mode Android build of Unity/Recon, optionally installed to the connected phone.

.EXAMPLE
  .\Unity\build-android.ps1                  # Builds/Recon.apk
  .\Unity\build-android.ps1 -Install         # then adb install -r
  .\Unity\build-android.ps1 -Development     # development build with profiler connection
  .\Unity\build-android.ps1 -Bootstrap       # (re)create Assets/Recon/Scenes/ReconAR.unity first

  The Editor must be installed via Unity Hub at the exact version in ProjectVersion.txt. This
  script never installs Unity; it fails fast if the version is missing.
#>
param(
    [string]$UnityVersion = "6000.3.22f1",
    [string]$Output = "",
    [switch]$Development,
    [switch]$Install,
    [switch]$Bootstrap
)
$ErrorActionPreference = "Stop"

$unityDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$project  = Join-Path $unityDir "Recon"
$unity    = "C:\Program Files\Unity\Hub\Editor\$UnityVersion\Editor\Unity.exe"
if (-not (Test-Path $unity)) { throw "Unity $UnityVersion not found at $unity. Install that exact version via Unity Hub (see Unity/README.md)." }

$buildDir = Join-Path $project "Builds"
New-Item -ItemType Directory -Force $buildDir | Out-Null
if (-not $Output) { $Output = Join-Path $buildDir "Recon.apk" }

function Invoke-Unity([string[]]$UnityArgs, [string]$LogName) {
    $log = Join-Path $buildDir $LogName
    $all = @("-batchmode", "-nographics", "-projectPath", $project, "-logFile", $log) + $UnityArgs
    Write-Host "Unity $UnityVersion $($UnityArgs -join ' ')"
    $p = Start-Process -FilePath $unity -ArgumentList $all -Wait -PassThru -NoNewWindow
    if ($p.ExitCode -ne 0) {
        Write-Host "---- last 80 log lines ($log) ----"
        Get-Content $log -Tail 80
        throw "Unity exited with $($p.ExitCode)"
    }
}

if ($Bootstrap) {
    Invoke-Unity @("-buildTarget", "Android", "-executeMethod", "Recon.Editor.SceneBootstrap.CreateReconArSceneBatch") "bootstrap.log"
}

$buildArgs = @("-buildTarget", "Android", "-executeMethod", "Recon.Editor.BuildScript.BuildAndroid", "-outputPath", $Output)
if ($Development) { $buildArgs += "-development" }
Invoke-Unity $buildArgs "build.log"
Write-Host "Built $Output ($([math]::Round((Get-Item $Output).Length / 1MB, 1)) MB)"

if ($Install) {
    $adb = Join-Path (Split-Path $unity) "Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe"
    if (-not (Test-Path $adb)) { $adb = "adb" }
    & $adb install -r $Output
    if ($LASTEXITCODE -ne 0) { throw "adb install failed ($LASTEXITCODE). Is USB debugging on and the phone authorised?" }
    Write-Host "Installed. Launch: adb shell monkey -p com.Fastians.Recon 1"
}
