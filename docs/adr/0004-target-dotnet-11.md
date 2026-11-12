# 4. Target .NET 11

- **Status:** Accepted
- **Date:** 2026-11-12

## Context

The project was scaffolded on .NET 10, the current long-term support (LTS) release. .NET 11 shipped in November 2026 as a standard-term support (STS) release, and it is the SDK installed on my development machine.

Keeping .NET 10 meant installing a second SDK and runtime next to .NET 11. That had already caused one confusing failure: a terminal resolved `dotnet` to SDK 11, and `global.json` refused it.

Since .NET 9, STS releases are supported for 24 months instead of 18. That puts the end of support for .NET 11 at roughly the same time as .NET 10, so choosing LTS gains no extra support window here.

## Decision

Target `net11.0` with the .NET 11 SDK and the 11.x Microsoft packages (EF Core, ASP.NET Core testing, `dotnet-ef`).

- `global.json` pins SDK `11.0.100` with `rollForward: latestFeature`, so any later 11.0 SDK works but no other major version does.
- The target framework is set once, in `Directory.Build.props`.
- Microsoft package versions stay on the same major version as the target framework.

## Consequences

- One SDK on the development machine, and the newest C# and ASP.NET Core features.
- No migration changes were needed: EF Core 11 reports no difference between the model and the existing `InitialCreate` migration.
- Azure App Service must offer a .NET 11 runtime stack before milestone 6. If it does not, the app is deployed self-contained or as a container instead.
- Upgrading to .NET 12 (the next LTS, November 2027) is a reasonable follow-up once it ships, and is a one-line change in `Directory.Build.props` plus package versions.
