# TaroKing — valat.si remake

Slovenian tarok (4 players, full rules) — Blazor Server (.NET 10), local play against bots **and** online tables in v1.

- **Solution**: `TaroKing.sln`
- **Engine**: pure C# class library (no I/O), deterministic, seeded RNG → same seed means the same hand
- **The server is the authority**: the client never holds another player's cards; every rule is re-checked server-side
- **Commit rule**: one phase = one commit (`Phase N - Name`). Details stay here, not in the commit message.

---

## Rules the engine must cover (reference)

**Pack**: 54 cards — 22 trumps (Škis > XXI Mond > XX…II > I Pagat) + 4 suits of 8.
Black suits (clubs, spades): K, Q, Knight, Jack, 10, 9, 8, 7 — red suits (hearts, diamonds): K, Q, Knight, Jack, 1, 2, 3, 4.

**Card values**: king / Škis / Mond / Pagat = 5, queen = 4, knight = 3, jack = 2, everything else = 1.
Counted in batches of three: add the values, subtract 2 per batch. Pack total **70**, **36+** wins.

**Bidding ladder** (lowest first):

| Contract | Value | Talon | Calls a king | Goal |
|---|---|---|---|---|
| Klop | ±70 | 6 cards, turned up trick by trick | — | 0 tricks |
| Three | 10 + difference | 3 | yes | 36+ |
| Two | 20 + difference | 2 | yes | 36+ |
| One | 30 + difference | 1 | yes | 36+ |
| Solo three | 40 + difference | 3 | no | 36+ |
| Solo two | 50 + difference | 2 | no | 36+ |
| Solo one | 60 + difference | 1 | no | 36+ |
| Beggar | 70 | — | no | 0 tricks |
| Solo without | 80 | — | no | 36+ |
| Open beggar | 90 | — | no | 0 tricks, hand exposed after trick 1 |
| Colour valat | 125 | — | no | all tricks, trumps act as a plain suit |
| Valat | 500 | — | no | all tricks |

Bidding priority: forehand > 2nd > 3rd > dealer; a senior player may *match* a bid, a junior one must *raise* or pass.
If everyone but forehand passes, forehand may only play klop or three.

**Difference** = (card points won − 35), rounded to the nearest 5.

**Announcements** (silent / announced): trula 10/20, kings 10/20, king ultimo 10/20, pagat ultimo 25/50, valat 250/500.
Kontra ladder: kontra → rekontra → subkontra → mordkontra (×2 each step, up to ×16). Announcements are kontra'd separately.

**Penalties and special cases**: captured Mond (Škis and Mond in the same trick) = −20, personal; emperor trick (Škis + Mond + Pagat) is taken by the Pagat; in negative contracts you must beat the highest card on the table and may not play the Pagat except when forced; no 5-point card (kings, Škis, Mond, Pagat) may be discarded to the talon.

**Radlci**: written for klop, for beggar and above, and for any valat. Winning with an uncancelled radlc = ×2 and the radlc is crossed off; losing = ×2 and the radlc stays. At the end of a session, −100 for each uncancelled radlc.

---

## Phase 0 — Scaffold and conventions

**Goal**: the solution builds, tests run, the Blazor app starts on an empty page.

- [x] `TaroKing.sln` with `TaroKing.Engine`, `TaroKing.Bots`, `TaroKing.Data`, `TaroKing.App`, `TaroKing.Engine.Tests`
- [x] `Directory.Build.props` — `net10.0`, nullable, `Company = DaTaLabs`
- [x] `.editorconfig` — tabs, 1TBS, braces always required
- [x] `.gitignore`, `README.md`, `Tasks.md`
- [x] Blazor Server app (InteractiveServer) with a basic layout
- [x] `dotnet build` clean, `dotnet test` green (3 tests)
- [x] No NU1903 advisories — EF Core pinned to 10.0.12, `System.Security.Cryptography.Xml` pinned explicitly
- [x] `git init` + first commit

**Acceptance**: `dotnet run --project src\TaroKing.App` opens the page; `dotnet test` reports 3 passed; `dotnet restore` prints no warnings.

---

## Phase 1 — Cards, pack, deal

**Goal**: a complete, tested representation of the pack.

