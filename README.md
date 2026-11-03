# Reacher

Reacher is an internal tool for finding Swedish aktiebolag by industry and
municipality. A user selects one exact five-digit SNI code and one or more
municipalities, then can:

- browse and filter eligible companies;
- sort by employees, revenue, net result, or registration date;
- find a company's website on request; and
- analyse that website with Lighthouse and Reacher's own checks to estimate
  whether it needs rework.

The complete demo flow is documented in [docs/demo.md](docs/demo.md).
The authoritative project decisions are in
[docs/decisions.md](docs/decisions.md), and the database and HTTP contracts are
in [docs/contracts.md](docs/contracts.md).

## Prerequisites

- .NET SDK 10, pinned in [global.json](global.json)
- Node.js 24 or newer and npm
- Docker Desktop, running

## Development setup

Development runs the API and frontend on the host.

Start only the development database:

```powershell
docker compose up -d postgres
```

Configure local secrets with `dotnet user-secrets` as described in
[Configuration and secrets](#configuration-and-secrets), then run the API:

```powershell
dotnet run --project backend/src/Reacher.Api
```

In another terminal, run the frontend:

```powershell
Set-Location frontend
npm ci
npm run dev
```

Open the Vite URL shown in the terminal, normally http://localhost:5173.
The development frontend proxies `/api` and `/health` to the API at
http://localhost:5080. The API applies database migrations in Development.
The Development-only OpenAPI document is available at
http://localhost:5080/openapi/v1.json.

Stop the database with `docker compose down` (data is kept), or
`docker compose down -v` to delete the database volume.

## Run the whole project with Docker Compose

The Docker Compose deployment runs Postgres, the API, and the built frontend
together. It uses `.env` to pass secrets into the API container; user-secrets
from the host are not available inside Docker containers.

```powershell
Copy-Item .env.example .env
# Add the real keys to .env before starting. Keep this file local.
docker compose up -d --build
```

Open http://localhost:8080. Nginx serves the frontend and proxies `/api` and
`/health` to the API. The API applies database migrations before it starts.

The default Compose settings use the fake ingest coordinator and fake Brave
client. For a real-data run, set these values in `.env`:

```env
Ingest__UseFakeCoordinator=false
Brave__UseFake=false
```

Real ingest also requires the SCB and Bolagsverket credentials. Website
analysis requires the PageSpeed key.

Stop the deployment with `docker compose down`. Add `-v` only when you also
want to delete the Postgres data volume.

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

## Configuration and secrets
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

Avoid printing configured secrets. If a key is leaked, rotate it immediately;
deleting a commit is not enough.

Docker Compose reads the same settings from `.env` environment variables. The
names are listed in [.env.example](.env.example); `__` maps to `:` in ASP.NET
Core configuration. Keep `.env` local. It is ignored by Git and must never be
committed or pasted into an issue, pull request, log, or screenshot.

## Brave and GDPR rules

- Brave is called only after the user requests website candidates for one
  company. It is never called in bulk or from a background job.
- Brave results, user-pasted URLs, and analysis results are transient. They are
  not stored in the database, logs, files, caches, browser storage, or URLs.
- URLs are validated against SSRF before the server fetches them, including
  after redirects.
- Only eligible aktiebolag are stored. Personnummer-like SCB rows beginning
  with `19` or `20` are discarded immediately.
- API keys, tokens, personnummer, and Brave or user URLs are never logged.

See [docs/decisions.md](docs/decisions.md) for the full decisions and
[docs/contracts.md](docs/contracts.md) for the API and database contracts.

## Next steps

- Add login and authentication, as described by decision D1.
- Deploy to a cloud environment, as described by decision D11.
- Revisit support for additional legal forms after the MVP, as described by
  decision D2.

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
