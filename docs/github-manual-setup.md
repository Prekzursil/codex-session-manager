# GitHub manual setup

These settings are not fully enforceable from repository files alone and should be enabled after the repository is created.

## Required repository settings

1. **Protect `main`**
   - require pull requests
   - require status checks before merge
   - block force-pushes
   - restrict direct pushes where appropriate

2. **Enable secret scanning**
   - turn on secret scanning for the public repository
   - enable push protection if available

3. **Adopt quality-zero-style governance**
   - require the emitted CI / CodeQL / Semgrep / aggregate quality checks
   - if provider-backed tools are later connected (Codacy, DeepScan, Sonar, Sentry), only require contexts that the repo actually emits

4. **Security features**
   - enable private vulnerability reporting if available
   - review Dependabot alerts and security updates

## Notes

- repo files can define workflows, templates, Dependabot config, and release automation
- branch protections and some secret-scanning controls are admin settings
- keep local-green and remote/provider-green as separate completion signals