- [x] `Suit` (Clubs, Spades, Hearts, Diamonds, Trump), `SuitRank`, `Card` (readonly record struct)
- [x] Card point values + ordering within a suit (`IComparable<Card>`, display order only)
- [x] `Pcg32` — own deterministic PRNG, so a seed means the same hand on any machine or runtime version
- [x] `Deck.Full()` — exactly 54 cards, no duplicates; `Deck.Shuffled(seed)`
- [x] `Deal.Create(seed)` — 6 to the talon, then packets of 6 per seat, twice round
- [x] `Deal.FromOrderedPack(pack)` — deal from a known pack order, for tests and replays
- [x] "No trump in hand" rule → `SeatsWithoutTrump` / `RequiresRedeal`
- [x] `CardScoring.Count(cards)` — batches of three plus remainder, order-independent

**Acceptance**: unit tests — all cards sum to 70; 1000 random deals always produce 54 distinct cards; batch counting matches hand-calculated examples; the same seed produces the same deal.

---

## Phase 2 — Bidding

**Goal**: the full auction, including the seniority rule.

- [x] `Contract` enum (bidding rank) + `ContractInfo` table: value, talon size, calls a king, solo, negative, all-tricks, counts card points, allows bonuses, who leads, forehand-only
- [x] `BiddingState` — whose turn, who has passed, history, winning bid
- [x] Seniority rule (senior matches, junior must raise); priority runs forehand → dealer
- [x] Forehand stays silent, the auction opens at seat 1, and the lowest open bid is **dva**
- [x] Forehand privilege: if the other three pass, forehand names any contract — the only way klop or tri gets played, and forehand may not pass
- [x] Auction ends once everyone but one bidder has passed
- [x] `BiddingState.CompulsoryKlop()` for the no-trump redeal
- [x] `LegalBids()` / `CanPass()` for the UI and the bots

**Acceptance**: tests cover — everyone passes; forehand matching a bid; a junior player may not match; escalation up to valat; the auction always ends with a valid declarer.

---

## Phase 3 — Talon, calling a king, discarding

**Goal**: correct talon exchange for every contract.

- [x] `KingCall.Resolve` — calling a king (your own → effectively solo); the partner is known to the engine but not published until that king falls
- [x] Called king in the talon → declarer plays alone
- [x] `TalonPhase` packet choice: 2×3 (tri), 3×2 (dva), 6×1 (ena) and the same for the solos
- [x] Lay-away: five-pointers refused, count must match the packet, no duplicates, only cards actually held
- [x] Laid-away trumps exposed through `ShownDiscards` (the count is public knowledge)
- [x] Unchosen packets go to `OpponentTalon` and count for the opponents
- [x] Solo brez / berač / valat — the talon goes to the opponents unseen
- [x] Klop: the 6 talon cards kept as `KlopGifts` for the first six tricks
- [ ] Collecting the rest of the talon by winning a trick with the called king (needs trick play — folded into Phase 4)

**Acceptance**: tests — impossible to discard a king/Škis/Mond/Pagat; hand size after the exchange is always 12; klop gifts go to the correct trick winner.

---

## Phase 4 — Trick play

**Goal**: server-authoritative move legality.

- [x] `TrickRules` — pure functions: `LegalPlays`, `Winner`, `WinningCard`, `IsEmperorTrick`, `CapturedMondSeat`
- [x] `TrickPlay` — the twelve tricks, owning every hand; `Play(seat, card)` refuses out-of-turn, not-held and illegal cards
- [x] Must follow suit; if you cannot, you must trump; otherwise anything
- [x] Must beat the table in negative contracts (klop, berač, odprti berač), trumps included
- [x] Pagat held back in negative contracts unless nothing else is legal
- [x] Emperor trick (škis + mond + pagat → the pagat takes it)
- [x] Colour valat: trumps are an ordinary suit and only win when led
- [x] Captured mond detection (positive contracts only, even when the pagat steals the trick)
- [x] Klop gifts handed to the winner of each of the first six tricks
- [ ] Collecting the rest of the talon by winning a trick with the called king — **needs your confirmation that this is played at all** (see notes)

**Acceptance**: a test per rule, plus a fuzz test — 10 000 random hands played out with random *legal* moves finish without an exception, every player plays 12 cards, 48 cards end in tricks and 6 in the talon.

