# HookLens

HookLens is a small .NET developer utility for inspecting webhook traffic as it flows through local and test environments. The project is intentionally scoped to a simple foundation: a lightweight ASP.NET Core service that exposes status and health endpoints, with a clean architecture that can grow into request capture, analysis, and replay features over time.

This repository currently contains only the initial foundation. The application does not yet implement webhook capture, persistence, replay, or a user interface.

## Purpose

The long-term vision for HookLens is to help developers:

- inspect incoming HTTP webhook payloads
- validate headers, body content, and routing behavior
- replay captured requests to downstream systems
- diagnose integration issues in a local development workflow

For now, the goal is to establish the project skeleton and a simple HTTP API that is easy to extend.

## Project structure

- src/HookLens: ASP.NET Core application
- src/HookLens/Endpoints: endpoint definitions
- src/HookLens/Models: DTOs and response models

## Prerequisites

- .NET SDK 10.0 or later
- A terminal with access to the .NET CLI

## Run locally

From the repository root, run:

```bash
dotnet restore
dotnet build
dotnet run --project src/HookLens/HookLens.csproj
```

When the app starts, the local development URL is printed in the terminal.

## Endpoints

- GET /health - returns basic health information
- GET /status - returns service metadata and readiness status

Examples:

```bash
curl http://localhost:PORT/health
curl http://localhost:PORT/status
```

Replace `PORT` with the port printed by `dotnet run` when the application starts.

## Notes

This is intentionally a simple, maintainable foundation for a small developer utility. The architecture remains focused on a minimal HTTP service and does not yet include persistence, replay, or frontend tooling.
