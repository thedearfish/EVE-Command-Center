# ADR 0001: Use explicit architecture boundaries

- Status: Accepted
- Date: 2026-07-13

## Context

The application will eventually combine domain rules, Win32/DWM interop, WPF presentation, settings persistence and optional feature modules. Coupling those concerns would make EVE client lifecycle bugs difficult to test and would make later modules expensive to add.

## Decision

Use six production projects:

- Core;
- Application;
- Windows;
- Infrastructure;
- Presentation;
- App.

Core must not reference WPF, Win32, JSON or the file system. Application coordinates use cases through interfaces. Platform-specific and persistence details are adapters. App is the composition root.

## Consequences

The initial solution contains more projects than a single-window prototype, but domain and cycling logic can be unit tested without Windows or EVE Online.
