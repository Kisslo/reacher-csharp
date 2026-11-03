# Demo runbook

This runbook demonstrates the complete Reacher flow:

1. Select an SNI code and municipalities.
2. Ingest or load company data.
3. Filter and sort companies.
4. Find a company's website.
5. Analyse the website.
6. Sort the results by rework score.

## Preparation

### Real-data demo

Use the SNI code and municipalities agreed by the team. Ingest the selected
areas the day before the demo so the data is already available and fresh.

Before the demo:

- Start Docker Desktop.
- Confirm that the API keys are available locally through `.env`.
- Set the real integrations in `.env`:

  ```env
  Ingest__UseFakeCoordinator=false
  Brave__UseFake=false
  ```

- Recreate the API after changing `.env`:

  ```powershell
  docker compose up -d --force-recreate api
  ```

- Start the full stack:

  ```powershell
  docker compose up -d --build
  ```

- Check the application at http://localhost:8080.
- Check the API health endpoint:

  ```powershell
  Invoke-WebRequest -UseBasicParsing http://localhost:8080/health
  ```

- Confirm that the database contains no personal identity numbers:

  ```powershell
  docker compose exec postgres psql -U reacher -d reacher -c `
    "SELECT count(*) FROM company WHERE left(orgnr, 2) IN ('19', '20');"
  ```

  The result must be `0`.

### Fallback demo

If an external API is unavailable, use the fictional seed data and fake
integrations. Do not call Brave repeatedly to compensate for a failed demo.

```powershell
docker compose exec postgres psql -U reacher -d reacher `
  -v ON_ERROR_STOP=1 -f /seed/dev-seed.sql
```

The seed contains fictional companies in Stockholm, Solna, and Göteborg, using
SNI codes `96210` and `96220`.

For the fallback configuration, set these values in `.env`:

```env
Ingest__UseFakeCoordinator=true
Brave__UseFake=true
```

Then recreate the API:

```powershell
docker compose up -d --force-recreate api
```

## Demo sequence

### 1. Start a search

- Open http://localhost:8080.
- Enter one exact five-digit SNI code.
- Select two municipalities.
- Start the search.
- Show the ingest progress while data is missing or stale.
- Wait until the job succeeds.

If the selected coverage is already fresh, explain that the API returns the
existing data without starting a new ingest job.

### 2. Filter and sort the company list

Demonstrate at least two filters, for example:

- Employee class
- Revenue class
- Registration date
- Revenue
- Net result

Sort the result list by one database-backed field and move between pages if
there is more than one page of results.

Point out that each company appears once and matching workplaces are shown
inside the company row.

### 3. Find a website

- Click **Hitta webbplats** for one company.
- Show the candidate list and the preselected candidate.
- Explain that the search is performed only for the selected company and only
  after the user clicks.
- Select a candidate or enter a user-provided URL if needed.

Do not copy candidate URLs into screenshots, notes, issue comments, or logs.

### 4. Analyse the website

- Start the analysis for the selected website.
- Show the Lighthouse scores and the own checks.
- Explain that the rework score is a guide, not an exact measurement.
- Return to the company list and sort the current results by rework score.

Analysis results are kept only in the browser session. They are not persisted
in the database or browser storage.

### 5. Show the privacy properties

Explain these points during the demo:

- Only eligible aktiebolag are stored.
- Personnummer-like organisation numbers beginning with `19` or `20` are
  discarded before storage.
- Brave results, pasted URLs, and analysis results are transient.
- Website endpoints use `Cache-Control: no-store`.
- Secrets are supplied through `.env` and are never committed.
