# Frontend

React + Vite application for Reacher. The frontend is served separately during
development and proxies API requests to the ASP.NET backend.

## Prerequisites

- Node.js 24 or newer
- npm
- The backend running locally when generating API types

## Install and run

From this directory:

```powershell
npm ci
npm run dev
```

The Vite development server proxies `/api` and `/health` to
`http://localhost:5080` by default. To use another backend address, set
`VITE_API_PROXY_TARGET` before starting Vite:

```powershell
$env:VITE_API_PROXY_TARGET = "http://localhost:5090"
npm run dev
```

## Scripts

| Command                | Purpose                                                   |
| ---------------------- | --------------------------------------------------------- |
| `npm run dev`          | Start the Vite development server                         |
| `npm run build`        | Type-check and create a production build                  |
| `npm run lint`         | Run ESLint                                                |
| `npm run typecheck`    | Run TypeScript without emitting files                     |
| `npm test`             | Run the tests once                                        |
| `npm run test:watch`   | Re-run tests while files change                           |
| `npm run format`       | Format frontend files with Prettier                       |
| `npm run format:check` | Check Prettier formatting                                 |
| `npm run gen:api`      | Generate TypeScript types from the local OpenAPI document |

## API types

Start the backend in Development mode before generating types. The default
command reads `http://localhost:5080/openapi/v1.json` and writes the generated
types to `src/api/generated.ts`:

```powershell
npm run gen:api
```

The generated file is committed and must not be edited manually. When a backend
API contract changes, regenerate the file in the same change.

## Frontend conventions

- User-facing strings belong in `src/texts/` and are written in Swedish.
- API problem `code` values are translated to Swedish in
  `src/texts/errors.ts`.
- Website candidates, pasted URLs, and website analysis results are transient.
  Never write them to `localStorage`, `sessionStorage`, the page URL, or another
  persistent browser store. Later website features should keep this data only in
  React state or context as required by the project decisions.

## CI

The frontend CI job runs from this directory:

```powershell
npm ci
npm run lint
npm run typecheck
npm test
npm run build
```

CI uses the committed generated API types and does not require a running backend
or any external API.

## Testing

Tests use Vitest and Testing Library. They run in jsdom, so no browser and no
backend are required. `npm test` is the command CI runs. `npm run test:watch`
re-runs them while you edit.

Put a test next to the code it covers, named `*.test.ts` or `*.test.tsx`.
Query the screen the way a user would: by role, label, or text. Never call a
real API. Replace `fetch` with `vi.stubGlobal('fetch', …)` and return made-up
JSON.

- A pure function is called directly. See `src/features/search/searchRules.test.ts`
  and `src/features/search/searchUrl.test.ts`.
- A component that loads data is rendered with `renderWithQuery` from
  `src/test/render.tsx`. That helper wraps it in a new React Query client with
  retries off, so tests do not share a cache. See
  `src/features/search/MunicipalityPicker.test.tsx` for keyboard input.

T1-10, T2-08, and T2-09 should follow the same pattern.
