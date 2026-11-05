<#
Validates a TimeSink release package:
  1. Portable zip: required layout, and (with -Smoke) runs extracted Agent and UI against scratch DBs.
  2. Installer update (with -TestInstaller or when -Installer is provided): validates clean install,
     then runs an update over an active running instance, verifying graceful shutdown, file replacement,
     and automatic relaunch.
Usage: pwsh installer\Test-Package.ps1 [-Zip <path>] [-Installer <path>] [-Smoke] [-TestInstaller]
Exit code 0 = pass.
#>
param(
    [string]$Zip = (Join-Path $PSScriptRoot '..\artifacts\TimeSink-portable-win-x64.zip'),
    [string]$Installer = $null,
    [switch]$Smoke,
    [switch]$TestInstaller
)
$ErrorActionPreference = 'Stop'
$fail = New-Object System.Collections.Generic.List[string]
function Check($ok, $what) { if ($ok) { Write-Host "ok   $what" } else { Write-Host "FAIL $what"; $script:fail.Add($what) } }

# 1. Validate portable zip if present
if (Test-Path $Zip) {
    $tmp = Join-Path ([IO.Path]::GetTempPath()) ("timesink-pkgtest-" + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory $tmp | Out-Null
    $started = @()
    try {
        Expand-Archive -LiteralPath $Zip -DestinationPath $tmp
        $root = if (Test-Path (Join-Path $tmp 'TimeSink.Agent.exe')) { $tmp } else { Join-Path $tmp 'TimeSink' }
        foreach ($f in 'TimeSink.Agent.exe', 'ui\TimeSink.UI.exe', 'ui\TimeSink.UI.dll', 'ui\TimeSink.Core.dll',
                       'ui\TimeSink.UI.runtimeconfig.json', 'ui\themes\pulse-dark\theme.json', 'ui\themes\pulse-light\theme.json', 'README.md') {
            Check (Test-Path (Join-Path $root $f)) "zip contains $f"
        }
        $hasSqlite = (Test-Path (Join-Path $root 'e_sqlcipher.dll')) -or (Test-Path (Join-Path $root 'e_sqlite3.dll'))
        Check $hasSqlite 'zip contains sqlite native library (e_sqlcipher.dll or e_sqlite3.dll)'
        Check (-not (Get-ChildItem $root -Recurse -Filter *.pdb)) 'no .pdb files in zip'
        Check (-not (Test-Path (Join-Path $root 'portable.flag'))) 'no portable.flag (not implemented in v1)'
        $rc = Get-Content (Join-Path $root 'ui\TimeSink.UI.runtimeconfig.json') -Raw
        Check ($rc -match 'includedFrameworks') 'UI is self-contained'

        if ($Smoke -and $fail.Count -eq 0) {
            $db = Join-Path $tmp 'agent-test.db'
            $a = Start-Process (Join-Path $root 'TimeSink.Agent.exe') -ArgumentList '--db', "`"$db`"" -PassThru
            $started += $a
            Start-Sleep -Seconds 4
            Check (-not $a.HasExited) 'Agent runs with --db scratch'
            Check (Test-Path $db) 'Agent created scratch database'
            $env:TIMESINK_DB = Join-Path $tmp 'ui-test.db'
            $u = Start-Process (Join-Path $root 'ui\TimeSink.UI.exe') -PassThru
            $started += $u
            Start-Sleep -Seconds 6
            Check (-not $u.HasExited) 'UI runs with TIMESINK_DB scratch'
        }
    }
    finally {
        $env:TIMESINK_DB = $null
        foreach ($p in $started) { try { if (-not $p.HasExited) { $p.Kill() } } catch {} }
        Get-Process | Where-Object { try { $_.Path -like "$tmp*" } catch { $false } } | ForEach-Object { try { $_.Kill() } catch {} }
        Start-Sleep -Milliseconds 500
        Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
    }
} elseif (-not $TestInstaller) {
    Write-Host "FAIL zip not found: $Zip"
    exit 1
}

# 2. Validate installer & update flow if requested or installer found
if (-not $Installer) {
    $found = Get-ChildItem (Join-Path $PSScriptRoot '..\artifacts') -Filter "TimeSink-Setup-*.exe" -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $found) {
        $found = Get-ChildItem (Join-Path $PSScriptRoot 'Output') -Filter "TimeSink-Setup-*.exe" -ErrorAction SilentlyContinue | Select-Object -First 1
    }
    if ($found) { $Installer = $found.FullName }
}

if ($TestInstaller -or ($Installer -and (Test-Path $Installer) -and $Smoke)) {
    if (-not (Test-Path $Installer)) {
        Write-Host "FAIL installer not found: $Installer"
        exit 1
    }
    Write-Host "== testing installer and update flow: $Installer"
    $tmpInst = Join-Path ([IO.Path]::GetTempPath()) ("timesink-insttest-" + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory $tmpInst | Out-Null
    $appDir = Join-Path $tmpInst "App"
    $db = Join-Path $tmpInst "agent-live.db"
    try {
        # Initial silent installation into scratch directory
        $proc = Start-Process $Installer -ArgumentList "/DIR=`"$appDir`"", "/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART", "/SP-" -PassThru
        $proc.WaitForExit(60000)
        Check ($proc.ExitCode -eq 0) "initial silent install succeeded (exit $($proc.ExitCode))"

        $agentExe = Join-Path $appDir "TimeSink.Agent.exe"
        Check (Test-Path $agentExe) "installed TimeSink.Agent.exe"
        Check (Test-Path (Join-Path $appDir "ui\TimeSink.UI.exe")) "installed ui\TimeSink.UI.exe"

        # Start active instance of Agent against scratch database
        $agentProc = Start-Process $agentExe -ArgumentList "--db", "`"$db`"" -PassThru
        Start-Sleep -Seconds 3
        Check (-not $agentProc.HasExited) "Agent running prior to update (PID $($agentProc.Id))"

        # Set older timestamp to verify replacement
        (Get-Item $agentExe).LastWriteTime = (Get-Date).AddDays(-5)
        $oldTimestamp = (Get-Item $agentExe).LastWriteTime

        # Execute installer update over running app
        $updateProc = Start-Process $Installer -ArgumentList "/DIR=`"$appDir`"", "/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART", "/SP-" -PassThru
        $updateProc.WaitForExit(60000)
        Check ($updateProc.ExitCode -eq 0) "installer update succeeded (exit $($updateProc.ExitCode))"

        Start-Sleep -Seconds 3
        Check ($agentProc.HasExited) "running Agent closed during update"

        $newTimestamp = (Get-Item $agentExe).LastWriteTime
        Check ($newTimestamp -gt $oldTimestamp) "Agent executable updated with new files"

        # Verify automatic relaunch after update
        $relaunched = Get-Process -Name "TimeSink.Agent" -ErrorAction SilentlyContinue | Where-Object {
            try { $_.Path -like "$appDir*" } catch { $false }
        }
        Check (@($relaunched).Count -gt 0) "Agent automatically relaunched after update"
    }
    finally {
        Get-Process | Where-Object { try { $_.Path -like "$tmpInst*" } catch { $false } } | ForEach-Object { try { $_.Kill() } catch {} }
        Start-Sleep -Milliseconds 500
        Remove-Item $tmpInst -Recurse -Force -ErrorAction SilentlyContinue
    }
}

if ($fail.Count) { Write-Host "$($fail.Count) check(s) failed"; exit 1 }
Write-Host 'package OK'
