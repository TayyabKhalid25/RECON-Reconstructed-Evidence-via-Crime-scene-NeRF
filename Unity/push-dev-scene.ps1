<#
.SYNOPSIS
  Pushes a .ply (and its metadata.json) to the phone for DevSplatLoader (FTW-30 / I1), and pulls
  PerfLog output back.

.EXAMPLE
  .\Unity\push-dev-scene.ps1 -Ply D:\exports\1\splat_unity_deg0.ply -Metadata D:\exports\1\metadata.json
  .\Unity\push-dev-scene.ps1 -PullPerf        # copies persistentDataPath/perf/*.json to Unity/Recon/Builds/perf
#>
param(
    [string]$Ply = "",
    [string]$Metadata = "",
    [string]$Package = "com.Fastians.Recon",
    [string]$UnityVersion = "6000.3.22f1",
    [switch]$PullPerf
)
$ErrorActionPreference = "Stop"
$unityDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$adb = "C:\Program Files\Unity\Hub\Editor\$UnityVersion\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe"
if (-not (Test-Path $adb)) { $adb = "adb" }
$dest = "/sdcard/Android/data/$Package/files"

if ($Ply) {
    if (-not (Test-Path $Ply)) { throw "No file at $Ply" }
    & $adb shell mkdir -p "$dest/scenes/dev"
    & $adb push $Ply "$dest/scenes/dev/splat_unity.ply"
    if ($LASTEXITCODE -ne 0) { throw "adb push failed. Has the app been launched once so the folder exists, and is USB debugging authorised?" }
    if ($Metadata) {
        if (-not (Test-Path $Metadata)) { throw "No file at $Metadata" }
        & $adb push $Metadata "$dest/scenes/dev/metadata.json"
    } else {
        Write-Host "No -Metadata given: the scene will render NOT METRIC (fine for FPS only)." -ForegroundColor Yellow
    }
    & $adb shell ls -la "$dest/scenes/dev"
}

if ($PullPerf) {
    $out = Join-Path $unityDir "Recon\Builds\perf"
    New-Item -ItemType Directory -Force $out | Out-Null
    & $adb pull "$dest/perf" $out
    Get-ChildItem (Join-Path $out "perf") -Filter *.json -ErrorAction SilentlyContinue | Sort-Object LastWriteTime | ForEach-Object {
        $j = Get-Content $_.FullName -Raw | ConvertFrom-Json
        if ($j.summary) {
            Write-Host ("{0}: {1} | {2} | MT={3} | splats={4} | n={5} p5={6:N1} median={7:N1} mean={8:N1}" -f $_.Name, $j.device, $j.graphicsApi, $j.multithreadedRendering, ($j.samples | Select-Object -Last 1).splats, $j.summary.count, $j.summary.p5, $j.summary.median, $j.summary.mean)
        }
    }
}
