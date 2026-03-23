# Contributing

## Development flow

1. create a feature branch
2. add or update tests first where behavior changes
3. run `dotnet test CodexSessionManager.sln`
4. run `dotnet build CodexSessionManager.sln`
5. open a pull request

## Guardrails

- keep `C:\Users\Prekzursil\.codex` canonical and `.codex-vscode` read-only for migration/comparison assumptions
- treat live Codex SQLite state as inspection-only in v1
- keep maintenance operations preview-first and checkpoint-first
- use synthetic redacted fixtures only

## Quality expectations

- no unchecked destructive maintenance behavior
- no secrets in source, workflows, fixtures, docs, or screenshots
- no success claims without fresh verification evidence
