# Decisions log

| # | Decision |
|---|---|
| D1 | **Internal tool, no login in the MVP.** All endpoints live under `/api` and hold no per-user data, so auth can be added later without redesign. |
| D2 | **Aktiebolag only** (`jurform` = `49`). Enskilda firmor are never stored. SCB rows whose `peOrgNr` starts with `19`/`20` (personnummer) are discarded immediately and never logged. |
| D3 | **Area = the workplace's municipality** (`belagenhetsadress.kommun` on AE), as in reacher. Never JE `kommunSate`. A search can have several municipalities. |
| D4 | **SNI = one exact 5-digit SNI 2025 code.** SCB only matches primary näringsgren (`rangordning` = `1`). |
| D5 | **Eligibility, fail closed with allow-lists:** `jurform IN ('49')`, `ftgStat IN ('1')`, `aeStat IN ('1')`, `reklamSparrTyp IN ('1')` on both company and workplace. We only store eligible companies and workplaces (data minimisation). `telefonSparrTyp` is not used (we don't call anyone). |
| D6 | **Ingest on demand.** Data per (SNI, municipality) is fresh for 30 days. Searching stale or missing data starts an ingest job. A "refresh" button forces one. |
| D7 | **Brave is called only when a user clicks for one company**, never in bulk, and only the top 3 results are used. Results (including URLs) are never stored: not in the database, logs, files, server caches or browser storage. We only store a monthly request counter. |
| D8 | **Website matching is a hybrid:** the backend filters out directory and social sites, scores the rest and pre-selects the best one if confidence is high enough. The user can pick another candidate or paste a URL. |
| D9 | **Website analysis = Google PageSpeed Insights API** (Lighthouse, mobile) + our own checks. Results are never stored server-side. The frontend keeps them in memory for the session only, not in `localStorage`. |
| D10 | **User-pasted URLs are treated like Brave results** (never stored) and validated against SSRF (`http`/`https` only, no private/loopback/link-local addresses). |
| D11 | **Postgres in Docker Compose locally.** All configuration via `appsettings` + environment variables, so the same containers can run in the cloud later. |
| D12 | **SCB codes are stored as raw text exactly as delivered** (`1` and `"1"` both become `"1"`), as in reacher. |
| D13 | **Code, issues and docs in English.** Text the end user sees in Swedish. |
| D14 | **Financials:** the 3 latest fiscal years from Bolagsverket iXBRL, tags `se-gen-base:Nettoomsattning` → `revenue` and `se-gen-base:AretsResultat` → `net_result`. |
| D15 | **One list row per company** (not per workplace). A company's matching workplaces are shown inside the row. |
| D16 | **Never log API keys, tokens, personnummer, or any URL from Brave or from a user.** That is why URLs are always sent in POST bodies, never in query strings. |
