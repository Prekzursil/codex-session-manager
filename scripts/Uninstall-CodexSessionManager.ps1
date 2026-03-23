param(
    [Parameter(Mandatory = $false)]
    [string]$InstallRoot = "$env:LOCALAPPDATA\\Programs\\CodexSessionManager"
)

if (Test-Path $InstallRoot) {
    Remove-Item -Recurse -Force $InstallRoot
    Write-Host "Removed Codex Session Manager from $InstallRoot"
} else {
    Write-Host "Nothing to remove at $InstallRoot"
}
