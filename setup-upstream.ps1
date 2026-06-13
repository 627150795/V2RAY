$ErrorActionPreference = 'Stop'

$upstream = Join-Path (Split-Path $PSScriptRoot -Parent) 'upstream-v2rayN'
$commit = 'a2a5940d9120ab3e49d735405a799f49f31ec435'
$patch = Join-Path $PSScriptRoot 'patches\v2rayn-monitor-paths.patch'

if (!(Test-Path (Join-Path $upstream '.git'))) {
    git clone --filter=blob:none https://github.com/2dust/v2rayN.git $upstream
}

$current = git -C $upstream rev-parse HEAD
if ($current -ne $commit) {
    git -C $upstream fetch origin $commit
    git -C $upstream checkout --detach $commit
}

$previousPreference = $ErrorActionPreference
$ErrorActionPreference = 'Continue'
git -C $upstream apply --reverse --check $patch 2>$null
$reverseCheck = $LASTEXITCODE
$ErrorActionPreference = $previousPreference
if ($reverseCheck -ne 0) {
    git -C $upstream apply --check $patch
    git -C $upstream apply $patch
}

Write-Host "v2rayN ServiceLib ready at $commit"
