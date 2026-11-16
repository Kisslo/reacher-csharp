# SCB's AFR API: endpoints, fields and codes

Reference for the SCB client and ingest (`backend/src/Reacher.Ingestion/Scb/`): what SCB
sends, and how it becomes `company` and `workplace` rows (`docs/contracts.md`).

**Sources**
- The OpenAPI contract, `https://apiafr.scb.se/swagger/v1/swagger.json`. SCB calls it the normative contract. Types marked *contract* come from there.
- SCB's Variabelbeskrivning (a local PDF, `Variabelbeskrivning_scb.pdf`, gitignored): what the codes mean.
- Real responses read on 2026-10-07 with a local key. Types and formats marked *response* were checked there.

This file and the test fixtures hold no real data.

## Connection

| | |
|---|---|
| Base URL | `https://apiafr.scb.se/v1` (`Scb:BaseUrl`). The version is in the path |
| Key | Header `X-API-Key`, from `Scb:ApiKey`. Without a key: 401 "Invalid or missing API key" |
| Errors | `application/problem+json` with `type`, `title`, `status`, `detail`, `instance`, `traceId`. Common codes: 400, 401, 404, 429, 500, 503 |
| Page size | `limit`, max 5000 (`Scb:PageSize`) |
| Rate limit | Not documented. SCB answers 429 when overloaded. We send at most `Scb:RequestsPerSecond` (default 2) |
| Retries | 408, 429, 5xx, network errors and timeouts: 3 retries with exponential backoff (about 2, 4 and 8 s), following `Retry-After` up to 60 s. 401 and TLS errors are never retried |
| Nightly load | Closed 04:00–04:30 Swedish time. The client refuses every call in that window, and the API answers `scb_unavailable` (503) |
| Last update | `GET /v1/api-info` → `senasteUppdateringsDatum` |

## Endpoints

The API has two kinds of unit: **JE** (juridisk enhet, the company, key `peOrgNr`) and **AE** (arbetsställe, the workplace, key `cfarNr`). One `workplace` row is one AE.

| Endpoint | Response | `IScbClient` | Used by |
|---|---|---|---|
| `GET /v1/arbetsstallen/naringsgren/{kod}?limit=&cursorId=` | `pagination` + `arbetsstallen[]`, AE *partial* | `GetWorkplacesBySni` | The ingest: every workplace with one SNI code |
| `GET /v1/juridiskaenheter/{peOrgNr}/full` | One JE, all variables | `GetLegalUnit` | The ingest: once per company per run |
| `GET /v1/kodtabeller/{tabell}` | A list of `kod` + `klartext` | `GetCodeTable` | `/api/codes/*` and the search check |
| `GET /v1/arbetsstallen/naringsgren/{kod}/count` | `count`, `path` | `CountWorkplacesBySni` | Nothing yet |
| `GET /v1/arbetsstallen/{cfarNr}/full` | One AE, all variables | `GetWorkplace` | Nothing yet. The list already has every workplace field we store |

Do **not** use:
- `/juridiskaenheter/kommun/{kod}`: filters on the company's registered municipality (`kommunSate`), for a sole trader the owner's home municipality. Our area is the workplace's municipality (D3).
- `/arbetsstallen/kommun/{kod}`: every industry in the municipality, many times more rows than the SNI filter.
- `/arbetsstallen/full` and `/juridiskaenheter/full`: the whole register without a filter.

### No combined filter

Each list endpoint filters on one thing: industry, municipality, county or employee class, and for JE also revenue class and legal form. "SNI 96210 in municipality 0180" can't be asked for. The ingest therefore:

1. Lists the SNI code nationally: `/arbetsstallen/naringsgren/{kod}`.
2. Drops every row whose `peOrgNr` isn't `16` + an orgnr, before anything else (D2).
3. Keeps workplaces whose `belagenhetsadress.kommun` is one of the searched municipalities, whose primary SNI is the searched code, and that pass D5.
4. Fetches `/juridiskaenheter/{peOrgNr}/full` once per company, since several workplaces can belong to one company.

The industry filter matches only the **primary** industry (`rangordning = 1`, D4).

Call budget (*response*, 2026-10-07): `96210` has 18,415 workplaces nationwide, so `limit=5000` gives 4 list calls, plus one JE call per company with a workplace in the searched municipalities. 3,625 of those workplaces are in Stockholm (0180).

### Paging

The list is sorted by `peOrgNr`, ascending (*response*). All legal persons (`16…`) therefore come before all sole traders (`19…`/`20…`), and a sample of the first pages isn't representative.

