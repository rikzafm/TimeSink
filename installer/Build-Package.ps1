<#
Builds the TimeSink release layout and packages it.
  1. Agent: Native AOT publish (needs the VS C++ Build Tools; vswhere.exe is put on PATH, decisions.md 19).
  2. UI: ReadyToRun, self-contained, no trimming.
  3. Both go side by side into artifacts\staging\TimeSink (the Agent starts TimeSink.UI.exe from its own folder).
  4. artifacts\TimeSink-portable-win-x64.zip (per-user, no admin, no installer needed).
  5. If Inno Setup's iscc.exe is found (PATH or the usual install folders), artifacts\TimeSink-Setup-<version>.exe.
Usage: pwsh installer\Build-Package.ps1 [-Version 1.0.0] [-SkipInstaller] [-Test]
Everything under artifacts\ is gitignored.
#>
param(
    [string]$Version = '1.0.0',
    [switch]$SkipInstaller,
    [switch]$Test   # run Test-Package.ps1 -Smoke on the zip afterwards
)
$ErrorActionPreference = 'Stop'
$repo = Resolve-Path (Join-Path $PSScriptRoot '..')
$art = Join-Path $repo 'artifacts'
$stage = Join-Path $art 'staging\TimeSink'
$zip = Join-Path $art 'TimeSink-portable-win-x64.zip'

function Run([string]$what, [scriptblock]$cmd) {
    Write-Host "== $what"
    & $cmd
    if ($LASTEXITCODE -ne 0) { throw "$what failed (exit $LASTEXITCODE)" }
}

$vsInstaller = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer'
if (-not (Get-Command vswhere.exe -ErrorAction SilentlyContinue)) {
    if (-not (Test-Path (Join-Path $vsInstaller 'vswhere.exe'))) { throw "vswhere.exe not found in $vsInstaller; install the Visual Studio C++ Build Tools (needed by Native AOT)." }
    $env:PATH = "$vsInstaller;$env:PATH"
}

if (Test-Path (Join-Path $art 'staging')) { Remove-Item (Join-Path $art 'staging') -Recurse -Force }
New-Item -ItemType Directory $stage -Force | Out-Null
$uiOut = Join-Path $art 'obj-publish\ui'
$agentOut = Join-Path $art 'obj-publish\agent'
Remove-Item (Join-Path $art 'obj-publish') -Recurse -Force -ErrorAction SilentlyContinue

Run 'publish UI (ReadyToRun, self-contained)' {
    dotnet publish "$repo\src\TimeSink.UI\TimeSink.UI.csproj" -c Release -r win-x64 --self-contained `
        -p:PublishReadyToRun=true -p:PublishTrimmed=false -p:DebugType=None -p:Version=$Version -o $uiOut
}
Run 'publish Agent (Native AOT)' {
    dotnet publish "$repo\src\TimeSink.Agent\TimeSink.Agent.csproj" -c Release -r win-x64 `
        -p:PublishAot=true -p:DebugType=None -p:Version=$Version -o $agentOut
}

Copy-Item "$uiOut\*" $stage -Recurse -Force
Copy-Item "$agentOut\*" $stage -Recurse -Force
Get-ChildItem $stage -Recurse -Filter *.pdb | Remove-Item -Force
Copy-Item "$repo\README.md" $stage -Force
if (Test-Path "$repo\LICENSE") { Copy-Item "$repo\LICENSE" $stage -Force }

if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path $stage -DestinationPath $zip -CompressionLevel Optimal
'{0}  {1:N1} MB' -f $zip, ((Get-Item $zip).Length / 1MB) | Write-Host

if (-not $SkipInstaller) {
    $iscc = (Get-Command iscc.exe -ErrorAction SilentlyContinue).Source
    if (-not $iscc) {
        $iscc = @("${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
                  "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe") | Where-Object { Test-Path $_ } | Select-Object -First 1
    }
    if ($iscc) {
        Run 'compile installer (Inno Setup)' { & $iscc "/DAppVersion=$Version" "/DStageDir=$stage" "/O$art" "$PSScriptRoot\TimeSink.iss" }
    } else {
        Write-Warning 'Inno Setup (iscc.exe) not found: installer NOT built. Install Inno Setup 6 and re-run, or use -SkipInstaller.'
    }
}

if ($Test) { Run 'verify portable zip' { & pwsh -NoProfile -File "$PSScriptRoot\Test-Package.ps1" -Zip $zip -Smoke } }
