# TaroKing

Remake of [valat.si](https://valat.si) — Slovenian Tarok (4 players, full rules), built with **Blazor Server on .NET 10**.

## Projects

| Project | What it is |
|---|---|
| `src/TaroKing.Engine` | Pure C# rules engine — cards, dealing, bidding, talon, trick play, announcements, scoring. No I/O, no ASP.NET, deterministic (seeded RNG). |
| `src/TaroKing.Bots` | AI players implementing `IPlayerAgent`. |
| `src/TaroKing.Data` | EF Core + SQLite persistence: users, matches, hands, ratings, chat. |
| `src/TaroKing.App` | Blazor Server web app — lobby, tables, UI. Single source of truth for game state. |
| `tests/TaroKing.Engine.Tests` | xUnit tests for the engine. |

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

## Design rules

- **The server is the authority.** The browser never receives another player's cards; every move is re-validated server-side.
- **The engine is pure.** It takes a state plus a move and returns a new state — no timers, no database, no HTTP.
- **A game is an event log.** Any game can be replayed from its events, which is what makes saving, reconnecting and analysis possible.

The phased plan lives in [Tasks.md](Tasks.md).
