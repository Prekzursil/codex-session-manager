# Codex Session Manager

A Windows WPF desktop utility for browsing, searching, exporting, and carefully managing local Codex session history.

## What it does

- discovers Codex sessions from the canonical local store and known backup/mirror locations
- deduplicates multiple physical copies into one logical session row
- renders sessions as readable chat-style transcripts instead of raw JSONL
- supports local search across transcript text, commands, aliases, tags, notes, and technical breadcrumbs
- lets you attach aliases, tags, and notes without mutating the underlying Codex session files
- includes an advanced maintenance area for previewing archive/move/reconcile/delete actions with warnings and checkpoints

## Privacy and data boundary

Codex Session Manager is **local-first**:

- session content stays on this PC
- aliases/tags/notes are app-owned metadata
- live Codex SQLite state is inspection-only in v1
- destructive maintenance is limited to file-backed session stores and protected by preview + checkpoint + typed confirmation

## Local development

### Requirements

- Windows
- .NET SDK 8.0.419 or a compatible 8.x SDK

### Build and test

```powershell
dotnet test CodexSessionManager.sln
dotnet build CodexSessionManager.sln
```

### Run the desktop app

```powershell
dotnet run --project src/CodexSessionManager.App/CodexSessionManager.App.csproj
```

## Release outputs

The repository is configured to produce:

- a portable Windows zip artifact
- an installer bundle zip that contains a PowerShell install/uninstall experience

The installer bundle is intentionally transparent and script-based for v1 so releases do not depend on a preinstalled local MSI/WiX toolchain.

## GitHub hardening in this repo

Repo-file automation included here:

- CI build + test + coverage artifact
- CodeQL workflow
- Semgrep workflow
- Dependabot for NuGet + GitHub Actions
- SECURITY.md, issue templates, PR template, CODEOWNERS
- tag-driven release workflow

## Manual GitHub settings after repo creation

Some GitHub protections still need to be enabled in the GitHub UI after the repo exists:

1. enable branch protection / rulesets for `main`
2. require pull requests and required status checks
3. enable secret scanning and push protection for the public repo
4. onboard any external quality providers you want beyond the GitHub-native workflows in this repo

Useful docs:

- Branch protection: <https://docs.github.com/en/repositories/configuring-branches-and-merges-in-your-repository/managing-protected-branches>
- Secret scanning / push protection: <https://docs.github.com/en/code-security/secret-scanning/enabling-secret-scanning-features>
- Dependabot: <https://docs.github.com/en/code-security/dependabot/dependabot-version-updates/configuring-dependabot-version-updates>
- CodeQL setup: <https://docs.github.com/en/code-security/code-scanning/enabling-code-scanning/configuring-default-setup-for-code-scanning>

## Fixture policy

This repository should use **synthetic redacted fixtures only**. Do not commit real private Codex sessions.
