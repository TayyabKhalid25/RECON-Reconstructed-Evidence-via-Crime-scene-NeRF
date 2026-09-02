<#
.SYNOPSIS
  Runs the RECON tests. Default: both the dotnet core tests (no Editor needed) and the Unity
  EditMode tests headless. -CoreOnly skips Unity, for machines without the Editor or for CI.

.EXAMPLE
  .\Unity\run-tests.ps1 -CoreOnly
  .\Unity\run-tests.ps1                 # dotnet + Unity EditMode
  .\Unity\run-tests.ps1 -Platform PlayMode
#>
param(
    [string]$UnityVersion = "6000.3.22f1",
    [ValidateSet("EditMode", "PlayMode")] [string]$Platform = "EditMode",
    [switch]$CoreOnly,
    [switch]$UnityOnly
)
$ErrorActionPreference = "Stop"
$unityDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$project  = Join-Path $unityDir "Recon"
$failed = $false

if (-not $UnityOnly) {
    Write-Host "== dotnet test Unity/Recon.Core.Tests =="
    dotnet test (Join-Path $unityDir "Recon.Core.Tests") --nologo -v minimal
    if ($LASTEXITCODE -ne 0) { $failed = $true; Write-Host "dotnet core tests FAILED" -ForegroundColor Red }
}

if (-not $CoreOnly) {
    $unity = "C:\Program Files\Unity\Hub\Editor\$UnityVersion\Editor\Unity.exe"
    if (-not (Test-Path $unity)) { throw "Unity $UnityVersion not found at $unity" }
    $outDir  = Join-Path $project "Builds"
    New-Item -ItemType Directory -Force $outDir | Out-Null
    $results = Join-Path $outDir "test-results-$Platform.xml"
    $log     = Join-Path $outDir "tests-$Platform.log"
    if (Test-Path $results) { Remove-Item $results }
    Write-Host "== Unity $Platform tests =="
    $args = @("-batchmode", "-nographics", "-projectPath", $project, "-runTests", "-testPlatform", $Platform,
              "-testResults", $results, "-logFile", $log)
    $p = Start-Process -FilePath $unity -ArgumentList $args -Wait -PassThru -NoNewWindow
    # Unity Test Framework exit codes: 0 all passed, 2 tests failed, 3 run error.
    if (Test-Path $results) {
        [xml]$xml = Get-Content $results
        $total = $xml.'test-run'.total; $passed = $xml.'test-run'.passed; $tfailed = $xml.'test-run'.failed
        Write-Host "Unity: $passed/$total passed, $tfailed failed  ($results)"
        $xml.SelectNodes("//test-case[@result='Failed']") | ForEach-Object {
            Write-Host " FAIL $($_.fullname)" -ForegroundColor Red
            $msg = $_.failure.message.'#cdata-section'; if ($msg) { Write-Host "      $msg" }
        }
        if ([int]$tfailed -gt 0) { $failed = $true }
    } else {
        Write-Host "No results file written; Unity exit code $($p.ExitCode). Last log lines:" -ForegroundColor Red
        Get-Content $log -Tail 60
        $failed = $true
    }
}

if ($failed) { exit 1 } else { Write-Host "All tests passed." -ForegroundColor Green }
