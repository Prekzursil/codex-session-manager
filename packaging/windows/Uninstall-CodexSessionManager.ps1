param(
    [string]$InstallRoot = "$env:LOCALAPPDATA\\Programs\\CodexSessionManager"
)

$ErrorActionPreference = "Stop"
if (Test-Path $InstallRoot) {
    Remove-Item -Path $InstallRoot -Recurse -Force
    Write-Host "Removed $InstallRoot"
} else {
    Write-Host "Nothing to remove at $InstallRoot"
}