---

## Phase 5 — Announcements and kontras

**Goal**: the announcement round after the talon exchange.

- [x] `Bonus` + `Bonuses` table (trula, kralji, kralj ultimo, pagat ultimo, valat) with silent and announced values
- [x] `AnnouncementRound` — starts with the declarer, several announcements allowed per turn, ends when the table goes all the way round without an action
- [x] Pagat ultimo only from the pagat holder, kralj ultimo only from the called king's holder
- [x] One announcement of a bonus per side; the other side may still claim the same bonus
- [x] Kontra / rekontra / subkontra / mordkontra, alternating sides, ×2 each step up to ×16
- [x] A kontra names the game or one specific announcement; you cannot kontra your own side
- [x] Klop cannot be kontra'd unless `allowKlopKontra` is switched on (house rule)
- [ ] Silent variants are resolved after play — Phase 6, where the scoring lives

**Acceptance**: a test per multiplier; an announced valat cancels every other bonus; a silent trula is credited without any announcement.

---

## Phase 6 — Scoring

**Goal**: a score sheet that matches the valat.si one.

- [x] `HandScorer` + `HandScore` — itemised `ScoreLine`s, always written from the declaring side's point of view
- [x] Difference (`RoundToFive`, halves away from zero), game value, silent and announced bonuses, kontra multipliers
- [x] `ContractInfo.ScoresDifference` added, so solo brez stays flat at 80 while tri/dva/ena and the solos score the margin
- [x] Bonus resolution from the piles and tricks: trula, kralji, pagat ultimo, kralj ultimo, valat — and a valat sweeps the rest off the sheet
- [x] Mond penalty (−20, personal, on top of the side's result)
- [x] Klop: +70 / −70 / −rounded points, every player for themselves
- [x] `ScoreSheet` — running totals, radlci written for klop / berač-and-above / any valat, cancelled on a declared win, −100 each at the end
- [ ] **Open**: does a declarer's radlc double the whole hand, or only the declaring side's half? Currently the whole hand (symmetric)

**Acceptance**: hand-calculated scenarios match to the point. Note the score is *per player*, not zero-sum: everyone on the winning side writes the amount and everyone on the losing side writes it as a minus, so a solo costs each of the three opponents the full value.

---

## Phase 7 — Negative contracts and valats

**Goal**: beggar, open beggar, colour valat and valat in full.

- [x] `PlayContext` — the engine now knows which seats are the declaring side during play
- [x] `HandEnding` — a hand finishes by playing all twelve tricks, by a berač taking one, or by a valat dropping one; the rest of the cards are never played
- [x] Berač — declarer leads, 0 tricks, no bonuses; decided the moment it takes a trick
- [x] Odprti berač — `IsHandExposed` turns the declarer's hand face up from the second trick
- [x] Barvni valat — 125, trumps as a plain suit (Phase 4), decided the moment a defender takes a trick
- [x] Valat — 500, same early finish; a partner taking a trick does not break it
- [x] Klop stays negative but never ends early
- [x] `Contracts.CanUpgradeToColourValat` — solo tri/dva/ena may be lifted after the talon exchange (wiring it into the session is Phase 8)

**Acceptance**: tests — a beggar loses on the first trick taken; the open beggar's hand is revealed at the right moment; a valat aborts correctly.

---

## Phase 8 — Game state, serialization, replay

**Goal**: a game is data you can store, send and replay.

- [x] `HandState` with `GamePhase` (Bidding → KingCall → Talon → Announcing → Play → Finished), driving phases 1-7
- [x] Append-only `GameEvent` log + `HandState.Replay(events)` → the identical hand, including part-played ones
- [x] The log is the state: nothing derived is stored, so a saved hand is a seed plus a list of actions
- [x] Explicit barvni valat decision after a solo's talon exchange (`UpgradeToColourValat` / `KeepContract`)
- [x] `PlayerView.For(hand, seat)` — your cards, everyone else's counts, and only what is public
- [x] The partner is hidden until the called king falls; the talon packets the declarer left are shown to nobody
- [x] The odprti berač's hand reaches every view once it is face up
- [ ] JSON serialization (source-generated) — deferred to Phase 12, where the wire format is actually needed; the event records are plain enough to serialise as they stand

**Acceptance**: replaying 1000 random games returns identical final states; `PlayerView` never contains another player's cards (asserted over the whole JSON).

---

## Phase 9 — Bots v1

**Goal**: a bot that plays decently and fast.

- [x] `IPlayerAgent` — async, and it only ever sees a `PlayerView`, so a bot cannot look at cards a person could not
- [x] `RandomBot` — the baseline to beat and the fuzz partner
- [x] `HeuristicBot` — hand strength (trumps, the three five-pointers, kings), bidding ladder, beggar shape detection
- [x] Lay-away puts points in the declarer's own pile and keeps trumps; king called in your longest suit
- [x] Play: draw trumps when strong, feed the partner, win cheaply when the trick is worth it, duck otherwise
- [x] Negative contracts: shed expensive cards on tricks you are not taking
- [x] Three profiles: cautious / normal / aggressive (they shift the bidding bar and the pagat ultimo bar)
- [x] `SlowAgent` wrapper for a configurable thinking delay
- [x] `BotTable` — drives a `HandState` with four agents, applies the radlc multiplier from the sheet, records the result
- [x] `HandState.ApplyRadlcMultiplier` + `RadlcApplied` event, so a doubled hand replays as a doubled hand

**Acceptance**: the thinking bots outscore the random ones over 60 hands with the seating swapped half-way, so forehand's advantage cannot decide it; every hand any table plays reaches a score, and replays from its log.

---

## Phase 10 — Blazor table UI

**Goal**: the game is clickable.

- [x] `Table.razor` at `/miza` — 4 seats, table, current trick in the middle
- [x] `CardView.razor` (CSS), fanned hand, hover, selection
- [x] `ActionPanel.razor` — bidding, king call, talon, lay-away, upgrade, announcements and kontra
- [x] `ScoreSheetView.razor` — running sheet with radlci and the final totals
- [x] Legal moves highlighted (illegal ones dimmed *and* rejected server-side)
- [x] Responsive layout (desktop + phone)
- [x] `LocalGame` — scoped service holding the hand, three bots and the sheet; every click is an engine call

**Acceptance**: a full hand against bots is played without opening the console; an illegal move sent from the client is rejected by the server.

---

## Phase 11 — Local game against bots (end to end)

- [x] "Quick game" — 1 human + 3 bots, instant start (`/miza` deals one straight away)
- [x] Setup page `/nova-igra` — number of hands / play to X points / endless, table style, bot tempo
- [x] End screen with standings, per-seat statistics and which contracts came up
- [x] Saving and resuming an interrupted session — games live in `LocalGames`, keyed by the id in the URL
- [x] `LocalGame.PlayOutAsync` — a bot takes the human's seat so a whole session can run headless
- [ ] Rotate the deal so forehand moves around the table (needs an engine change: `ForehandSeat` is currently hard-coded to 0, and the score sheet indexes by engine seat)

**Acceptance**: 10 consecutive hands without an error; refreshing the page mid-hand restores the state.

---

## Phase 12 — Online tables

**Goal**: real multiplayer on Blazor Server circuits.

- [x] `TableService` (singleton) — table list, seats, state, one authoritative `OnlineTable` per table
- [x] `TableHeartbeat` (hosted service) — the table's own 250 ms clock; nothing waits on a browser
- [x] Lobby `/lobi`: create a table (public/private, timer, bots on empty seats), join, leave
- [x] Table options in valat.si's shape: rounds (7-30), seconds per move (1.5-4.5), minimum rating, members-only
- [x] Seed every hand from `RandomNumberGenerator` and store it — deterministic for replay, unguessable in play
- [x] State per seat: `PlayerView.For` per player, `PlayerView.ForSpectator` for watchers
- [x] Reconnect: a refresh or dropped connection does not kill the hand (60 s grace, seat held)
- [x] Move timer + auto-move on timeout (Fischer clock: increment per move, then the reserve)
- [x] A bot takes over the seat of a player who drops, and hands it back on return
- [x] Table chat + spectators (a watcher's view carries no cards and no legal moves at all)
- [x] `Board.razor` — one felt shared by the local and online tables; `ITableActions` so one action panel drives both
- [ ] Three-handed tarok (the lobby offers 3/4 seats on valat.si; the engine is strictly four-player)
- [ ] `MembersOnly` is stored but inert, and every player is rated 1000, until accounts land in Phase 13

**Acceptance**: 4 browsers play a full hand; killing one tab mid-hand hands the seat to a bot and gives it back on return; no client ever receives another player's cards (verified in the network log).

**Note on "sekunde na potezo"**: implemented as a Fischer increment — every move gives the seat that many seconds back, and longer thinking eats a reserve bank (default 60 s). A hard 1.5-second guillotine would be unplayable, so this is the only reading of the valat.si range that makes sense. Worth confirming at a real table.

---

## Phase 13 — Accounts, persistence, rating

- [x] EF Core + SQLite: `Users` (Identity), `Matches`, `MatchSeats`, `Hands`, `Events`, `Ratings`, `ChatMessages`, `Reports`
- [x] `EventCodec` — the event log written down explicitly (the JSON serialisation deferred from Phase 8); every kind round-trips
- [x] Login/registration (ASP.NET Core Identity: `AddIdentityCore` + cookies, plain form posts to `/account/*`) + guest play without an account
- [x] Store played games (event log) + history viewer `/zgodovina`, match page `/partija/{id}`
- [x] Rating (Elo-like, per match) + leaderboard `/lestvica` — weighted by match length, the ratio of final scores, and the opponents' ratings; needs two members at an online table
- [x] Player flags on the table page: member, frequent leaver (a bot finished ≥ a quarter of their matches), number of blacklist reports; "prijavi" button for members
- [x] Last 30 games in history, each hand replayable against the bots — same seed, same chair (`SessionOptions.HumanSeat`)
- [x] Player statistics: success rate per contract, average difference, pagat ultimo conversion
- [x] Finished online tables are archived from the heartbeat; a member's session against bots from `LocalGame.Finished`
- [x] Initial migration generated (`src\TaroKing.Data\Migrations\20260920001450_Initial.cs`). A database created earlier by `EnsureCreated` must be deleted once, or `MigrateAsync` fails with "table already exists".

**Acceptance**: migrations run on an empty database; a finished game shows up in history and moves the rating; the leaderboard matches the totals.

**Notes**: the `Events` table is one row per event (`Kind`, `Seat`, `Payload`), so a hand is readable in the database and replayable from it. Guests get a localStorage name tag and the default rating; only online matches with at least two members are rated, so nobody farms points off bots. `MembersOnly` tables refuse guests.

---

## Phase 14 — Polish and deploy

- [ ] Sounds, dealing and trick-collecting animations
- [x] Logo — crown badge "TK" (option B): `BrandLogo.razor` inline in the header (colours from `--logo-badge` / `--logo-letters`, falling back to `--accent` / `--felt-dark`, so a theme only sets two variables), Sora 800 wordmark, `wwwroot/favicon.svg` (switches to the light colours with `prefers-color-scheme`), static `wwwroot/logo-dark.svg`, `logo-blue.svg`, `logo-light.svg`
- [ ] Themes (classic green / dark) + settings (speed, confirm move)
- [ ] i18n: Slovenian by default, English second
- [ ] Mobile layout (portrait)
- [ ] Health check, logging (Serilog), rate limiting
- [ ] Docker + docker-compose, deployment notes

**Acceptance**: Lighthouse ≥ 90 on the table page; the app runs in a container against an external SQLite file; the language switches without a restart.

---

## After v1

v1 = phases 0-14. Everything below is ordered by what unblocks what: first the things a live site
needs to stay up and stay clean (15-16), then the rules work (17-18), then the features people
ask for (19-23). Same commit rule: one phase = one commit (`Phase N - Name`).

Version tags: **v1.1** = 15-16 · **v1.2** = 17-18 · **v1.3** = 19 · **v2.0** = 20-22 · **v2.1** = 23.

---

## Phase 15 — Live tables survive a restart, and ops basics (v1.1)

**Goal**: deploying a new version does not kill the hands being played, and you can see what the server is doing.

- [ ] `LiveTableStore` — every event of an online hand is appended to the database as it happens (`LiveTables`, `LiveHands`, `LiveEvents`), not only at the end of the match
- [ ] On startup `TableService` rebuilds every unfinished table from its log (`HandState.Replay`), seats held, clocks reset to the full reserve
- [ ] Graceful shutdown: on `ApplicationStopping` stop dealing new hands, flush, tell the tables "strežnik se posodablja — igra se nadaljuje čez minuto"
- [ ] Reconnect UI copes with a server that went away for up to 2 minutes (Blazor reconnect modal in Slovenian, automatic retry)
- [ ] Archive queue with retry (the in-memory one from the review becomes durable)
- [ ] Nightly SQLite backup (`VACUUM INTO`) with 14-day retention; restore procedure written down in `README.md`
- [ ] Metrics: open tables, humans online, tick duration p50/p99, archive failures — OpenTelemetry → Prometheus endpoint `/metrics` (protected)
- [ ] Load test project `tools/TaroKing.LoadTest`: N bot-only tables + M simulated circuits; record the ceiling per core in `README.md`
- [ ] Optional PostgreSQL provider behind a config switch (`Database:Provider`), migrations for both

**Acceptance**: `docker compose restart app` in the middle of a trick — all four browsers reconnect and the hand continues from the same card; 200 bot tables hold tick p99 < 50 ms on the production box; a backup restores into an empty container and history is intact.

---

## Phase 16 — Account lifecycle and moderation (v1.1)

**Goal**: people can recover an account, leave, and be dealt with when they misbehave.

- [ ] Optional e-mail on the account, confirmed by link; SMTP through configuration (no provider hard-coded)
- [ ] Password reset by e-mail; change password and change e-mail on `/profil`
- [ ] Delete my account (GDPR): user row anonymised, `MatchSeat.UserId` set null, name replaced with "izbrisan igralec", rating history removed
- [ ] Export my data (JSON: profile, matches, rating history)
- [ ] Roles: `Admin`, `Moderator` (Identity roles, seeded from configuration)
- [ ] `/admin/prijave` — report queue from `BlacklistReport`: see the match, the chat, the reported player's history; actions: dismiss, warn, mute chat (timed), ban (timed / permanent)
- [ ] Mute and ban are enforced server-side in `OnlineTable.Say` / `Sit` and at login
- [ ] Personal block list: a player you blocked cannot sit at a table you host, and you do not see their chat
- [ ] Chat word filter (Slovenian + English list in a file, not in code) and per-player chat rate limit
- [ ] Audit log of every moderator action (who, what, when, why)
- [ ] Privacy page + terms page (Slovenian), linked from registration

**Acceptance**: reset link works once and expires after 1 hour; a banned account cannot log in or sit and sees why; deleting an account leaves every match it played readable with the name anonymised; every moderator action appears in the audit log.

---

## Phase 17 — Rule sets and house rules (v1.2)

**Goal**: a table can choose its rules, and every stored hand knows which rules it was played under. Builds on the `RulesVersion` introduced by the code-review fixes.

- [ ] `RuleSet` record in the engine — immutable, serialisable, carried by `HandDealt`; `RuleSet.ValatSi` is the default and the only one used for rated play
- [ ] Options, each with a test pair (on/off):
  - [ ] kontra on klop
  - [ ] radlc end-of-session penalty (value)
  - [ ] captured mond in klop
  - [ ] "mond in the talon" penalty (−21 when the declarer leaves it)
  - [ ] called king in the talon: declarer may pick it up / collects the rest of the talon by winning a trick with the king (the open item from Phases 3-4)
  - [ ] compulsory klop on a hand without trumps (on/off)
  - [ ] barvni valat upgrade after the talon (on/off)
  - [ ] who writes the score: declaring side only / both sides
  - [ ] calling a queen when you hold all four kings
- [ ] Lobby: "pravila" section when opening a table, summarised on the table card; non-default rules mark the table **unrated**
- [ ] History shows the rule set of each match; replay against bots uses the same rule set
- [ ] `RuleSet` presets: `ValatSi`, `Domača miza` (editable), `Turnir` (used by Phase 22)

**Acceptance**: every option has a hand-calculated test for both states; a match played with a custom rule set replays to identical scores; a rated table cannot be opened with anything but `ValatSi`.

---

## Phase 18 — Three-player tarok (v1.2)

**Goal**: the lobby's 3/4 switch works. 16 cards each, no king calling, mond penalty −21.

- [ ] `TableFormat` (players, hand size, talon size, trick count) replaces the constants in `TarokConstants`; every `new int[PlayerCount]` in the engine takes it from the format
- [ ] `Deal` for three: 6 talon + 3 × 16, dealt in packets of 8
- [ ] Contract table for three: no tri/dva/ena with a called king — every positive contract is a solo; bidding ladder and values per the three-handed rules (write the table into this file first, as was done for four)
- [ ] `BiddingState`, `AnnouncementRound` (no kralj ultimo), `TrickPlay` (3 cards per trick, 16 tricks), `HandScorer` (mond −21), `ScoreSheet` for three columns
- [ ] Emperor trick and must-beat rules re-checked for three cards on the table
- [ ] `PlayerView`, `EventCodec`, `HandState.Replay` carry the format; stored four-player hands replay unchanged
- [ ] `HeuristicBot` thresholds for 16-card hands; bot benchmark for three
- [ ] `Board.razor` three-seat layout (desktop + portrait phone); lobby 3/4 option; `TableOptions.Seats` no longer fixed
- [ ] Separate rating and leaderboard for three-handed play
- [ ] Fuzz test: 10 000 random three-player hands, 48 cards in tricks, 6 in the talon

**Acceptance**: three browsers play a full rated match; card points still total 70; all existing four-player tests pass untouched; a three-handed match shows up in history and moves only the three-handed rating.

---

## Phase 19 — Friends, private rooms, invites (v1.3)

**Goal**: playing with the people you know takes one link.

- [ ] Friend requests (send, accept, decline, remove); friends list with presence (online / at table X / offline) on `/prijatelji`
- [ ] Invite link for a private table: signed token, expiry, optional seat reservation per invited friend
- [ ] "Povabi" button at the table → in-app notification to online friends (toast with join button)
- [ ] Reserved seats: a reserved chair is held for its guest until the host releases it
- [ ] Host controls before the first deal: kick from seat, lock table, change options
- [ ] "Revanša" — same four, same options, new table, one click for each player to accept
- [ ] Head-to-head page: your record against one player (matches, average final score difference)
- [ ] Private-table chat and history are visible only to those who sat there (closes review item L7 properly)
- [ ] Friends-only tables: visible in the lobby to friends of the host only

**Acceptance**: invite link → seated in ≤ 2 clicks for a logged-in friend and ≤ 3 for a guest; an expired or forged token is refused; a rematch table opens with all four seated in their old chairs; presence updates within 5 seconds.

---

## Phase 20 — Stronger bot (v2.0)

**Goal**: a bot an experienced player respects, with selectable strength.

- [ ] `CardTracker` — what has been played, who is void in what (from failures to follow), who cannot hold the called king, trump count outstanding; built only from `PlayerView`
- [ ] `HandSampler` — deals the unseen cards consistently with everything the tracker knows (determinization), seeded, fast (≥ 5 000 samples/s)
- [ ] `RolloutPolicy` — the current `HeuristicBot` play logic extracted so it can drive both the bot and the rollouts
- [ ] `MctsBot` — perfect-information Monte Carlo over sampled worlds (start with flat PIMC, move to ISMCTS if PIMC's strategy-fusion shows in the benchmark); time budget per move, cancellable
- [ ] Bidding and announcing by simulation: estimate the score distribution of each legal bid from sampled deals instead of the threshold table
- [ ] Talon choice and lay-away by evaluation (needs the open talon from the review)
- [ ] Partnership inference before the king falls (who plays like a partner)
- [ ] Difficulty levels: lahek (heuristic) / srednji (MCTS 100 ms) / težek (MCTS 1 s); online fill-in bots use srednji
- [ ] `tools/TaroKing.Arena` — bot-vs-bot league with duplicate deals and seat rotation, Elo per bot version, results committed as a markdown table
- [ ] CI gate: a new bot version must not lose to the previous one over 2 000 duplicate deals
- [ ] Thinking runs off the table's gate/heartbeat thread, with a hard deadline and the heuristic move as fallback

**Acceptance**: težek beats the v1 heuristic bot by ≥ 15 points per hand over 2 000 duplicate deals with seats rotated; p99 move time ≤ budget + 50 ms; 50 tables with MCTS fill-ins keep tick p99 < 50 ms; the bot never sees anything that is not in its `PlayerView` (asserted by test, as today).

---

## Phase 21 — Post-game analysis (v2.0)

**Goal**: after a match you can see where the points went.

- [ ] Replay viewer `/partija/{id}/analiza/{hand}` — step through the event log forwards and backwards, all four hands face up (finished hands only), talon and lay-away shown
- [ ] Per-decision evaluation: at each of your decisions the Phase 20 engine scores every legal option over sampled worlds *from your point of view at that moment*; the move you made is compared with the best
- [ ] Double-dummy solver for the last 4-5 tricks (exact, all cards known) to mark real endgame errors separately from unlucky guesses
- [ ] "Kje si izgubil točke": top 3 decisions of the hand by expected points lost, in plain Slovenian ("v 7. štihu bi s kraljem pobral 9 točk več")
- [ ] Bidding review: expected score of what you bid vs. the alternatives, given only your 12 cards
- [ ] Match summary: points lost in bidding / talon / announcements / play; trend over your last 30 matches on `/profil`
- [ ] Analysis runs as a background job with a queue and a per-user daily limit; results cached in the database
- [ ] Share link to one analysed hand (read-only, no chat, names optional)

**Acceptance**: analysis of a 12-hand match finishes in < 60 s in the background; for the last five tricks the suggested line is provably optimal (solver-checked test positions); stepping through a replay never shows a hand that is still being played; the analysis of a hand is identical when run twice (seeded).

---

## Phase 22 — Tournaments and league seasons (v2.0)

**Goal**: organised competition on top of the rating.

- [ ] Seasons: quarterly, soft rating reset towards 1000 at the start, season leaderboard + all-time leaderboard, archive of past seasons
- [ ] Divisions by rating at season start; promotion/relegation at season end; badge on the profile
- [ ] Tournament entity: name, start time, format, rule set (`Turnir` from Phase 17), rounds, hands per round, entry limits
- [ ] Format 1 — **duplicate rounds**: every table in a round plays the *same deals* (same seeds, same seat for the same role), so luck of the deal cancels out; ranking by total score
- [ ] Format 2 — knockout of tables: top 2 of each table advance
- [ ] Registration, check-in window, automatic seating (avoid seating friends together in duplicate rounds), late no-show → bot + forfeit flag
- [ ] Round clock, automatic start of each round, waiting room between rounds, live standings page
- [ ] Anti-collusion in duplicate play: seeds are generated at round start, never stored client-side, and tables of one round finish before any of its hands become viewable in history
- [ ] Organiser tools (`Moderator` role): create, pause, replace a player with a bot, void a table
- [ ] Results page per tournament, permanent; winners on the home page

**Acceptance**: a 16-player, 3-round duplicate tournament runs unattended from check-in to final standings with 16 bot clients; identical deals are verified across tables of a round; a player dropping mid-round does not stall the round; season rollover is a single idempotent job.

---

## Phase 23 — Installable app and notifications (v2.1)

**Goal**: TaroKing behaves like an app on a phone.

- [ ] PWA manifest, icons, splash, offline shell page ("ni povezave — poskušam znova")
- [ ] Web Push (VAPID): "na potezi si" when the tab is in the background, "miza se začenja", friend invite, tournament round starting; per-type switches on `/profil`
- [ ] Wake-lock while seated at a table; vibration on your turn (setting)
- [ ] Touch: drag a card to play, long-press for a larger view, one-thumb layout for the action panel
- [ ] Spectator delay option for public tables (views lag by one trick) so watching cannot help a player
- [ ] Bandwidth: measure render batch size per tick at a full table; trim `Board.razor` re-renders to the seats that changed
- [ ] Accessibility pass: keyboard play for every action, screen-reader labels for cards ("srčev kralj"), colour-blind suit markers

**Acceptance**: installs from Chrome on Android and from Safari on iOS; a push arrives within 5 s of your turn starting with the tab closed; a full hand is playable with the keyboard only; Lighthouse PWA and accessibility ≥ 90.

---

## Backlog (not scheduled)

- Native wrapper (MAUI Blazor Hybrid) if the PWA proves too limited on iOS
- Other tarok variants (Croatian, Austrian Königrufen) on the same engine through `RuleSet` + `TableFormat`
- Public read-only API for statistics
- Training mode: the bot explains its move while you play
