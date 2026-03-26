param(
    [string]$InstallRoot = "$env:LOCALAPPDATA\\Programs\\CodexSessionManager"
)

$ErrorActionPreference = "Stop"
if (Test-Path $InstallRoot) {
    Remove-Item -Path $InstallRoot -Recurse -Force
    Write-Output "Removed $InstallRoot"
} else {
    Write-Output "Nothing to remove at $InstallRoot"
}
