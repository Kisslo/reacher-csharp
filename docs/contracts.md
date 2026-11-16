# Contracts

## Database schema (Postgres, EF Core)

| Table | Columns | Notes |
|---|---|---|
| `company` | `orgnr` PK (10 digits), `name`, `sni`, `legal_form`, `company_status`, `employee_class`, `revenue_class`, `revenue_class_year`, `registered_at` (date, null), `ad_block_type`, `updated_at` | Codes as text (D12). Only eligible companies (D5). |
| `workplace` | `cfar` PK, `orgnr` FK → `company`, `name` (null), `sni`, `municipality`, `street`, `postal_code`, `city`, `workplace_status`, `ad_block_type`, `updated_at` | Visiting address only, never `postAdress`. |
| `financial_fact` | `orgnr` FK, `period_end` (date), `key`, `value` (numeric), `document_id`, `fetched_at`. PK (`orgnr`, `period_end`, `key`) | `key` from the tag map (D14). |
| `coverage` | `sni`, `municipality`, `fetched_at`. PK (`sni`, `municipality`) | Freshness per D6. |
| `ingest_job` | `id` (uuid), `sni`, `municipalities` (text[]), `status` (`queued`/`running`/`succeeded`/`failed`/`cancelled`), `phase`, `done`, `total`, `error`, `created_at`, `started_at`, `finished_at` | A cancelled job keeps rows already committed; it never updates `coverage` or runs the end-of-run delete. |
| `brave_usage` | `month` PK (`YYYY-MM`), `request_count` | Counter only (D7). |
| `company_outreach` | `orgnr` PK and FK → `company` (`ON DELETE CASCADE`), `called_at` (timestamp, null), `willing` (bool, null), `note` (text, max 500, null), `updated_at` | User-entered shared outreach state; one row per company. |

## API contract

All errors are `application/problem+json` with an extra `code` field. The frontend maps `code` to Swedish text in `frontend/src/texts/errors.ts`. An unexpected error is HTTP 500 with `code` `internal_error` and a fixed title; the response never includes the exception message. When the access gate is enabled (D17), every path except `/health` returns 401 `unauthenticated` without the shared password, and the browser shows its own password prompt.

| `code` | Status | Returned by |
|---|---|---|
| `invalid_sni` | 400 | `GET /api/codes/sni/{code}` when the code isn't 5 digits |
| `sni_not_found` | 404 | `GET /api/codes/sni/{code}` |
| `scb_unavailable` | 502, or 503 during SCB's nightly load (04:00–04:30) | `/api/codes/*`, `POST /api/searches` |
| `invalid_search` | 400 | `POST /api/searches`: invalid body, or an unknown SNI code or municipality |
| `invalid_search_query` | 400 | `GET /api/companies` |
| `job_not_found` | 404 | `GET /api/jobs/{jobId}`, `POST /api/jobs/{jobId}/cancel` |
| `job_not_cancellable` | 409 | `POST /api/jobs/{jobId}/cancel` |
| `company_not_found` | 404 | `PUT /api/companies/{orgnr}/outreach`, `POST /api/companies/{orgnr}/website-candidates` |
| `note_rejected` | 400 | `PUT /api/companies/{orgnr}/outreach` |
| `brave_quota_exceeded` | 429 | `POST /api/companies/{orgnr}/website-candidates` |
| `brave_unavailable` | 502 | `POST /api/companies/{orgnr}/website-candidates` |
| `invalid_url` | 400 | `POST /api/website-analysis` |
| `analysis_fetch_failed` | 502 | `POST /api/website-analysis` |
| `analysis_timeout` | 504 | `POST /api/website-analysis` |
| `unauthenticated` | 401 | Every path except `/health`, when the access gate is enabled |
| `internal_error` | 500 | Any endpoint, for an unexpected error |

### Health and codes

```text
GET  /health
GET  /api/codes/municipalities      → [{ code, name }]
GET  /api/codes/employee-classes    → [{ code, name }]
GET  /api/codes/revenue-classes     → [{ code, name }]
GET  /api/codes/sni                 → [{ code, name }]   (5-digit SNI 2025 codes, sorted by code)
GET  /api/codes/sni/{code}          → { code, name } | 404 sni_not_found
```

### Searches and jobs

```text
POST /api/searches                  body { sni, municipalities: [string], refresh: bool }
                                    → 202 { jobId, status }          (job started or already running)
                                    → 200 { jobId: null, status: "fresh" }
GET  /api/jobs/{jobId}              → { jobId, status, phase, done, total, error, startedAt, finishedAt }
POST /api/jobs/{jobId}/cancel       → 200 { same shape as GET /api/jobs/{jobId}, status: "cancelled" }
                                    → 404 job_not_found
                                    → 409 job_not_cancellable          (already succeeded or failed)
```

### Companies

```text
GET  /api/companies
     ?sni=96210&municipality=0180&municipality=0184
     &employeeClass=2&employeeClass=3&revenueClass=3
     &registeredFrom=2015-01-01&registeredTo=2024-12-31
     &revenueMin=&revenueMax=&netResultMin=&netResultMax=   (latest fiscal year, SEK)
     &hasFinancials=true
     &sort=name|revenue|netResult|registeredAt|employeeClass&order=asc|desc
     &page=1&pageSize=50
     → { items: [CompanyListItem], page, pageSize, totalCount,
         coverage: [{ municipality, fetchedAt, stale }] }

CompanyListItem = { orgnr, name, sni, employeeClass, revenueClass, revenueClassYear, registeredAt,
                    calledAt, willing, note,
                    workplaces: [{ name, municipality, city }],
                    financials: [{ periodEnd, revenue, netResult }] }   // newest first, max 3

Additional company filters:
     &called=true|false&willing=true|false
     // willing=null matches neither willingness filter.

PUT  /api/companies/{orgnr}/outreach
     body { called: bool, willing: bool | null, note: string | null }
     → 204
     → 404 company_not_found
     → 400 note_rejected for a note over 500 characters, containing a web address
       (http://, https:// or www.), or containing a personnummer-like number:
       6 or 8 digits, an optional - or +, then 4 digits (YYMMDD-NNNN, YYYYMMDDNNNN).
       There is no date check, so orgnr and phone numbers of that shape are rejected too.
```

### Websites

```text
POST /api/companies/{orgnr}/website-candidates
     → { candidates: [{ url, title, confidence: "high"|"medium"|"low", preselected }],
         remainingMonthlyQuota }

POST /api/website-analysis          body { url }
     → { url, fetchedAt, reworkScore,
         lighthouse: { performance, accessibility, bestPractices, seo },
         checks: { https, viewport, generator, copyrightYear, responseTimeMs } }
```

Responses from the two website endpoints have `Cache-Control: no-store`.
