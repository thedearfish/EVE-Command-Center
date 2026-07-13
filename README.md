# EVE Command Center

EVE Command Center is a greenfield, open-source Windows application for previewing and switching between multiple EVE Online clients.

> Status: architecture foundation and initial implementation.

## Version 0.1 scope

- discover EVE client windows;
- maintain a client registry;
- render live previews with DWM thumbnails;
- activate a selected client window;
- global hotkeys;
- cyclic Next/Previous switching;
- user-defined client order;
- skip closed clients and Character Selection;
- persist settings;
- system tray integration.

Combat parsing, mining statistics, alerts, overlays, ESI, fleet tools, FPS limiting and third-party plug-ins are intentionally outside the first alpha.

## Technology

- .NET 10 LTS;
- C#;
- WPF;
- Win32 and Desktop Window Manager interop;
- xUnit;
- GitHub Actions.

## Architecture

The solution keeps domain and application logic independent from WPF, Win32, JSON and the file system.

```text
Core             domain model and invariants
Application      use cases and ports
Windows          Win32/DWM adapters
Infrastructure   persistence and operating-system-neutral infrastructure
Presentation     WPF views and view models
App              composition root and executable
```

Architecture decisions are recorded in [`docs/adr`](docs/adr).

## Build

Requirements:

- Windows 10 or Windows 11;
- .NET 10 SDK;
- Visual Studio with the .NET desktop development workload, or the .NET CLI.

```powershell
dotnet restore EveCommandCenter.sln
dotnet build EveCommandCenter.sln --configuration Release
dotnet test EveCommandCenter.sln --configuration Release
dotnet run --project src/EveCommandCenter.App/EveCommandCenter.App.csproj
```

## Development workflow

Changes are developed in short-lived branches and reviewed through pull requests. `main` should remain buildable.

## License

MIT. See [`LICENSE`](LICENSE).

## Disclaimer

EVE Online and all related trademarks are the property of CCP hf. This project is not affiliated with or endorsed by CCP hf.
