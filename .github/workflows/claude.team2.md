# Team 2: Search API and website

Loaded together with the shared `claude.md`. That file holds the hard rules, decisions and contracts, and it always wins over this one. You are talking to a member of Team 2.

## Our job in one sentence
Serve the company list the way the user wants to see it, and for one company at a time find its website and judge whether it needs rework, without keeping anything Brave gives us; in week 47 build the website panel and analysis view.

## What we own
Folder paths are a suggestion until J-02 lands. Update the table if they change.

| Area | Where | Ticket |
|---|---|---|
| Company search `GET /api/companies` | `backend/src/Reacher.Api/Endpoints/` + query in `Reacher.Data` | T2-01 |
| Search and job endpoints | `backend/src/Reacher.Api/Endpoints/` | T2-02 |
| Brave client, fake client, usage counter | `backend/src/Reacher.WebsiteAnalysis/Brave/` | T2-03 |
| Website matching | `backend/src/Reacher.WebsiteAnalysis/Matching/` | T2-04 |
| URL validation, PageSpeed, own checks, rework score | `backend/src/Reacher.WebsiteAnalysis/Analysis/` | T2-05 |
| Website endpoints | `backend/src/Reacher.Api/Endpoints/` | T2-06 |
| Matching evaluation | `docs/matching-evaluation.md` | T2-07 |
| "Find website" panel | `frontend/src/features/website/` | T2-08 |
| Analysis view, session score store, score column | `frontend/src/features/analysis/` | T2-09 |

## What we consume and what we hand over
- **We consume:** Team 1's tables, read only. We never write `company`, `workplace`, `financial_fact` or `coverage`. Also their dev seed (T1-01) and `IIngestCoordinator` (T1-07).
- **We hand over:** the API endpoints the frontend uses, and the action/score columns for Team 1's company table.
- **We are waiting on Team 1 for:** T1-01 → T2-01 (start on your own fictional fixtures); T1-07 → T2-02 (start on the fake coordinator); T1-05 → T2-07; T1-10 → T2-09.

## Working rules

### Transient data (D7, D9, D10)
- A Brave response lives in local variables for one HTTP request. Never in fields, statics, caches, the database, logs, files or exceptions.
- Both website endpoints send `Cache-Control: no-store`. Request body logging stays off for them (if `HttpLogging` is used, exclude these routes).
- The usage counter is incremented **before** the Brave call, atomically in the database (one `UPDATE … SET request_count = request_count + 1` or upsert), so two parallel clicks can't both slip past the cap.
- Development and tests use `FakeBraveSearchClient` (`Brave:UseFake = true`). The real client is only for deliberate manual checks and T2-07 (≤ 150 requests). Tell the team before using more than a handful of real requests.
- Every PR that touches Brave, matching or analysis includes this checklist in its description:
  - [ ] Nothing from Brave, a pasted URL or an analysis is written to the database, logs, files or caches
  - [ ] No URL in a query string or log message
  - [ ] Tests use the fake client and recorded fixtures with made-up sites

### SSRF (D10)
- Allow only `http`/`https`, default ports, and no credentials in the URL.
- Resolve DNS and reject loopback, private, link-local, CGNAT (`100.64.0.0/10`), `0.0.0.0/8`, `::1`, `fc00::/7`, `fe80::/10` and IPv4-mapped IPv6 versions of them.
- Guard against DNS rebinding: check the address actually connected to, using `SocketsHttpHandler.ConnectCallback`, not only an earlier lookup.
- Handle redirects yourself (`AllowAutoRedirect = false`), at most 5, re-validating every hop.
- Cap the body (2 MB) and the time (10 s) for our own checks.
- PageSpeed fetches the page from Google's servers, but validate before sending anyway. There is no point analysing `localhost`.

### Company search (T2-01)
- One EF query that translates to one SQL statement, with filtering, sorting and paging in the database. Check the generated SQL with EF logging.
- "Latest fiscal year" = the newest `period_end` per company. Companies missing the sorted value always go last.
- The filters are exactly those in `docs/contracts.md`. A new filter is a contract change (joint).

### Scores (T2-05)
- The rework formula and weights live in `ReworkScoreOptions`. The starting values are an *assumption*. A change that alters what the score means gets a line in `docs/decisions.md`.
- Lighthouse scores vary between runs (a few points either way is normal). Show the score as a guide, not an exact value.

## Frontend rules (week 47)
- Candidates and analysis results live only in React state or context. Brave and analysis calls are mutations (`useMutation`), not cached queries. Nothing goes in `localStorage`, `sessionStorage`, the page URL or router state.
- The session score store maps `orgnr → { reworkScore, analysedAt }`, without the URL.
- External links use `target="_blank" rel="noopener noreferrer"`.
- All user-facing strings in Swedish, in `frontend/src/texts/`. API error `code`s are translated in `src/texts/errors.ts`.

## Tickets
| ID | Issue | Title | Week | Blocked by | Status |
|---|---|---|---|---|---|
| T2-01 | [#13](https://github.com/Kisslo/reacher-csharp/issues/13) | [BE] Company search API | 45 | J-01, J-02 | Todo |
| T2-03 | [#15](https://github.com/Kisslo/reacher-csharp/issues/15) | [BE] Brave client | 45 | J-02, J-03 | Todo |
| T2-02 | [#14](https://github.com/Kisslo/reacher-csharp/issues/14) | [BE] Search and job endpoints | 46 | J-01, T1-07 (start on fake) | Todo |
| T2-04 | [#16](https://github.com/Kisslo/reacher-csharp/issues/16) | [BE] Website matching | 46 | T2-03 | Todo |
| T2-05 | [#17](https://github.com/Kisslo/reacher-csharp/issues/17) | [BE] Website analysis | 46 | J-02, J-03 | Todo |
| T2-06 | [#18](https://github.com/Kisslo/reacher-csharp/issues/18) | [BE] Website endpoints | 46 | J-01, T2-04, T2-05 | Todo |
| T2-07 | [#19](https://github.com/Kisslo/reacher-csharp/issues/19) | Measure matching accuracy | 46 | T2-04, T1-05 | Todo |
| T2-08 | [#23](https://github.com/Kisslo/reacher-csharp/issues/23) | [FE] "Find website" panel | 47 | J-05, T2-06 | Todo |
| T2-09 | [#24](https://github.com/Kisslo/reacher-csharp/issues/24) | [FE] Analysis view and score column | 47 | T2-08, T1-10 | Todo |

T2-05 doesn't depend on Brave, so it can start as soon as J-03 is merged, in parallel with T2-03.

## Team status
*2026-10-27:* nothing started yet. J-01 is merged. T2-01 can start after J-02 on our own fixtures; T2-03 and T2-05 after J-03.

## Team 2 open questions
- **Brave plan:** confirm the limits when the key is set up and set `Brave:MonthlyCap` below them.
- **PageSpeed quota:** confirm the key's daily quota in Google Cloud Console.
- **Score sorting scope (T2-09):** the ticket sorts analysed companies on the current page only. Is that enough, or should it cover every company analysed this session?
