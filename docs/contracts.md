# Contracts

## Database schema (Postgres, EF Core)

| Table | Columns | Notes |
|---|---|---|
| `company` | `orgnr` PK (10 digits), `name`, `sni`, `legal_form`, `company_status`, `employee_class`, `revenue_class`, `revenue_class_year`, `registered_at` (date, null), `ad_block_type`, `updated_at` | Codes as text (D12). Only eligible companies (D5). |
| `workplace` | `cfar` PK, `orgnr` FK → `company`, `name` (null), `sni`, `municipality`, `street`, `postal_code`, `city`, `workplace_status`, `ad_block_type`, `updated_at` | Visiting address only, never `postAdress`. |
| `financial_fact` | `orgnr` FK, `period_end` (date), `key`, `value` (numeric), `document_id`, `fetched_at`. PK (`orgnr`, `period_end`, `key`) | `key` from the tag map (D14). |
| `coverage` | `sni`, `municipality`, `fetched_at`. PK (`sni`, `municipality`) | Freshness per D6. |
| `ingest_job` | `id` (uuid), `sni`, `municipalities` (text[]), `status` (`queued`/`running`/`succeeded`/`failed`), `phase`, `done`, `total`, `error`, `created_at`, `started_at`, `finished_at` | |
| `brave_usage` | `month` PK (`YYYY-MM`), `request_count` | Counter only (D7). |

## API contract

All errors are `application/problem+json` with an extra `code` field (e.g. `sni_not_found`, `invalid_url`, `brave_quota_exceeded`, `analysis_timeout`). The frontend maps `code` to Swedish text.

### Health and codes

```text
GET  /health
GET  /api/codes/municipalities      → [{ code, name }]
GET  /api/codes/employee-classes    → [{ code, name }]
GET  /api/codes/revenue-classes     → [{ code, name }]
GET  /api/codes/sni/{code}          → { code, name } | 404 sni_not_found
```

### Searches and jobs

```text
POST /api/searches                  body { sni, municipalities: [string], refresh: bool }
                                    → 202 { jobId, status }          (job started or already running)
                                    → 200 { jobId: null, status: "fresh" }
GET  /api/jobs/{jobId}              → { jobId, status, phase, done, total, error, startedAt, finishedAt }
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
                    workplaces: [{ name, municipality, city }],
                    financials: [{ periodEnd, revenue, netResult }] }   // newest first, max 3
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
