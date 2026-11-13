<#
.SYNOPSIS
  Builds the mod and runs the headless in-game self-test (Dev/AGM_SelfTest.cs).

.DESCRIPTION
  Launches RimWorld with -Headless -quicktest -agmselftest using an isolated
  save-data folder (_selftest/, git-ignored) whose mod list is Harmony + Core +
  DLCs + this mod, with dev mode on. The player's real config and saves are not
  touched, but the normal Player.log is overwritten.

  Prints load errors and the [AGM SelfTest] lines. Exit code 0 = PASS.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File tools/selftest.ps1
#>
param(
    [string]$RimWorldPath = "F:\SteamLibrary\steamapps\common\RimWorld",
    [int]$TimeoutSeconds = 300,
    [switch]$NoBuild
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$profileDir = Join-Path $root "_selftest"
$log = Join-Path $env:USERPROFILE "AppData\LocalLow\Ludeon Studios\RimWorld by Ludeon Studios\Logs\Player.log"

# Make sure 'dotnet' is on PATH in fresh shells.
$env:Path = [Environment]::GetEnvironmentVariable("Path", "Machine") + ";" + [Environment]::GetEnvironmentVariable("Path", "User")

if (-not $NoBuild) {
    Write-Host "Building..."
    $build = & dotnet build (Join-Path $root "Source\ActualGodMode\ActualGodMode.csproj") -c Release -nologo 2>&1
    if ($LASTEXITCODE -ne 0) {
        $build | Select-String "error" | ForEach-Object { Write-Host $_.Line }
        Write-Host "BUILD FAILED"
        exit 2
    }
}

# Isolated test profile.
$configDir = Join-Path $profileDir "Config"
New-Item -ItemType Directory -Force $configDir | Out-Null
$expansions = "ludeon.rimworld", "ludeon.rimworld.royalty", "ludeon.rimworld.ideology", "ludeon.rimworld.biotech", "ludeon.rimworld.anomaly", "ludeon.rimworld.odyssey"
$active = @("brrainz.harmony") + $expansions + @("jafex.actualgodmode")
$modsConfig = "<?xml version=`"1.0`" encoding=`"utf-8`"?>`n<ModsConfigData>`n  <version>1.7</version>`n  <activeMods>`n" +
    (($active | ForEach-Object { "    <li>$_</li>" }) -join "`n") + "`n  </activeMods>`n  <knownExpansions>`n" +
    (($expansions | ForEach-Object { "    <li>$_</li>" }) -join "`n") + "`n  </knownExpansions>`n</ModsConfigData>`n"
Set-Content -Path (Join-Path $configDir "ModsConfig.xml") -Value $modsConfig -Encoding utf8

# Dev mode on, so def config errors are reported.
$prefs = Join-Path $configDir "Prefs.xml"
if (Test-Path $prefs) {
    $x = Get-Content $prefs -Raw
    if ($x -match "<devMode>") { $x = $x -replace "<devMode>\w+</devMode>", "<devMode>True</devMode>" }
    else { $x = $x -replace "<PrefsData>", "<PrefsData>`n  <devMode>True</devMode>" }
    Set-Content -Path $prefs -Value $x -NoNewline -Encoding utf8
} else {
    Set-Content -Path $prefs -Value "<?xml version=`"1.0`" encoding=`"utf-8`"?>`n<PrefsData>`n  <devMode>True</devMode>`n</PrefsData>`n" -Encoding utf8
}

Write-Host "Running headless self-test (RimWorld closes itself when done)..."
$start = Get-Date
$proc = Start-Process -FilePath (Join-Path $RimWorldPath "RimWorld.exe") `
    -ArgumentList "-Headless", "-quicktest", "-agmselftest", "`"-savedatafolder=$profileDir`"" `
    -WorkingDirectory $RimWorldPath -PassThru
if (-not $proc.WaitForExit($TimeoutSeconds * 1000)) {
    Stop-Process -Id $proc.Id -Force
    Write-Host "TIMEOUT after $TimeoutSeconds s (game killed)"
}
Write-Host ("Elapsed: {0:N0} s" -f ((Get-Date) - $start).TotalSeconds)

$lines = Get-Content $log
$errors = $lines | Select-String -Pattern "Config error|^\d{4}-\S+ \S+\s+ERROR" |
    ForEach-Object { ($_.Line -replace "^\S+ \S+\s+", "") } | Group-Object
Write-Host ""
Write-Host "=== Errors in log: $(@($errors).Count) distinct"
$errors | ForEach-Object { Write-Host ("  [{0}x] {1}" -f $_.Count, $_.Name.Substring(0, [Math]::Min(300, $_.Name.Length))) }

Write-Host ""
Write-Host "=== Self-test"
$test = $lines | Select-String -Pattern "\[AGM SelfTest\]" | ForEach-Object { $_.Line -replace "^\S+ \S+\s+\w+ \[Verse\] \[AGM SelfTest\] ", "" }
$test | ForEach-Object { Write-Host "  $_" }

$passed = ($test | Where-Object { $_ -match "^RESULT: PASS" }).Count -gt 0
if ($passed -and @($errors).Count -eq 0) { Write-Host "`nOVERALL: PASS"; exit 0 }
Write-Host "`nOVERALL: FAIL"
exit 1
