# reacher-csharp

Internal tool: pick an industry (SNI code) and municipalities, get a filterable list of
Swedish aktiebolag, find a company's website and see whether it needs rework.
Decisions and contracts live in [docs/](docs/).

## Prerequisites
- .NET SDK 10 (pinned in `global.json`)
- Docker Desktop, running

## Run locally
1. Start Postgres:
   ```
   docker compose up -d
   ```
2. Run the API:
   ```
   dotnet run --project backend/src/Reacher.Api
   ```
3. Check that it works:
   - http://localhost:5080/health returns `Healthy`
   - http://localhost:5080/openapi/v1.json is the OpenAPI document (Development only)

Stop Postgres with `docker compose down` (data is kept) or `docker compose down -v`
(deletes the database).

If port 5432 is taken, you probably have a local Postgres installed. Stop it, or change
the port in both `docker-compose.yml` and `appsettings.Development.json`.

## Before you push
```
cd backend
dotnet format
dotnet build
dotnet test      # needs Docker running (Testcontainers)
```
After adding a NuGet package, commit the updated `packages.lock.json`.

## Repo layout
```
backend/   .NET solution (Reacher.slnx): Api, Data, Ingestion, WebsiteAnalysis + tests
frontend/  React app (J-05)
docs/      decisions, contracts, SCB field reference
```
