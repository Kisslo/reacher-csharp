# Team 1: Company data

Loaded together with the shared `claude.md`. That file holds the hard rules, decisions and contracts, and it always wins over this one. You are talking to a member of Team 1.

## Our job in one sentence
Turn SCB and Bolagsverket data into clean, eligible `company`, `workplace` and `financial_fact` rows for whatever SNI code and municipalities a user asks for, and in week 47 build the search form and company table on top of them.

## What we own
Folder paths are a suggestion until J-02 lands. Update the table if they change.

| Area | Where | Ticket |
|---|---|---|
| Entities, DbContext, migrations, dev seed | `backend/src/Reacher.Data/` | T1-01 |
| SCB client | `backend/src/Reacher.Ingestion/Scb/` | T1-02 |
| iXBRL parser | `backend/src/Reacher.Ingestion/Bolagsverket/IxbrlParser.cs` | T1-03 |
| Bolagsverket client | `backend/src/Reacher.Ingestion/Bolagsverket/` | T1-04 |
| SCB ingest + eligibility (D5) | `backend/src/Reacher.Ingestion/Scb/ScbIngestService.cs` | T1-05 |
| Financials ingest | `backend/src/Reacher.Ingestion/Bolagsverket/FinancialsIngestService.cs` | T1-06 |
| Ingest job: queue, worker, coordinator, freshness | `backend/src/Reacher.Ingestion/Jobs/` + registration in `Reacher.Api` | T1-07 |
| Code tables + `/api/codes/*` | `backend/src/Reacher.Api/Codes/` | T1-08 |
| SCB field documentation | `docs/scb-fields.md` | – |
| Search form, ingest progress | `frontend/src/features/search/` | T1-09 |
| Company table and filters | `frontend/src/features/companies/` | T1-10 |

## What we hand over and what we consume
- **We deliver:** the tables in `docs/contracts.md`, holding eligible companies only (D5); `IIngestCoordinator` for T2-02; the code-table endpoints.
- **We consume:** Team 2's `GET /api/companies` (T2-01) and the search/job endpoints (T2-02), for our frontend in week 47.
- **Team 2 is waiting on us for:** T1-01 (schema + seed) → T2-01; T1-07 → T2-02 (they start on a fake); T1-05 → T2-07 (real companies to evaluate matching on).

## Working rules
- **Raw in the client, clean in the ingest.** Clients return what the API sends. Ingest services normalise: orgnr as 10 digits (strip the `16` prefix), codes converted to string and nothing else (D12), names trimmed. Fixtures can then be as messy as reality.
- **Drop personnummer first.** Filter out `peOrgNr` starting with `19`/`20` as the very first step after reading a page, before mapping, logging or caching (D2). A test with a capturing logger proves it never reaches the log.
- **Allow-lists only (D5):** `jurform IN ('49')`, `ftgStat IN ('1')`, `aeStat IN ('1')`, `reklamSparrTyp IN ('1')` on the company **and** on the workplace. Allowed values come from config (`AllowedLegalForms`) or named constants, never from a `!=` check.
- **Workplace address only (D3):** `belagenhetsadress.*`. `postAdress` and `kommunSate` are never mapped into our types.
- **Names:** company name = `foretagsnamn`, else `namn`. Workplace name = `ben` if not blank after trim. In reacher only 115 of 5,000 workplaces had a non-blank `ben`, so expect most to be empty.
- **SCB has no combined filter.** List by SNI nationally and filter by workplace municipality in code (see `docs/scb-fields.md`). Reacher measured 3,625 workplaces for SNI 96210 in Stockholm (0180), so a large search means thousands of detail calls. Fetch each company (`/juridiskaenheter/{peOrgNr}/full`) once per run.
- **SCB etiquette:** retry on 429/5xx with backoff and `Retry-After` (max 60 s). Refuse to start 04:00–04:30 Swedish time (nightly load). About 4 requests/s is an *assumption*; the limit is undocumented.
- **Bolagsverket etiquette:** at most 1 request/s. The token is cached and renewed 60 s early. orgnr goes only in POST bodies, never in URLs or logs. Documents are unpacked in memory, never written to disk.
- **TLS errors are never retried, and certificate validation is never turned off.** Some Windows machines in reacher lacked the Telia Root CA v2 for Bolagsverket. .NET uses the Windows certificate store, which normally has it; if not, fix the machine, not the code.
- **No report = no rows, never 0.** A year without a revenue row means "unknown", not zero revenue. The filters treat it as "no data".
- **Real data stays local.** Raw API responses go in a gitignored folder (`/scb_raw/` is already ignored; add one for Bolagsverket if needed). Fixtures are anonymised or made up.
- **Report real runs.** A PR that changes ingest includes counts from a real run (fetched, dropped per rule, stored, duration), without real names or orgnr.

