# Security Policy

## Supported versions

Until a stable versioning policy is established, only the latest tagged release is considered supported for security fixes.

## Reporting a vulnerability

Please do **not** open public GitHub issues for sensitive security reports.

Instead:

1. open a private security advisory if available for the repository, or
2. contact the maintainer directly with a concise reproduction and impact summary

Include:

- affected version or commit
- reproduction steps
- expected vs actual behavior
- whether the issue can expose private local session content, metadata, or unsafe maintenance behavior

## Security expectations for contributors

- do not add telemetry or network sync without explicit review
- do not weaken maintenance confirmations or checkpoint creation
- do not make live Codex SQLite state writable in v1
- do not add real personal session data to tests, docs, fixtures, or screenshots
