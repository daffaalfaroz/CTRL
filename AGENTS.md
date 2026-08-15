# AGENTS.md

Repo is a stub: only `README.md` + `.gitignore` (initial commit). No app code exists yet.

## Structure (planned, not yet created)
- `CTRL Mobile` — Android/tablet app, Flutter/Dart (per `.gitignore`)
- `CTRL Desktop` — PC companion app, .NET (per `.gitignore`)

## Conventions
- Env/secrets: `.env` and `.env.*` are gitignored; only `.env.example` is committed. When adding config, commit the example, not the real file.
- `README.md` is written in Bahasa Indonesia — keep user-facing docs in that language unless asked otherwise.

## Environment gotchas
- Windows machine (PowerShell 5.1).
- Git may fail with `detected dubious ownership`; prefix commands with `-c safe.directory='*'` if it occurs.
