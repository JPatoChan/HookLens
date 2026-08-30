# HookLens

HookLens is a small .NET developer utility for inspecting webhook traffic in local and test environments. It provides a lightweight API for capturing incoming HTTP requests and storing them in SQLite so they persist across application restarts.

## Purpose

HookLens helps developers:

- inspect incoming webhook payloads
- validate headers and raw request bodies
- trace requests from specific sources
- troubleshoot integrations during local development

## Project structure

- src/HookLens: ASP.NET Core application
- src/HookLens/Endpoints: endpoint definitions
- src/HookLens/Services: SQLite-backed storage and capture logic
- src/HookLens/Data: EF Core DbContext and migrations
- src/HookLens/Models: request and response models

## Prerequisites

- .NET SDK 10.0 or later
- A terminal with access to the .NET CLI

## Run locally

From the repository root, run:

```bash
dotnet restore
dotnet build
dotnet run --project src/HookLens/HookLens.csproj --urls http://localhost:5078
```

When the app starts, the local development URL is printed in the terminal.

If no connection string is configured, HookLens uses `Data Source=hooklens.db`. Because this is a relative path, the database file is created in the application's current working directory.
## Endpoints

- GET /health - returns basic health information
- GET /status - returns service metadata and readiness status
- POST /capture/{source} - captures an arbitrary JSON request body and stores it in SQLite
- GET /requests - returns all captured requests, newest first
- GET /requests/{id} - retrieves one captured request by ID

Examples:

```bash
curl http://localhost:5078/health

curl http://localhost:5078/status

curl -X POST http://localhost:5078/capture/github \
  -H "Content-Type: application/json" \
  -d '{"event":"ping","ok":true}'

curl http://localhost:5078/requests

curl http://localhost:5078/requests/{request-id}
```

## Notes

Storage is currently local and file-based using SQLite. HookLens does not yet include replay, filtering, authentication, frontend tooling, or Docker support.