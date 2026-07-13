# Contributing

## Branches

Use short-lived branches:

- `foundation/*`
- `feature/*`
- `fix/*`
- `docs/*`

## Pull requests

Keep pull requests focused. Include validation steps and document Windows-specific manual testing whenever Win32, DWM, hotkeys or tray behavior changes.

## Architecture rules

- Core has no WPF, Win32, JSON or file-system dependencies.
- Application depends on abstractions, not adapters.
- Native handles are translated at the Windows boundary.
- Expected operating-system failures use structured results.
- New behavior should include unit or integration tests where practical.

## Local validation

```powershell
dotnet restore EveCommandCenter.sln
dotnet build EveCommandCenter.sln --configuration Release
dotnet test EveCommandCenter.sln --configuration Release
```