Cursor, not offset:
1. The first call has no `cursorId`.
2. The response has `pagination` with `nextCursorId` (number), `limit` (number) and `hasMore` (bool).
3. The next call sends `cursorId=<nextCursorId>` with the **same** `limit`. Repeat until `hasMore` is `false`.

The client decides the end on `hasMore` only, never on `nextCursorId`. If `hasMore` is true but the cursor is missing, it throws.

## Field mapping

*Partial* = the list call, *full* = the `/full` call. The columns are in `docs/contracts.md`.

### `company` (JE full, except `orgnr`)

| Column | SCB field | Note |
|---|---|---|
| `orgnr` | `peOrgNr` (AE partial) | 12 digits: `16` + the orgnr. The `16` is stripped, and the rest must be 10 digits with a third digit 2–9. Anything else, such as `19`/`20` + a personnummer, is dropped (D2). JE's own `orgNr` isn't used |
| `name` | `foretagsnamn`, else `namn` | The first that isn't blank after trim. `foretagsnamn` = registered company name, `namn` = legal name |
| `sni` | `naringsgrenar[]` with `rangordning = 1` | Falls back to the searched code |
| `legal_form` | `jurform` | Must be in `Ingest:AllowedLegalForms` (`49`) |
| `company_status` | `ftgStat` | Must be `1` |
| `employee_class` | `anstKl` (JE) | The whole company. The workplace's own `anstKl` isn't stored |
| `revenue_class` | `omsKl` | |
| `revenue_class_year` | `omsAr` | A number |
| `registered_at` | `regDat` | UTC, converted to a Swedish date (see below) |
| `ad_block_type` | `reklamSparrTyp` (JE) | Must be `1` |

A company without a name, `anstKl`, `omsKl` or `omsAr` is skipped and counted, because those columns are NOT NULL. They are never filled with a guess.

### `workplace` (AE partial)

| Column | SCB field | Note |
|---|---|---|
| `cfar` | `cfarNr` | A number in JSON, stored as text |
| `orgnr` | `peOrgNr` | As for `company` |
| `name` | `ben` | Null if blank. *Response:* only 115 of 5,000 workplaces on the first page had text; 2,658 had `""` and 2,227 had `" "` |
| `sni` | `primarNaringsgren.naringsgren` | `"96210"`, five digits without a dot. Must be the searched code |
| `municipality` | `belagenhetsadress.kommun` | The workplace's municipality (D3). Never JE `kommunSate` |
| `street` | `belagenhetsadress.bGatuAdress` | The visiting address. Never `postAdress` |
| `postal_code` | `belagenhetsadress.bPostNr` | |
| `city` | `belagenhetsadress.bPostOrt` | |
| `workplace_status` | `aeStat` | Must be `1` |
| `ad_block_type` | `reklamSparrTyp` (AE) | Must be `1`. The block exists on **both** the company and the workplace |

