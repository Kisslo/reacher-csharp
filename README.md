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

## Configuration
Non-secret defaults live in `backend/src/Reacher.Api/appsettings.json`. Secrets are never
in that file. Locally they live in user-secrets for the API project. `dotnet run` loads
them when `ASPNETCORE_ENVIRONMENT` is Development, so no `.env` file is required.
A missing required setting stops startup with the setting's name, never its value.

- `Scb`: base URL and page size (max 5000). `Scb:ApiKey` via user-secrets.
- `Bolagsverket`: token URL, API URL, scope, years, and the iXBRL tag map.
  Client id and secret via user-secrets.
- `Ingest`: freshness days (30) and allowed legal forms (`49`).
- `Brave`: monthly cap (keep it below the Brave plan limit), requests per second,
  and `UseFake` (true in Development). API key via user-secrets.
- `PageSpeed`: strategy (`mobile`) and timeout (90 seconds). API key via user-secrets.
- `ReworkScore`: weights and penalties. Starting assumption for the rework score.
- `ConnectionStrings:Default`: the Compose Postgres connection string, in `appsettings.Development.json`.

Set the secrets from the repo root. Type the values locally. Do not commit them.

```
dotnet user-secrets set "Scb:ApiKey" "..." --project backend/src/Reacher.Api
dotnet user-secrets set "Bolagsverket:ClientId" "..." --project backend/src/Reacher.Api
dotnet user-secrets set "Bolagsverket:ClientSecret" "..." --project backend/src/Reacher.Api
dotnet user-secrets set "Brave:ApiKey" "..." --project backend/src/Reacher.Api
dotnet user-secrets set "PageSpeed:ApiKey" "..." --project backend/src/Reacher.Api
```

`dotnet user-secrets list --project backend/src/Reacher.Api` prints the values.
Do not paste that output into an issue, a pull request, or a screenshot.
A leaked key is rotated. Deleting the commit is not enough.

Docker Compose later reads the same settings from environment variables.
The names are in `.env.example` (`Scb__ApiKey` and the rest). `__` maps to `:`.

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
