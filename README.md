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
2. Run the API. In Development it applies the database migrations at startup.
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

## Database
The schema is in EF Core migrations in `backend/src/Reacher.Data/Migrations`.
In Development the API applies them at startup (`Database:MigrateOnStartup`).
To apply them without the API, or to add one, use the repo's pinned `dotnet ef`
(run `dotnet tool restore` once):

```
dotnet ef database update --project backend/src/Reacher.Data
dotnet ef migrations add <Name> --project backend/src/Reacher.Data
```

Both use the local Compose database, or `ConnectionStrings__Default` if it is set.

### Dev seed
30 fictional companies in Stockholm, Solna and Göteborg (SNI 96210 and 96220), some with
annual-report figures. It replaces every company and all coverage, so never run it against
a real ingest. Apply the migrations first, then:

```
docker compose exec postgres psql -U reacher -d reacher -v ON_ERROR_STOP=1 -f /seed/dev-seed.sql
```

If Postgres was started before the seed folder was mounted, run `docker compose up -d`
once to recreate the container. The data is kept.

## Configuration
Non-secret defaults live in `backend/src/Reacher.Api/appsettings.json`. Secrets are never
in that file. Locally they live in user-secrets for the API project. `dotnet run` loads
them when `ASPNETCORE_ENVIRONMENT` is Development, so no `.env` file is required.
A missing required setting stops startup with the setting's name, never its value.

- `Scb`: base URL, page size (max 5000) and requests per second (default 2, max 10). `Scb:ApiKey` via user-secrets.
- `Bolagsverket`: token URL, API URL, scope, years, and the iXBRL tag map.
  Client id and secret via user-secrets.
- `Ingest`: freshness days (30), allowed legal forms (`49`), and `UseFakeCoordinator`
  (false). Set it to true to simulate ingest progress without calling SCB. The real
  coordinator is used when the flag is false.
- `Brave`: monthly cap (keep it below the Brave plan limit), requests per second,
  and `UseFake` (true in Development). API key via user-secrets.
- `PageSpeed`: strategy (`mobile`) and timeout (90 seconds). API key via user-secrets.
- `ReworkScore`: weights and penalties. Starting assumption for the rework score.
- `Matching`: directory and social block list, confidence thresholds, and the title bonus. Starting assumption until matching is measured.
- `Database:MigrateOnStartup`: apply migrations when the API starts. False by default, true in `appsettings.Development.json`.
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

Never edit or hand-merge a `packages.lock.json`. If merging `main` gives a conflict in one,
take `main`'s version and let NuGet regenerate it (PowerShell, from the repo root):
```
git merge origin/main
git diff --name-only --diff-filter=U -- '*packages.lock.json' | ForEach-Object { git checkout --theirs -- $_ }
dotnet restore backend --force-evaluate
git add backend
git commit --no-edit
```

## Repo layout
```
backend/   .NET solution (Reacher.slnx): Api, Data, Ingestion, WebsiteAnalysis + tests
frontend/  React app (J-05)
docs/      decisions, contracts, SCB field reference
```
