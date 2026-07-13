# ADR 0003: Isolate Win32 and DWM interoperability

- Status: Accepted
- Date: 2026-07-13

## Context

Native window handles are ephemeral. Foreground activation, minimization, DPI changes and DWM thumbnail lifetimes have Windows-specific failure modes.

## Decision

Keep all P/Invoke and DWM interop in the Windows project. Core uses an opaque `WindowId` value and never calls native APIs. Native operations return structured results instead of plain booleans where the caller needs to distinguish expected failure modes.

## Consequences

Windows behavior can be integration tested separately. Domain and application tests do not require a desktop session.
