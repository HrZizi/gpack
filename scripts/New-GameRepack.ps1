# Forwarding script to root New-GameRepack.ps1
$script = Join-Path $PSScriptRoot "..\New-GameRepack.ps1"
& $script @args