Never mapped (data minimisation): `postAdress`, `kommunSate`, `lanSate`, `tel`, `epost`, `epostSparrTyp`, `telefonSparrTyp` (we don't call anyone, D5) and `geografiskInformation` (coordinates, for a sole trader sometimes a home). The models in `ScbModels.cs` leave them out, so the serializer ignores them.

### Text, codes and dates

- **Codes are text (D12).** SCB mixes strings and numbers, and the same field can change type between endpoints. `ScbCodeConverter` turns `1` and `"1"` into `"1"` and changes nothing else (`"01"` stays `"01"`).
- **Blank is `""` or `" "`, not `null`.** The sample had no `null` at all. The ingest trims, and a blank value becomes null, or `""` for the NOT NULL address columns.
- **Dates** arrive as `YYYY-MM-DDThh:mm:ss.sssZ` in UTC (*response*). A date without a time is sent as Swedish midnight, e.g. `2025-02-02T23:00:00.000Z` for 3 February (`T22:00` in summer), so the ingest converts to `Europe/Stockholm` before taking the date.
- **`slutDat`** is missing on an AE that has none, and can be an old date on an active JE. It is never used for status: `ftgStat` and `aeStat` decide.

## Code fields

| Field | Level | Contract (data) | Contract (code table) | Codes |
|---|---|---|---|---|
| `jurform` | JE | string | string | 2 digits. `10` natural person (sole trader), `31` HB/KB, `49` other AB, `91` undivided estate, `99` not investigated. Full list: `kodtabeller/jurformkoder` |
| `ftgStat` | JE | string | string | `0` never active, `1` active, `9` no longer active |
| `aeStat` | AE | string (partial), number (full) | number | `0` never active, `1` active, `9` no longer active |
| `reklamSparrTyp` | JE, AE | number | number | `1` accepts advertising, `2` has opted out (*response*, code table) |
| `anstKl` | JE, AE | string (JE, AE partial), number (AE full) | number | See "Employee classes" |
| `omsKl` | JE | string | number | See "Revenue classes" |
| `bolStat` | JE | string | number | See "Bolagsverket status". Not used |
| `naringsgren` | AE, JE | string | string | A five-digit SNI 2025 code, e.g. `96210` hairdressers and barbers |

### Employee classes (`anstKl`)

`anstklkoder` (*response*): `0` unknown, `1` 0 employees, `2` 1–4, `3` 5–9, `4` 10–19, `5` 20–49, and so on up to `16` (10,000 or more). This is not the SME scale.

### Revenue classes (`omsKl`)

JE full has `omsKl` and `omsAr` (the year, a number). SCB computes it from VAT returns. Classes: `0` < 1 tkr, `1` 1–499 tkr, `2` 500–999 tkr, `3` 1,000–4,999 tkr, `4` 5,000–9,999 tkr, `5` 10,000–19,999 tkr, `6` 20,000–49,999 tkr, `7` 50,000–99,999 tkr, `8` 100,000–499,999 tkr, `9` 500,000–999,999 tkr, `10` 1,000,000–4,999,999 tkr, `11` 5,000,000–9,999,999 tkr, `12` > 9,999,999 tkr. There is also a filter, `/juridiskaenheter/omsattningsklass/{kod}`, which we don't use.

### Bolagsverket status (`bolStat`)

JE full has `bolStat`: `0` normal, `11`–`13` composition, `20`–`24` bankruptcy, `31`–`35` liquidation, `36`–`78` deregistered, merger, conversion and more, `80`–`82` company reconstruction, `85`–`87` resolution, `90`–`99` division and takeover. Full list: `kodtabeller/bolstatkoder`. Units not registered with Bolagsverket always have `0`. Not used: see "Open questions" in `claude.md`.

## Code tables for `/api/codes` (T1-08)

SCB has a table for all four lists, so no static SNI file is needed. Table names and `kod` types come from the OpenAPI contract. Every table answers with `kod` and `klartext`. `naringsgrenkoder` also has `avdelningKod` and `avdelningText`, which we don't map.

| Our endpoint | SCB table | `kod` in the contract | Order |
|---|---|---|---|
| `/api/codes/municipalities` | `kommunkoder` | string | By name, Swedish order |
| `/api/codes/employee-classes` | `anstklkoder` | number | By number (`10` after `2`) |
| `/api/codes/revenue-classes` | `omsklkoder` | number | By number |
| `/api/codes/sni` | `naringsgrenkoder` | string | 5-digit codes, by code |
| `/api/codes/sni/{code}` | `naringsgrenkoder` | string | Lookup by code |

`ScbCodeTables` reads them through `IScbClient.GetCodeTable` and caches each table for 24 hours. Municipality and SNI codes are padded to 4 and 5 digits, and a dot in an SNI code is removed. The SNI list and the single-code lookup share that cache and keep only 5-digit codes, so a higher level such as `96` or `9621` is neither listed nor accepted in a search. A 4-digit code is kept only when it is a dropped leading zero of division 01, 02 or 03 (`1110` is `01110`). Class codes stay as delivered, because they are compared with `company.employee_class` and `company.revenue_class`.

`kommunkoder` has 292 rows (measured 2026-10-30): the 290 municipalities plus `0000` (Kommunkod saknas) and `9999` (Ej svensk hemortskommun). Only codes in counties 01–25 are kept, so those two are neither listed nor accepted in a search.

## Recorded responses

In `backend/tests/Reacher.Ingestion.Tests/Scb/Fixtures/`, read by `ScbClientTests`. The shape is copied from real responses (2026-10-07). The values are made up.

| File | Call | Content |
|---|---|---|
| `ae_naringsgren_96210.json` | `/arbetsstallen/naringsgren/96210?limit=2` | A first page (`hasMore: true`): two workplaces of the same made-up AB, one in Stockholm (0180) and one in Solna (0184) whose `ben` is one space |
| `ae_full.json` | `/arbetsstallen/90000001/full` | The Stockholm workplace |
| `je_full.json` | `/juridiskaenheter/165599001236/full` | The made-up AB |

Rules for fixtures:
- Orgnr are made up, and there is never a `19`/`20` row (hard rule 1).
- Names, streets and coordinates are made up. Postal codes, municipality and county codes and code values may be real, because they don't point anyone out.
- The shape is exactly as in the real response: field names, order, empty strings instead of `null`, missing fields, JSON types and date formats.
