$ErrorActionPreference = 'Stop'
$project = $PSScriptRoot
$output = Join-Path $project 'artifacts'
$publish = Join-Path $project 'publish'
& (Join-Path $project 'setup-upstream.ps1')
dotnet publish (Join-Path $project 'ProxyMonitor.csproj') -c Release -r win-x64 --self-contained true -o $publish
if (Test-Path (Join-Path $output 'ProxyMonitor-portable')) { Remove-Item -LiteralPath (Join-Path $output 'ProxyMonitor-portable') -Recurse -Force }
Copy-Item -LiteralPath $publish -Destination (Join-Path $output 'ProxyMonitor-portable') -Recurse
$iscc = 'C:\Program Files (x86)\Inno Setup 6\ISCC.exe'
if (!(Test-Path $iscc)) { $iscc = 'C:\Program Files\Inno Setup 6\ISCC.exe' }
if (!(Test-Path $iscc)) { $iscc = Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe' }
if (Test-Path $iscc) {
    & $iscc (Join-Path $project 'installer.iss')
} else {
    Write-Warning 'Inno Setup was not found; portable build completed without an installer.'
}
