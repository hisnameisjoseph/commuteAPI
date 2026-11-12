# CommuteApi

A small C# service that reads your next calendar event, works out where you are going, and tells the [ptv-display](https://github.com/hisnameisjoseph/ptv-display) departures board which stop and route to pin to the top of the screen.

**Status:** Planned. This README is the build specification. Nothing below is implemented yet.

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
16. [Open decisions](#open-decisions)
17. [Roadmap](#roadmap)
18. [Licence](#licence)

---

## Why this exists

### The problem

`ptv-display` is a wall-mounted Melbourne public transport departures board. It shows the same stops all day, regardless of where I actually need to go next. Calendar apps know where I need to be, but they only give directions when I open them and ask.

### The solution

CommuteApi connects the two. It reads the next event in my Outlook calendar (for example, a 9am shift at Highpoint), matches it to a saved place, and returns the stop and route that the display should prioritise. The board shows the right departures before I ask. Zero interaction.

### Why it is built this way

This project is also a deliberate learning exercise in the Microsoft stack:

| Skill | Where it appears in this project |
|---|---|
| C# and ASP.NET Core MVC | The whole service, including the admin pages |
| Entity Framework Core | All database access and migrations |
| SQL Server / Azure SQL | Relational storage for places and match history |
| Microsoft 365 integration | Outlook calendar via the Microsoft Graph API |
| System integration | An existing TypeScript app (ptv-display) consuming a new C# service |
| Azure | App Service hosting and Azure SQL |

---

## Features

### Must have (MVP)

1. **Saved places.** Create, edit and delete places through a small MVC admin page. Each place maps a name to a PTV stop, route and direction.
2. **Calendar read.** Fetch the next upcoming event from Outlook calendar through Microsoft Graph.
3. **Event to place matching.** Match the event to a saved place using the location field first, then the title, with an LLM fallback for vague titles.
4. **Commute endpoint.** One JSON endpoint that ptv-display calls to learn which stop and route to pin.
5. **ptv-display integration.** The display polls the endpoint and moves the matching route to the top.

### Nice to have (after MVP)

- CSV import of places (extract, validate, load).
- "Leave by" time based on the event start and a per-place lead time.
- Google Calendar as a second calendar provider.
- Match history page showing which method matched each event.

---

## Non-goals

- **No journey planning.** The service does not calculate routes between two points. Places are mapped to stops by hand. The PTV Timetable API v3 is not used for routing here.
- **No multi-user support.** Single user, single calendar account.
- **No writing to the calendar.** Read-only access.
- **No departure times.** ptv-display already fetches live departures. CommuteApi only says what to prioritise.

---

## Architecture

```
+------------------+        +----------------------+        +------------------+
|  Outlook         |        |  CommuteApi          |        |  ptv-display     |
|  Calendar        | <----- |  (ASP.NET Core MVC)  | <----- |  (Cloudflare     |
|  (Microsoft      |  Graph |                      |  HTTPS |   Worker + Pages)|
|   Graph API)     |        |  - CalendarService   |  JSON  |                  |
+------------------+        |  - PlaceMatcher      |        +------------------+
                            |  - Admin pages (MVC) |
+------------------+        |                      |
|  LLM provider    | <----- |  EF Core             |
|  (fallback only) |        +----------+-----------+
+------------------+                   |
                                       v
                            +----------------------+
                            |  SQL Server /        |
                            |  Azure SQL           |
                            +----------------------+
```

**Request flow for `GET /api/commute/next`:**

1. ptv-display calls the endpoint with an API key.
2. `CalendarService` fetches the next event inside the lookahead window from Microsoft Graph.
3. `PlaceMatcher` matches the event to a saved place (see [Matching logic](#matching-logic)).
4. The result is recorded in `MatchLogs` and returned as JSON.
5. ptv-display pins the returned stop and route to the top of the board.

Results are cached for a short period so the display's 45 second refresh does not hit Graph or the LLM on every poll.

---

## Tech stack

| Layer | Choice |
|---|---|
| Language | C# |
| Framework | ASP.NET Core MVC on the current .NET LTS release |
| Data access | Entity Framework Core (code-first migrations) |
| Database | SQL Server in Docker for local development, Azure SQL in production |
| Calendar | Microsoft Graph SDK for .NET, delegated permission `Calendars.Read` |
| LLM | Provider behind an `ILocationResolver` interface (see [Open decisions](#open-decisions)) |
| Testing | xUnit, with EF Core SQLite in-memory provider for data tests |
| CI | GitHub Actions: restore, build, test on every push and pull request |
| Hosting | Azure App Service |

---

## Data model

### `Places`

| Column | Type | Notes |
|---|---|---|
| `Id` | int, PK | |
| `Name` | nvarchar(100), unique | For example "Highpoint" |
| `Aliases` | nvarchar(500), nullable | Comma separated, for example "Telstra, Highpoint Shopping Centre" |
| `StopId` | int | PTV `stop_id`, the canonical identifier used by ptv-display |
| `RouteId` | int | PTV route ID |
| `DirectionId` | int | PTV direction ID |
| `RouteType` | int | PTV route type (train, tram, bus) |
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

All `/api` endpoints require the header `X-Api-Key`.

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

The display falls back to its default layout when `matched` is `false`.

### `GET /api/places`

Returns all saved places.

### `GET /health`

Returns `200` when the service and database are reachable. No API key required.

### Admin pages (MVC, not JSON)

| Route | Purpose |
|---|---|
| `/Places` | List places |
| `/Places/Create` | Add a place |
| `/Places/Edit/{id}` | Edit a place |
| `/Places/Delete/{id}` | Delete a place |

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
| 0 | **Scaffold** | `dotnet new mvc -o CommuteApi` runs locally, repository created, GitHub Actions builds on push | 1 hour |
| 1 | **Database and Places CRUD** | `Places` table created by an EF Core migration. Admin pages can create, edit and delete a place | 2 days |
| 2 | **Commute endpoint with fake calendar** | `GET /api/commute/next` returns a match using a hardcoded `ICalendarService` stub. API key check works | 1 day |
| 3 | **Microsoft Graph integration** | Azure app registration done. The stub is replaced by a real `GraphCalendarService` reading the next Outlook event | 2 days |
| 4 | **Matcher and LLM fallback** | Location and title matching covered by unit tests. LLM fallback behind `ILocationResolver`, with caching and `MatchLogs` | 2 days |
| 5 | **ptv-display integration** | The display calls the endpoint and pins the returned route to the top. Falls back cleanly when `matched` is `false` | 2 days |
| 6 | **Deploy** | Running on Azure App Service with Azure SQL. Secrets in App Service configuration. `/health` returns 200 | 1 day |
| 7 | **Polish** | README updated with screenshots, architecture decision records written, test coverage reviewed | 1 day |

**Total:** about 2 weeks of part-time work.

### Working agreement for each milestone

- One feature branch and one pull request per milestone.
- Tests are written in the same pull request as the code.
- CI must pass before merging.
- Any significant decision gets a short record in `docs/adr/`.

---

## Getting started

### Prerequisites

- .NET SDK (current LTS)
- Docker Desktop (for local SQL Server)
- EF Core CLI: `dotnet tool install --global dotnet-ef`
- A Microsoft account with an Outlook calendar
- An Azure app registration with the delegated permission `Calendars.Read`

### Run locally

```bash
# 1. Clone
git clone https://github.com/hisnameisjoseph/CommuteApi.git
cd CommuteApi

# 2. Start SQL Server
docker compose up -d

# 3. Set secrets (never commit these)
cd src/CommuteApi
dotnet user-secrets init
dotnet user-secrets set "ConnectionStrings:Default" "<connection string>"
dotnet user-secrets set "Graph:ClientId" "<client id>"
dotnet user-secrets set "Graph:TenantId" "<tenant id>"
dotnet user-secrets set "ApiKey" "<any long random string>"

# 4. Create the database
dotnet ef database update

# 5. Run
dotnet run
```

Open the URL printed in the terminal, then go to `/Places` to add your first place.

---

## Configuration

| Key | Purpose | Example |
|---|---|---|
| `ConnectionStrings:Default` | Database connection | Stored in user secrets or App Service |
| `Graph:ClientId` | Azure app registration client ID | |
| `Graph:TenantId` | Tenant ID, or `consumers` for a personal account | |
| `ApiKey` | Shared key expected in `X-Api-Key` | |
| `Commute:LookaheadHours` | How far ahead to look for the next event | `12` |
| `Commute:CacheSeconds` | How long to cache the result | `300` |
| `Commute:TimeZone` | Zone used for API output | `Australia/Melbourne` |
| `Llm:Enabled` | Turn the LLM fallback on or off | `true` |
| `Llm:ApiKey` | LLM provider key | |
| `Cors:AllowedOrigins` | Origins allowed to call the API | The ptv-display Pages URL |

Secrets live in `dotnet user-secrets` locally and in App Service configuration in production. `appsettings.json` contains no secrets.

---

## Testing

```bash
dotnet test
```

| Area | What is tested |
|---|---|
| `PlaceMatcher` | Location match, title match, alias match, case insensitivity, no match |
| LLM fallback | Valid answer accepted, unknown answer treated as `NONE`, disabled flag respected, result cached |
| Time handling | UTC storage, Melbourne output, daylight saving boundaries, events crossing midnight |
| `CommuteController` | Missing API key returns 401, no event returns `matched: false`, happy path |
| Data layer | Unique place names enforced, migrations apply cleanly |

External services (Graph and the LLM) are always replaced by fakes in tests. No test makes a network call.

---

## Deployment

1. Create an Azure SQL database and an App Service (check free tier limits first).
2. Add all keys from [Configuration](#configuration) to the App Service settings.
3. Apply migrations: `dotnet ef database update` against the production connection string.
4. Deploy from GitHub Actions on merge to `main`.
5. Confirm `/health` returns 200.
6. Set the API URL and key in the ptv-display Worker configuration.

---

## Security and privacy

Calendar data is personal information, so the service is designed to hold as little of it as possible.

- **Read-only scope.** Only `Calendars.Read` is requested.
- **No event content stored.** Titles, locations and attendees are never written to the database or logs. Only a hash of the event ID is kept.
- **Minimal LLM payload.** The fallback sends only the event title and location, never attendees, body text or other events.
- **API key on every `/api` call.** CORS is restricted to the ptv-display origin.
- **Secrets out of source control.** User secrets locally, App Service configuration in production.
- **HTTPS only.**

---

## Project structure

```
CommuteApi/
├── src/
│   └── CommuteApi/
│       ├── Controllers/        # CommuteController (API), PlacesController (MVC)
│       ├── Data/               # DbContext, entity configuration, migrations
│       ├── Models/             # Place, MatchLog, view models, DTOs
│       ├── Services/           # ICalendarService, GraphCalendarService,
│       │                       # PlaceMatcher, ILocationResolver
│       ├── Views/              # Razor views for the admin pages
│       ├── appsettings.json
│       └── Program.cs
├── tests/
│   └── CommuteApi.Tests/
├── docs/
│   └── adr/                    # Architecture decision records
├── .github/
│   └── workflows/ci.yml
├── docker-compose.yml          # Local SQL Server
└── README.md
```

---

## Open decisions

These need an answer before or during the relevant milestone. Record each one as an ADR.

| Decision | Options | Needed by |
|---|---|---|
| Local database | SQL Server in Docker (closest to production) or SQLite (lighter on a Mac) | Milestone 1 |
| LLM provider | Any provider with a simple chat API. Kept behind `ILocationResolver` so it can be swapped | Milestone 4 |
| Graph sign-in flow | Device code flow or interactive browser sign-in, with a cached refresh token | Milestone 3 |
| Licence | MIT or none | Milestone 7 |

---

## Roadmap

- [ ] Milestone 0: Scaffold
- [ ] Milestone 1: Database and Places CRUD
- [ ] Milestone 2: Commute endpoint with fake calendar
- [ ] Milestone 3: Microsoft Graph integration
- [ ] Milestone 4: Matcher and LLM fallback
- [ ] Milestone 5: ptv-display integration
- [ ] Milestone 6: Deploy
- [ ] Milestone 7: Polish
- [ ] Later: CSV import of places
- [ ] Later: "Leave by" time
- [ ] Later: Google Calendar provider

---

## Licence

To be decided. See [Open decisions](#open-decisions).

---

## Author

Joseph Ho. Related project: [ptv-display](https://github.com/hisnameisjoseph/ptv-display).