### iXBRL lessons from reacher
Real annual reports are messier than the fixture. These rules all come from bugs found on real data:
- Read only `iso4217:SEK`, whole kronor, and contexts **without dimensions**. Apply `scale` and `sign="-"`. Never read a minus sign from the text, and `decimals` never changes a value.
- **Skip the Flerårsöversikt** (found by its heading, ending at the next known heading). In real reports it is often in tkr and sometimes tagged with the wrong `scale` (415 kr, 730 Mkr). Use the Resultaträkning.
- A report usually holds the current year plus a comparison year. **The newest report wins for a fiscal year**, because comparison figures get restated. Process reports newest first, and download a report only if its own fiscal year isn't stored yet.
- Small companies on K2 often leave empty lines out, so a year can have `net_result` but no `revenue`. That is "unknown", not 0.
- Each report gives about 2 years, so a first run downloads about 2 reports per company. Plan the run time for that (reacher: ~1 h for 1,375 companies at 1 request/s).

## Frontend rules (week 47)
- All user-facing strings in Swedish, in `frontend/src/texts/`. Amounts formatted with `sv-SE`.
- Search parameters and filters go in the page URL. That's fine because they contain no Brave data.
- Team 2 owns the action and score columns in the company table. Leave a slot and agree the props in the PR.

## Tickets
| ID | Issue | Title | Week | Blocked by | Status |
|---|---|---|---|---|---|
| T1-01 | [#5](https://github.com/Kisslo/reacher-csharp/issues/5) | [BE] EF Core entities and first migration | 45 | J-01, J-02 | Done (PR #30) |
| T1-02 | [#6](https://github.com/Kisslo/reacher-csharp/issues/6) | [BE] SCB client | 45 | J-02, J-03 | Done (PR #31) |
| T1-03 | [#7](https://github.com/Kisslo/reacher-csharp/issues/7) | [BE] iXBRL parser | 45 | J-02 | Done (PR #38) |
| T1-04 | [#8](https://github.com/Kisslo/reacher-csharp/issues/8) | [BE] Bolagsverket client | 45 | J-02, J-03 | In review |
| T1-08 | [#12](https://github.com/Kisslo/reacher-csharp/issues/12) | [BE] Code tables and endpoints | 45 | T1-01, T1-02 | Todo |
| T1-05 | [#9](https://github.com/Kisslo/reacher-csharp/issues/9) | [BE] SCB ingest pipeline | 46 | T1-01, T1-02 | Todo |
| T1-06 | [#10](https://github.com/Kisslo/reacher-csharp/issues/10) | [BE] Financials ingest | 46 | T1-01, T1-03, T1-04 | Todo |
| T1-07 | [#11](https://github.com/Kisslo/reacher-csharp/issues/11) | [BE] Ingest job: queue, background worker, status | 46 | T1-05, T1-06 | Todo |
| T1-09 | [#21](https://github.com/Kisslo/reacher-csharp/issues/21) | [FE] Search form and ingest progress | 47 | J-05, T1-08, T2-02 | Todo |
| T1-10 | [#22](https://github.com/Kisslo/reacher-csharp/issues/22) | [FE] Company table with filters and sorting | 47 | J-05, T2-01 | Todo |

Week 45 is the heaviest. If something slips, T1-08 can wait (only T1-09 in week 47 needs it).

## Team status
*2026-10-27:* nothing started yet. J-01 is merged. T1-03 can start as soon as J-02 is merged; T1-01 too.

## Team 1 open questions
- **Stop paging at the first `19`/`20` (T1-05):** the list is sorted by `peOrgNr`, so this would save calls. Only do it with a test showing it holds, and log how many pages were skipped.
- **Ingest duration (T1-07):** measure how long a large search takes and tell Team 2, so the progress view can set expectations.
- **Financials and the 30-day freshness:** annual reports change yearly, so a refresh re-checks the document list but rarely downloads anything. Confirm that's how T1-06 behaves.
