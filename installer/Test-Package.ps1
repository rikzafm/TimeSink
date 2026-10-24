<#
Validates a TimeSink portable zip: required layout, then (with -Smoke) runs the extracted Agent and UI against
scratch databases only (never the real %LocalAppData%\TimeSink or the HKCU Run key) and kills what it started.
Usage: pwsh installer\Test-Package.ps1 [-Zip artifacts\TimeSink-portable-win-x64.zip] [-Smoke]
Exit code 0 = pass.
#>
param(
    [string]$Zip = (Join-Path $PSScriptRoot '..\artifacts\TimeSink-portable-win-x64.zip'),
    [switch]$Smoke
)
$ErrorActionPreference = 'Stop'
$fail = New-Object System.Collections.Generic.List[string]
function Check($ok, $what) { if ($ok) { Write-Host "ok   $what" } else { Write-Host "FAIL $what"; $script:fail.Add($what) } }

if (-not (Test-Path $Zip)) { Write-Host "FAIL zip not found: $Zip"; exit 1 }
$tmp = Join-Path ([IO.Path]::GetTempPath()) ("timesink-pkgtest-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory $tmp | Out-Null
$started = @()
try {
    Expand-Archive -LiteralPath $Zip -DestinationPath $tmp
    $root = if (Test-Path (Join-Path $tmp 'TimeSink.Agent.exe')) { $tmp } else { Join-Path $tmp 'TimeSink' }
    foreach ($f in 'TimeSink.Agent.exe', 'e_sqlite3.dll', 'TimeSink.UI.exe', 'TimeSink.UI.dll', 'TimeSink.Core.dll',
                   'TimeSink.UI.runtimeconfig.json', 'themes\pulse-dark\theme.json', 'themes\pulse-light\theme.json', 'README.md') {
        Check (Test-Path (Join-Path $root $f)) "contains $f"
    }
    Check (-not (Get-ChildItem $root -Recurse -Filter *.pdb)) 'no .pdb files'
    Check (-not (Test-Path (Join-Path $root 'portable.flag'))) 'no portable.flag (not implemented in v1)'
    $rc = Get-Content (Join-Path $root 'TimeSink.UI.runtimeconfig.json') -Raw
    Check ($rc -match 'includedFrameworks') 'UI is self-contained'

    if ($Smoke -and $fail.Count -eq 0) {
        $db = Join-Path $tmp 'agent-test.db'
        $a = Start-Process (Join-Path $root 'TimeSink.Agent.exe') -ArgumentList '--db', "`"$db`"" -PassThru
        $started += $a
        Start-Sleep -Seconds 4
        Check (-not $a.HasExited) 'Agent runs with --db scratch'
        Check (Test-Path $db) 'Agent created scratch database'
        $env:TIMESINK_DB = Join-Path $tmp 'ui-test.db'
        $u = Start-Process (Join-Path $root 'TimeSink.UI.exe') -PassThru
        $started += $u
        Start-Sleep -Seconds 6
        Check (-not $u.HasExited) 'UI runs with TIMESINK_DB scratch'
    }
}
finally {
    $env:TIMESINK_DB = $null
    foreach ($p in $started) { try { if (-not $p.HasExited) { $p.Kill() } } catch {} }
    # The Agent may have spawned a UI of its own: only touch processes running from our temp dir.
    Get-Process | Where-Object { try { $_.Path -like "$tmp*" } catch { $false } } | ForEach-Object { try { $_.Kill() } catch {} }
    Start-Sleep -Milliseconds 500
    Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
}
if ($fail.Count) { Write-Host "$($fail.Count) check(s) failed"; exit 1 }
Write-Host 'package OK'
