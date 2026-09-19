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

- [ ] "Quick game" — 1 human + 3 bots, instant start
- [ ] Choose number of hands / play to X points
- [ ] End screen with score sheet and session statistics
- [ ] Saving and resuming an interrupted session

**Acceptance**: 10 consecutive hands without an error; refreshing the page mid-hand restores the state.

---

## Phase 12 — Online tables

**Goal**: real multiplayer on Blazor Server circuits.

- [ ] `TableService` (singleton) — table list, seats, state, one authoritative `GameState` per table
- [ ] Lobby: create a table (public/private, 3/4 players, timer), join, leave
- [ ] Table options in valat.si's shape: number of rounds (7-30), seconds per move (1.5-4.5), minimum rating to sit down, members-only
- [ ] Seed every hand from a cryptographic source and store the seed — the engine's PRNG stays deterministic for replay, but the seed must not be guessable (valat.si mixes atmospheric noise with /dev/urandom)
- [ ] State broadcast per seat (everyone gets their own `PlayerView`)
- [ ] Reconnect: a refresh or dropped connection does not kill the hand (60 s grace)
- [ ] Move timer + auto-move on timeout
- [ ] A bot takes over the seat of a player who drops
- [ ] Table chat + spectators (no view of anyone's cards)

**Acceptance**: 4 browsers play a full hand; killing one tab mid-hand hands the seat to a bot and gives it back on return; no client ever receives another player's cards (verified in the network log).

---

## Phase 13 — Accounts, persistence, rating

- [ ] EF Core + SQLite: `Users`, `Matches`, `Hands`, `Events`, `Ratings`, `ChatMessages`
- [ ] Login/registration (ASP.NET Core Identity) + guest play without an account
- [ ] Store played games (event log) + history viewer
- [ ] Rating (Elo-like, per match rather than per trick) + leaderboard — valat.si weights it by match length, the ratio of final scores, and the opponents' starting ratings
- [ ] Player flags in the lobby, as valat.si does it: member, frequent leaver, number of blacklist reports
- [ ] Last 30 games visible in history, each round replayable against the bots
- [ ] Player statistics: success rate per contract type, average difference, pagat ultimo conversion

**Acceptance**: migrations run on an empty database; a finished game shows up in history and moves the rating; the leaderboard matches the totals.

---

## Phase 14 — Polish and deploy

- [ ] Sounds, dealing and trick-collecting animations
- [ ] Themes (classic green / dark) + settings (speed, confirm move)
- [ ] i18n: Slovenian by default, English second
- [ ] Mobile layout (portrait)
- [ ] Health check, logging (Serilog), rate limiting
- [ ] Docker + docker-compose, deployment notes

**Acceptance**: Lighthouse ≥ 90 on the table page; the app runs in a container against an external SQLite file; the language switches without a restart.

---

## After v1

- 3-player tarok (16 cards, no king calling, Mond penalty −21)
- Tournaments and league seasons
- Post-game analysis (where you lost points)
- Stronger bot (MCTS with determinization of hidden cards)
- Friends, private rooms, invites
