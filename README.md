# TaroKing

Remake of [valat.si](https://valat.si) — Slovenian Tarok (4 players, full rules), built with **Blazor Server on .NET 10**.

## Projects

| Project | What it is |
|---|---|
| `src/TaroKing.Engine` | Pure C# rules engine — cards, dealing, bidding, talon, trick play, announcements, scoring. No I/O, no ASP.NET, deterministic (seeded RNG). |
| `src/TaroKing.Bots` | AI players implementing `IPlayerAgent`. |
| `src/TaroKing.Data` | EF Core + SQLite persistence: users, matches, hands, ratings, chat. |
| `src/TaroKing.App` | Blazor Server web app — lobby, tables, UI. Single source of truth for game state. |
| `tests/TaroKing.Engine.Tests` | xUnit tests for the engine, the bots, the data layer and the table services. |
| `tools/TaroKing.LoadTest` | Console load test: N bot tables plus M simulated browsers, prints heartbeat p50/p99. |

## Requirements

- .NET SDK 10.0
- Any modern browser

## Run

```cmd
dotnet restore
dotnet build
dotnet test
dotnet run --project src\TaroKing.App
```

Then open the URL printed in the console (default `https://localhost:7101`).

The schema is created and upgraded by EF migrations on startup. After changing the model:

```cmd
dotnet ef migrations add <Name> --project src\TaroKing.Data --startup-project src\TaroKing.App
```

## Configuration

`src/TaroKing.App/appsettings.json`, or environment variables with `__` as the separator (`Smtp__Host`).

| Key | Meaning | Default |
|---|---|---|
| `ConnectionStrings:TaroKing` | SQLite connection string | `Data Source=taroking.db` |
| `Admins` | Array of user names that get the Admin role on startup | `[]` |
| `Smtp:Host`, `Port`, `User`, `Password`, `From`, `UseSsl` | Outgoing mail for e-mail confirmation and password reset. No host: links go to the log. | not set |
| `Metrics:Token` | Enables `/metrics` (Prometheus text) for `Authorization: Bearer <token>` or `?token=`. No token: 404. | not set |
| `Backup:Enabled`, `Backup:Hour`, `Backup:Directory` | Nightly `VACUUM INTO` copy, kept 14 days | `true`, `3`, `App_Data/backups` |
| `DataProtection:KeysPath` | Where the cookie/antiforgery key ring lives; keep it on the same volume as the database | `App_Data/keys` |
| `Tables:Max` | Open online tables the server will hold | `200` |

`App_Data/badwords.txt` is the chat word list (one word per line, `#` comments); edit it without a build.

## Operations

**Live tables survive a restart.** Every online table journals its options, chairs, chat and every event of every
hand into `LiveTables` / `LiveEvents` as it goes. On startup the tables are rebuilt from the journal, clocks reset to
the full reserve, and everybody is marked away — a seat whose owner does not reconnect within 60 s goes to a bot as
usual. On `ApplicationStopping` the tables announce the restart and deal no new hands; the host waits up to 20 s so
the journal can flush. Deploy = stop, replace, start.

**Backups.** A copy `App_Data/backups/taroking-yyyyMMdd-HHmm.db` is written every night at `Backup:Hour` (local
time) and copies older than 14 days are deleted. To restore:

1. Stop the app.
2. Replace `taroking.db` with the backup (delete `taroking.db-wal` and `taroking.db-shm` if present).
3. Start the app — migrations bring an older backup up to date.

**Metrics.** With `Metrics:Token` set, `GET /metrics` returns open tables, tables playing, humans online, heartbeat
p50/p99/max (ms), archive successes and failures, hands dealt and tables opened since start. `GET /zdravje` is a
plain liveness probe.

**Load.** `dotnet run -c Release --project tools\TaroKing.LoadTest -- 200 400 60` runs 200 tables with 400 readers
for a minute and prints the heartbeat percentiles. Write the largest table count with p99 < 50 ms on the production
box here.

_Measured 20. 9. 2026 on the dev machine (Release): 200 tables / 400 readers — tick p50 0.85 ms, p99 12.4 ms, max 26.3 ms. Comfortably under budget; the production ceiling is to be measured once there is a production box._

**Moderation.** Users named in `Admins` get the Admin role. `/admin/prijave` is the report queue (dismiss, warn,
mute, ban), `/admin/dnevnik` the audit log. A ban changes the account's security stamp, so an open session is signed
out within five minutes.

## Design rules

- **The server is the authority.** The browser never receives another player's cards; every move is re-validated server-side.
- **The engine is pure.** It takes a state plus a move and returns a new state — no timers, no database, no HTTP.
- **A game is an event log.** Any game can be replayed from its events, which is what makes saving, reconnecting and analysis possible.

The phased plan lives in [Tasks.md](Tasks.md).
