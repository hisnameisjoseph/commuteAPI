# CommuteApi

A small C# service that reads your next calendar event, works out where you are going, and tells the [ptv-display](https://github.com/hisnameisjoseph/ptv-display) departures board which stop and route to pin to the top of the screen.

**Status:** In progress. Milestone 0 (scaffold) is done. This README is the build specification. Sections describe the target design unless marked otherwise.

---

## Table of contents

1. [Why this exists](#why-this-exists)
2. [Features](#features)
3. [Non-goals](#non-goals)
4. [Architecture](#architecture)
5. [Tech stack](#tech-stack)
6. [Data model](#data-model)
7. [API](#api)
8. [Matching logic](#matching-logic)
9. [Development plan](#development-plan)
10. [Getting started](#getting-started)
11. [Configuration](#configuration)
12. [Testing](#testing)
13. [Deployment](#deployment)
14. [Security and privacy](#security-and-privacy)
15. [Project structure](#project-structure)
16. [Decisions](#decisions)
17. [Roadmap](#roadmap)
18. [Licence](#licence)

---

## Why this exists

### The problem

`ptv-display` is a Melbourne public transport departures board that runs on a wall display or a phone. It shows the stops I have configured, all day, regardless of where I actually need to go next. Calendar apps know where I need to be, but they only give directions when I open them and ask.

### The solution

CommuteApi connects the two. It reads the next event in my Outlook calendar (for example, a 9am shift at Highpoint), matches it to a saved place, and returns the stop and route that the display should prioritise. When I am signed in, the board shows the right departures before I ask. Zero interaction.

### Why it is built this way

This project is also a deliberate learning exercise in the Microsoft stack:

| Skill | Where it appears in this project |
|---|---|
| C# and ASP.NET Core MVC | The whole service, including the admin pages |
| Entity Framework Core | All database access and migrations |
| SQL Server / Azure SQL | Relational storage for places and match history |
| Microsoft 365 integration | Outlook calendar via the Microsoft Graph API |
| Microsoft Entra ID | Admin sign-in and delegated calendar access |
| System integration | An existing TypeScript app (ptv-display) consuming a new C# service |
| Azure | App Service, Azure SQL, Key Vault, managed identity, Application Insights, Bicep, Azure OpenAI |

---

## Features

### Must have (MVP)

1. **Saved places.** Create, edit and delete places through a small MVC admin page that requires Microsoft sign-in. Each place maps a name to a PTV stop and route.
2. **Calendar read.** Fetch the next upcoming event from the Outlook calendar through Microsoft Graph.
3. **Event to place matching.** Match the event to a saved place using the location field first, then the title, with an LLM fallback for vague titles.
4. **Commute endpoint.** One JSON endpoint that the ptv-display Worker calls, server to server, to learn which stop and route to pin.
5. **ptv-display integration.** For a signed-in viewer, the board adds a temporary primary card for the matched stop, filtered to the matched route. Signed-out visitors see the normal public board.

### Nice to have (after MVP)

- CSV import of places (extract, validate, load).
- "Leave by" time based on the event start and a per-place lead time.
- Google Calendar as a second calendar provider.
- Match history page showing which method matched each event.
- Stop picker on the Places form that searches ptv-display's `/api/search`, so stop IDs are never typed by hand.

---

## Non-goals

- **No journey planning.** The service does not calculate routes between two points. Places are mapped to stops by hand. The PTV Timetable API v3 is not used for routing here.
- **No multi-user support, for now.** Single user, single calendar account. Letting each viewer sign in and see their own calendar is on the [roadmap](#roadmap), not in the MVP. See [ADR 3](docs/adr/0003-protect-calendar-data-behind-a-worker-proxy-and-sign-in.md).
- **No writing to the calendar.** Read-only access.
- **No departure times.** ptv-display already fetches live departures. CommuteApi only says what to prioritise.
- **No trams.** ptv-display shows trains and buses only, so a place must use one of those modes.

---

## Architecture

```
                                       Browser (wall display or phone)
                                                    |
                                                    | HTTPS, signed in via
                                                    | Cloudflare Access (Microsoft or Google)
                                                    v
+------------------+        +----------------------+        +------------------------+
|  Outlook         |        |  CommuteApi          |        |  ptv-display Worker    |
|  Calendar        | <----- |  (ASP.NET Core MVC,  | <----- |  (Cloudflare Workers + |
|  (Microsoft      |  Graph |   Azure App Service) |  HTTPS |   Static Assets)       |
|   Graph API)     |        |                      | X-Api- |                        |
+------------------+        |  - CalendarService   |  Key   |  GET /api/commute      |
                            |  - PlaceMatcher      |        |  (proxy + edge cache)  |
+------------------+        |  - Admin pages (MVC) |        +------------------------+
|  Azure OpenAI    | <----- |  - EF Core           |
|  (fallback only) |        +----------+-----------+
+------------------+                   |
                                       | managed identity (no passwords)
                          +------------+-------------+
                          v                          v
                +------------------+      +----------------------+
                |  Azure SQL       |      |  Key Vault           |
                |                  |      |  (secrets, token     |
                +------------------+      |   cache key)         |
                                          +----------------------+
```

**Request flow for the commute card:**

1. The board's frontend calls `GET /api/commute` on its own Worker. Cloudflare Access only lets this through for my account, and the Worker checks the Access JWT as well.
2. The Worker calls `GET /api/commute/next` on CommuteApi with `X-Api-Key`, held as a Worker secret. The browser never sees the key or the CommuteApi URL.
3. `CalendarService` fetches the next event inside the lookahead window from Microsoft Graph.
4. `PlaceMatcher` matches the event to a saved place (see [Matching logic](#matching-logic)).
5. The result is recorded in `MatchLogs` and returned as JSON.
6. The frontend adds a temporary primary card for the returned stop, filtered to the returned route. It is kept in memory only and never written to the saved layout in `localStorage`.

Results are cached in CommuteApi (`HybridCache`) and in the Worker (`caches.default`), so the board's 45 second refresh does not hit Graph or the LLM on every poll.

---

## Tech stack

| Layer | Choice |
|---|---|
| Language | C# |
| Framework | ASP.NET Core MVC on .NET 10 (LTS) |
| Data access | Entity Framework Core 10 (code-first migrations) |
| Database | SQL Server 2022 in Docker for local development, Azure SQL in production |
| Calendar | Microsoft Graph SDK for .NET, delegated permissions `Calendars.Read` and `offline_access` |
| Authentication | Microsoft Entra ID via `Microsoft.Identity.Web` (admin pages), Cloudflare Access (board) |
| Caching | `HybridCache` |
| LLM | Azure OpenAI behind an `ILocationResolver` interface |
| Testing | xUnit, `WebApplicationFactory` for HTTP tests, EF Core SQLite in-memory provider for data tests |
| CI/CD | GitHub Actions: restore, build, test on every push and pull request. Deploy on merge to `main` |
| Infrastructure | Bicep |
| Hosting | Azure App Service |
| Observability | Application Insights |

---

## Data model

### `Places`

| Column | Type | Notes |
|---|---|---|
| `Id` | int, PK | |
| `Name` | nvarchar(100), unique | For example "Highpoint" |
| `Aliases` | nvarchar(500), nullable | Comma separated, for example "Telstra, Highpoint Shopping Centre" |
| `StopId` | int | PTV `stop_id`, the identifier ptv-display uses for a card. For merged stations (Flinders Street with Town Hall, Melbourne Central with State Library), use the stop ID ptv-display uses for the merged card |
| `RouteId` | int | PTV route ID, used to filter the card to this route |
| `DirectionId` | int, nullable | PTV direction ID. Stored for future use. ptv-display splits directions by station type, so the card does not use it |
| `RouteType` | int | `0` train or `2` bus. ptv-display shows no other modes |
| `LeadMinutes` | int, default 0 | Travel time allowance, used by the "leave by" feature |
| `CreatedAtUtc` | datetime2 | |
| `UpdatedAtUtc` | datetime2 | |

### `MatchLogs`

| Column | Type | Notes |
|---|---|---|
| `Id` | int, PK | |
| `EventHash` | char(64) | SHA-256 of the event ID. Event titles and locations are never stored |
| `EventStartUtc` | datetime2 | |
| `PlaceId` | int, FK, nullable | Null when no place matched |
| `Method` | nvarchar(20) | `Location`, `Title`, `Llm` or `None` |
| `CreatedAtUtc` | datetime2 | |

**Time handling rule:** all timestamps are stored in UTC. Conversion to `Australia/Melbourne` happens only at the API boundary. This is deliberate: server-timezone assumptions are a common source of bugs in scheduled and calendar logic.

---

## API

All `/api` endpoints require the header `X-Api-Key`. The only intended caller is the ptv-display Worker.

### `GET /api/commute/next`

Returns the place to prioritise for the next calendar event.

**200 OK, match found**

```json
{
  "matched": true,
  "place": "Highpoint",
  "stopId": 12345,
  "routeId": 678,
  "directionId": 1,
  "routeType": 2,
  "eventStart": "2026-11-13T09:00:00+11:00",
  "leaveBy": "2026-11-13T08:20:00+11:00",
  "method": "Title"
}
```

**200 OK, no event or no match**

```json
{ "matched": false, "method": "None" }
```

The response never includes the event title, location text or attendees. The board shows its normal layout when `matched` is `false`.

### `GET /api/places`

Returns all saved places.

### `GET /health`

Returns `200` when the service is running (and, from milestone 1, when the database is reachable). No API key required. **Implemented.**

### Admin pages (MVC, not JSON, Microsoft sign-in required)

| Route | Purpose |
|---|---|
| `/Places` | List places |
| `/Places/Create` | Add a place |
| `/Places/Edit/{id}` | Edit a place |
| `/Places/Delete/{id}` | Delete a place |
| `/Admin/Connect` | Grant calendar access and show whether a token is stored |

---

## Matching logic

The matcher tries cheap, deterministic methods first and only calls the LLM when they fail.

1. **Location field.** Case-insensitive match of the event's location against each place `Name` and `Aliases`.
2. **Title.** Same match against the event title.
3. **LLM fallback.** Send the title and location to the LLM along with the list of saved place names. The LLM must answer with exactly one place name from the list, or `NONE`. Any other answer is treated as `NONE`.
4. **No match.** Return `matched: false`.

Rules:

- The LLM never invents a place. It can only pick from saved places.
- LLM results are cached per event so each event costs at most one call.
- The service works with the LLM disabled. Steps 1 and 2 still run.

---

## Development plan

Each milestone is small enough to finish in one or two sittings and ends with something that runs.

| # | Milestone | Done when | Estimate |
|---|---|---|---|
| 0 | **Scaffold** | Solution, MVC app and test project build locally. `/health` returns 200 with a passing test. Docker Compose file for SQL Server. GitHub Actions builds and tests on push | 1 hour |
| 1 | **Database and Places CRUD** | `Places` and `MatchLogs` tables created by an EF Core migration. Admin pages can create, edit and delete a place, with mode limited to train or bus. `/health` checks the database | 2 days |
| 2 | **Commute endpoint with fake calendar** | `GET /api/commute/next` returns a match using a hardcoded `ICalendarService` stub. API key check works | 1 day |
| 3 | **Entra ID and Microsoft Graph** | Entra app registration done. Admin pages need Microsoft sign-in, limited to my account. `/Admin/Connect` grants calendar access, and the token cache is stored encrypted so it survives restarts. The stub is replaced by `GraphCalendarService` | 2 to 3 days |
| 4 | **Matcher and LLM fallback** | Location and title matching covered by unit tests. Azure OpenAI fallback behind `ILocationResolver`, with caching and `MatchLogs` | 2 days |
| 5 | **ptv-display integration** | The Worker has `GET /api/commute`, behind Cloudflare Access, which proxies to CommuteApi with a secret key and caches the result. The frontend adds a temporary primary card for the match and falls back cleanly when `matched` is `false` | 2 days |
| 6 | **Deploy** | Infrastructure defined in Bicep. Running on Azure App Service with Azure SQL, using managed identity and Key Vault references instead of passwords. GitHub Actions deploys with OIDC. Application Insights connected. `/health` returns 200 | 2 days |
| 7 | **Polish** | README updated with screenshots, ADRs complete, test coverage reviewed | 1 day |

**Total:** about 2 to 3 weeks of part-time work.

### Working agreement for each milestone

- One feature branch and one pull request per milestone.
- Tests are written in the same pull request as the code.
- CI must pass before merging.
- Any significant decision gets a short record in `docs/adr/`.

---

## Getting started

### Prerequisites

- .NET SDK 10.0 (the exact version is pinned in `global.json`)
- Docker Desktop with Rosetta enabled on Apple silicon (for local SQL Server)
- A Microsoft account with an Outlook calendar (from milestone 3)
- A Microsoft Entra app registration (from milestone 3)

The EF Core CLI is a local tool in `dotnet-tools.json`, so it does not need a global install.

### Run locally

```bash
# 1. Clone
git clone https://github.com/hisnameisjoseph/CommuteApi.git
cd CommuteApi
dotnet tool restore

# 2. Start SQL Server (from milestone 1)
cp .env.example .env   # then set a strong MSSQL_SA_PASSWORD
docker compose up -d

# 3. Set secrets (never commit these)
dotnet user-secrets --project src/CommuteApi set "ConnectionStrings:Default" "Server=localhost,1433;Database=CommuteApi;User Id=sa;Password=<your password>;TrustServerCertificate=True"
dotnet user-secrets --project src/CommuteApi set "ApiKey" "<any long random string>"

# 4. Create the database (from milestone 1)
dotnet ef database update --project src/CommuteApi

# 5. Run
dotnet run --project src/CommuteApi
```

Open the URL printed in the terminal, then go to `/Places` to add your first place. `/health` works from milestone 0.

---

## Configuration

| Key | Purpose | Example |
|---|---|---|
| `ConnectionStrings:Default` | Database connection | Stored in user secrets locally. In production it uses managed identity, so it holds no password |
| `AzureAd:ClientId` | Entra app registration client ID | |
| `AzureAd:TenantId` | `consumers` for a personal Microsoft account | `consumers` |
| `AzureAd:ClientSecret` | App registration secret (in production, a Key Vault reference) | |
| `Admin:AllowedEmail` | The only account allowed into the admin pages | |
| `ApiKey` | Shared key expected in `X-Api-Key` | |
| `Commute:LookaheadHours` | How far ahead to look for the next event | `12` |
| `Commute:CacheSeconds` | How long to cache the result | `300` |
| `Commute:TimeZone` | Zone used for API output | `Australia/Melbourne` |
| `Llm:Enabled` | Turn the LLM fallback on or off | `true` |
| `Llm:Endpoint` | Azure OpenAI endpoint | |
| `Llm:Deployment` | Azure OpenAI model deployment name | |

Secrets live in `dotnet user-secrets` locally and in Key Vault in production, read through App Service Key Vault references. `appsettings.json` contains no secrets.

On the ptv-display side, the Worker needs `COMMUTE_API_URL` and `COMMUTE_API_KEY` (via `wrangler secret put`), plus the Cloudflare Access audience tag to check the Access JWT.

---

## Testing

```bash
dotnet test
```

| Area | What is tested |
|---|---|
| Health | `/health` returns 200 without an API key. **Implemented** |
| `PlaceMatcher` | Location match, title match, alias match, case insensitivity, no match |
| LLM fallback | Valid answer accepted, unknown answer treated as `NONE`, disabled flag respected, result cached |
| Time handling | UTC storage, Melbourne output, daylight saving boundaries (first Sunday in October and in April), events crossing midnight |
| `CommuteController` | Missing API key returns 401, no event returns `matched: false`, happy path, no event title or location in the response |
| Admin pages | Anonymous requests are redirected to sign-in, other accounts are refused |
| Data layer | Unique place names enforced, route type limited to train or bus, migrations apply cleanly |

External services (Graph and the LLM) are always replaced by fakes in tests. No test makes a network call. CI collects code coverage on every run.

---

## Deployment

1. Deploy the infrastructure from `infra/main.bicep`: App Service plan and App Service (with a system-assigned managed identity), Azure SQL (check the free offer limits), Key Vault and Application Insights.
2. Give the App Service's managed identity access to Azure SQL (as a contained database user) and to Key Vault secrets.
3. Put secrets in Key Vault and reference them from App Service settings.
4. Set up a federated credential (OIDC) so GitHub Actions can deploy without a stored password.
5. On merge to `main`, GitHub Actions builds, runs the tests, applies migrations with an EF Core migration bundle, and deploys.
6. Confirm `/health` returns 200.
7. In ptv-display, set `COMMUTE_API_URL` and `COMMUTE_API_KEY` as Worker secrets, and put `/api/commute` behind a Cloudflare Access application that allows only my account.

---

## Security and privacy

Calendar data is personal information, so the service is designed to hold and expose as little of it as possible. See [ADR 3](docs/adr/0003-protect-calendar-data-behind-a-worker-proxy-and-sign-in.md).

- **Read-only scope.** Only `Calendars.Read` (plus `offline_access` for the refresh token) is requested.
- **The commute result is only for me.** The board's `/api/commute` route sits behind Cloudflare Access (Microsoft or Google sign-in, my account only), and the Worker also checks the Access JWT. Signed-out visitors get the public board with no commute card.
- **The API key never reaches a browser.** Only the ptv-display Worker calls CommuteApi, server to server, with the key held as a Worker secret. No browser origin is allowed through CORS.
- **Admin pages need Microsoft sign-in**, limited to one account.
- **No event content stored or returned.** Titles, locations and attendees are never written to the database or logs, and never included in API responses. Only a hash of the event ID is kept.
- **Tokens stored encrypted.** The Graph token cache is encrypted with a key held in Key Vault.
- **Minimal LLM payload.** The fallback sends only the event title and location, never attendees, body text or other events.
- **No passwords in production.** Managed identity for Azure SQL and Key Vault, and OIDC for deployments.
- **Secrets out of source control.** User secrets locally, Key Vault in production.
- **HTTPS only.**

---

## Project structure

```
CommuteApi/
├── src/
│   └── CommuteApi/
│       ├── Controllers/        # CommuteController (API), PlacesController, AdminController (MVC)
│       ├── Data/               # DbContext, entity configuration, migrations
│       ├── Models/             # Place, MatchLog, view models, DTOs
│       ├── Services/           # ICalendarService, GraphCalendarService,
│       │                       # PlaceMatcher, ILocationResolver
│       ├── Views/              # Razor views for the admin pages
│       ├── appsettings.json
│       └── Program.cs
├── tests/
│   └── CommuteApi.Tests/
├── infra/                      # Bicep templates (milestone 6)
├── docs/
│   └── adr/                    # Architecture decision records
├── .github/
│   └── workflows/ci.yml
├── CommuteApi.slnx
├── Directory.Build.props       # Shared build settings, warnings as errors
├── global.json                 # Pinned .NET SDK
├── dotnet-tools.json           # Local tools (dotnet-ef)
├── docker-compose.yml          # Local SQL Server
└── README.md
```

---

## Decisions

Each decision is recorded in [`docs/adr/`](docs/adr/).

| Decision | Outcome | Record |
|---|---|---|
| Recording decisions | ADRs in `docs/adr/` | [ADR 1](docs/adr/0001-record-architecture-decisions.md) |
| Local database | SQL Server in Docker, so migrations match Azure SQL | [ADR 2](docs/adr/0002-sql-server-in-docker-for-local-development.md) |
| Protecting calendar data | Worker proxy with a server-side key, Cloudflare Access sign-in on the board, single user | [ADR 3](docs/adr/0003-protect-calendar-data-behind-a-worker-proxy-and-sign-in.md) |
| Licence | MIT | `LICENSE` |

### Still open

| Decision | Options | Needed by |
|---|---|---|
| Graph sign-in flow | Sign in on `/Admin/Connect` with the authorization code flow and keep the refresh token (preferred, reuses the admin sign-in), or device code flow | Milestone 3 |
| Token cache storage | Encrypted rows in Azure SQL (Data Protection keys in Key Vault), or Key Vault secrets directly | Milestone 3 |
| LLM provider | Azure OpenAI (preferred, fits the Azure focus) or any provider with a simple chat API. Kept behind `ILocationResolver` so it can be swapped | Milestone 4 |
| App Service tier | Free F1 (60 CPU minutes a day, no Always On) or Basic B1. Check against a 45 second poll with caching | Milestone 6 |

---

## Roadmap

- [x] Milestone 0: Scaffold
- [ ] Milestone 1: Database and Places CRUD
- [ ] Milestone 2: Commute endpoint with fake calendar
- [ ] Milestone 3: Entra ID and Microsoft Graph
- [ ] Milestone 4: Matcher and LLM fallback
- [ ] Milestone 5: ptv-display integration
- [ ] Milestone 6: Deploy
- [ ] Milestone 7: Polish
- [ ] Later: CSV import of places
- [ ] Later: "Leave by" time
- [ ] Later: Google Calendar provider
- [ ] Later: Multi-user, so each signed-in viewer sees their own calendar

---

## Licence

MIT. See [`LICENSE`](LICENSE).

---

## Author

Joseph Ho. Related project: [ptv-display](https://github.com/hisnameisjoseph/ptv-display).
