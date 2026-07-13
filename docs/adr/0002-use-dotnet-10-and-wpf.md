# ADR 0002: Use .NET 10 and WPF

- Status: Accepted
- Date: 2026-07-13

## Context

EVE Command Center is a Windows-only desktop utility that needs mature Win32 interoperability, transparent desktop-window previews, tray integration and a low deployment burden.

## Decision

Use .NET 10 LTS, C# and WPF for the first production implementation.

## Consequences

The project receives the current LTS support window and direct access to Windows desktop APIs. It intentionally does not target macOS or Linux. UI code remains isolated in Presentation so another frontend is not coupled to domain logic.
