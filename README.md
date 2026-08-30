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
- src/HookLens/Services: SQLite-backed capture/storage logic and HTTP replay logic
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

## Docker

HookLens includes a multi-stage Dockerfile for containerized local development and testing.

Build the image:

```bash
docker build -t hooklens .
```

Run HookLens on port 8080 and keep the SQLite database in a mounted volume:

```bash
docker run --rm -p 8080:8080 \
  -v "$(pwd)/data:/data" \
  --name hooklens \
  hooklens
```

The container runs on `http://localhost:8080` and stores its SQLite database at `/data/hooklens.db` by default. You can omit the volume if you want an ephemeral container-local database for testing, but a mounted path is recommended for persistence.

## GitHub Actions

This repository includes a simple CI workflow in `.github/workflows/ci.yml` that runs on pushes to `main` and on pull requests targeting `main`.

The workflow installs .NET 10, restores dependencies, builds the solution in Release mode, and runs the full test suite.

## Dashboard

The app serves a lightweight browser dashboard at `/`.

The dashboard is intentionally minimal and developer-focused:

- dark terminal-inspired UI
- summary cards for request totals and freshness
- newest-first request list with payload previews
- search and source filters above the captured request list
- detail panel for headers, raw body, and JSON pretty-printing
- copy buttons for request IDs and payload bodies
- destination URL input and explicit request replay with success/error feedback
- responsive layout for desktop and narrower screens

Open http://localhost:5078/ in your browser after starting the app.

## Endpoints

- `GET /` - dashboard homepage
- `GET /health` - returns basic health information
- `GET /status` - returns service metadata and readiness status
- `POST /capture/{source}` - captures an arbitrary JSON request body and stores it in SQLite
- `GET /requests` - returns all captured requests, newest first; supports optional `source` and `q` filters
- `GET /requests/{id}` - retrieves one captured request by ID
- `POST /requests/{id}/replay` - replays the original captured body to an absolute `http` or `https` destination URL

Examples:

```bash
curl http://localhost:5078/health

curl http://localhost:5078/status

curl -X POST http://localhost:5078/capture/github \
  -H "Content-Type: application/json" \
  -d '{"event":"ping","ok":true}'

curl "http://localhost:5078/requests?source=github"

curl "http://localhost:5078/requests?q=ping"

curl "http://localhost:5078/requests?source=github&q=ping"

curl http://localhost:5078/requests

curl http://localhost:5078/requests/{request-id}

curl -X POST http://localhost:5078/requests/{request-id}/replay \
  -H "Content-Type: application/json" \
  -d '{"targetUrl":"http://localhost:8080/webhook"}'
```

## Replay

HookLens can replay a previously captured request to a new destination. The original body is sent as an HTTP `POST` using the recorded `Content-Type` when available, while transport-specific headers such as `Host`, `Connection`, and `Content-Length` that should be regenerated or omitted for the outbound request are excluded before the outbound call.

This is intended for local and test development use only. HookLens is not a production-grade relay or message broker.

## Notes

Storage is currently local and file-based using SQLite. HookLens does not yet include authentication or a separate frontend framework.