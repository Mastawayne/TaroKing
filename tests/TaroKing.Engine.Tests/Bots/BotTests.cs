using TaroKing.Bots;
using TaroKing.Engine.Bidding;
using TaroKing.Engine.Cards;
using TaroKing.Engine.Scoring;
using TaroKing.Engine.Session;

namespace TaroKing.Engine.Tests.Bots;

public class BotTests {

	// --- the table runs ---

	[Fact]
	public async Task A_table_of_random_bots_finishes_every_hand() {
		for (int seed = 0; seed < 40; seed++) {
			HandState hand = await BotTable.PlayHandAsync(BotTable.NewHand(seed), RandomTable(seed));

			Assert.Equal(GamePhase.Finished, hand.Phase);
			Assert.NotNull(hand.Score);
		}
	}

	[Fact]
	public async Task A_table_of_thinking_bots_finishes_every_hand() {
		for (int seed = 0; seed < 40; seed++) {
			HandState hand = await BotTable.PlayHandAsync(BotTable.NewHand(seed), HeuristicTable(seed));

			Assert.Equal(GamePhase.Finished, hand.Phase);
			Assert.NotNull(hand.Score);
			Assert.NotNull(hand.PlayedContract);
		}
	}

	[Fact]
	public async Task A_bot_hand_replays_from_its_log() {
		HandState hand = await BotTable.PlayHandAsync(BotTable.NewHand(11), HeuristicTable(11));
		HandState replayed = HandState.Replay(hand.Events);

		Assert.Equal(hand.PlayedContract, replayed.PlayedContract);
		Assert.Equal(hand.Score!.BySeat, replayed.Score!.BySeat);
	}

	[Fact]
	public async Task A_hand_can_be_driven_one_seat_at_a_time() {
		// This is what the table UI does: it asks for a single action, redraws, and comes back.
		HandState hand = BotTable.NewHand(7);
		IPlayerAgent[] agents = HeuristicTable(7);
		ScoreSheet sheet = new();

		while (hand.Phase != GamePhase.Finished) {
			int seat = hand.CurrentSeat!.Value;
			await BotTable.ActAsync(hand, agents[seat], seat);
		}

		BotTable.Record(sheet, hand);

		Assert.Single(sheet.Hands);
		Assert.Equal(hand.Score!.BySeat, sheet.Hands[0].BySeat);
		Assert.Equal(hand.Score.BySeat[0], sheet.Totals[0]);
	}

	[Fact]
	public void A_hand_that_is_still_running_cannot_be_written_down() {
		Assert.Throws<InvalidOperationException>(() => BotTable.Record(new ScoreSheet(), BotTable.NewHand(3)));
	}

	[Fact]
	public async Task A_session_keeps_a_running_sheet() {
		ScoreSheet sheet = await BotTable.PlaySessionAsync(hands: 12, firstSeed: 100, HeuristicTable(3));

		Assert.Equal(12, sheet.Hands.Count);
		Assert.Equal(TarokConstants.PlayerCount, sheet.Totals.Count);
		Assert.Equal(sheet.Hands.Sum(hand => hand[0]), sheet.Totals[0]);
	}

	// --- the thinking bot is better than the coin-flipping one ---

	[Fact]
	public async Task Thinking_beats_flipping_a_coin() {
		int thinking = 0;
		int flipping = 0;

		// Half the hands with the thinkers on seats 0 and 2, half with them on 1 and 3,
		// so forehand's advantage cannot decide the match.
		for (int seed = 0; seed < 60; seed++) {
			bool thinkersFirst = seed % 2 == 0;
			IPlayerAgent[] table = thinkersFirst
				? [Thinker(seed, 0), Flipper(seed, 1), Thinker(seed, 2), Flipper(seed, 3)]
				: [Flipper(seed, 0), Thinker(seed, 1), Flipper(seed, 2), Thinker(seed, 3)];

			HandState hand = await BotTable.PlayHandAsync(BotTable.NewHand(seed), table);
			HandScore score = hand.Score!;

			for (int seat = 0; seat < TarokConstants.PlayerCount; seat++) {
				bool isThinker = thinkersFirst ? seat % 2 == 0 : seat % 2 == 1;
				if (isThinker) {
					thinking += score[seat];
				} else {
					flipping += score[seat];
				}
			}
		}

		Assert.True(thinking > flipping, $"Thinking scored {thinking}, flipping scored {flipping}.");
	}

	// --- the pieces of the thinking ---

	[Fact]
	public void A_hand_full_of_trumps_counts_for_more_than_a_hand_of_rubbish() {
		Assert.True(HeuristicBot.Strength(MonsterHand()) > HeuristicBot.Strength(JunkHand()));
	}

	[Fact]
	public async Task A_monster_hand_bids_high_and_a_poor_one_passes() {
		HeuristicBot bot = new(seed: 1);

		Contract? big = await bot.ChooseBidAsync(BiddingView(MonsterHand()));
		Contract? nothing = await bot.ChooseBidAsync(BiddingView(JunkHand()));

		Assert.NotNull(big);
		Assert.True(big >= Contract.SoloThree, $"A monster hand should be worth more than {big}.");
		Assert.Null(nothing);
	}

	[Fact]
	public async Task Forehand_with_nobody_else_in_names_a_klop_rather_than_nothing() {
		HeuristicBot bot = new(seed: 1);

		PlayerView view = BiddingView(JunkHand()) with {
			LegalBids = Contracts.From(Contract.Klop),
			CanPassBid = false
		};

		Assert.Equal(Contract.Klop, await bot.ChooseBidAsync(view));
	}

	[Fact]
	public async Task A_hand_that_can_take_no_trick_bids_a_beggar() {
		HeuristicBot bot = new(seed: 1);

		PlayerView view = BiddingView(BeggarHand()) with { LegalBids = Contracts.From(Contract.Two) };

		Assert.Equal(Contract.Beggar, await bot.ChooseBidAsync(view));
	}

	[Fact]
	public async Task The_lay_away_hides_points_in_the_declarers_own_pile() {
		HeuristicBot bot = new(seed: 1);

		Card[] legal = [
			Card.Of(Suit.Clubs, SuitRank.Queen),   // 4 points
			Card.Of(Suit.Spades, SuitRank.Knight), // 3
			Card.Of(Suit.Hearts, SuitRank.Pip1),   // 1
			Card.Trump(5)                          // 1, and a trump worth keeping
		];

		PlayerView view = new() {
			Seat = 0,
			Phase = GamePhase.Talon,
			Declarer = 0,
			Contract = Contract.Two,
			Hand = legal,
			LegalDiscards = legal,
			DiscardCount = 2
		};

		IReadOnlyList<Card> chosen = await bot.ChooseDiscardsAsync(view);

		Assert.Equal(2, chosen.Count);
		Assert.Contains(Card.Of(Suit.Clubs, SuitRank.Queen), chosen);
		Assert.Contains(Card.Of(Suit.Spades, SuitRank.Knight), chosen);
		Assert.DoesNotContain(Card.Trump(5), chosen);
	}

	[Fact]
	public async Task A_king_is_called_in_the_suit_you_are_longest_in() {
		HeuristicBot bot = new(seed: 1);

		Card[] hand = [
			Card.Of(Suit.Hearts, SuitRank.Pip1),
			Card.Of(Suit.Hearts, SuitRank.Pip2),
			Card.Of(Suit.Hearts, SuitRank.Queen),
			Card.Of(Suit.Clubs, SuitRank.Pip1),
			Card.Trump(9)
		];

		PlayerView view = new() {
			Seat = 0,
			Phase = GamePhase.KingCall,
			Declarer = 0,
			Hand = hand,
			LegalKingCalls = [Suit.Clubs, Suit.Spades, Suit.Hearts, Suit.Diamonds]
		};

		Assert.Equal(Suit.Hearts, await bot.ChooseKingAsync(view));
	}

	[Fact]
	public async Task A_king_you_already_hold_is_not_the_one_you_call() {
		HeuristicBot bot = new(seed: 1);

		Card[] hand = [
			Card.Of(Suit.Hearts, SuitRank.King),
			Card.Of(Suit.Hearts, SuitRank.Pip1),
			Card.Of(Suit.Hearts, SuitRank.Pip2),
			Card.Of(Suit.Clubs, SuitRank.Pip1),
			Card.Of(Suit.Clubs, SuitRank.Pip2)
		];

		PlayerView view = new() {
			Seat = 0,
			Phase = GamePhase.KingCall,
			Declarer = 0,
			Hand = hand,
			LegalKingCalls = [Suit.Clubs, Suit.Spades, Suit.Hearts, Suit.Diamonds]
		};

		Assert.NotEqual(Suit.Hearts, await bot.ChooseKingAsync(view));
	}

	[Fact]
	public async Task A_slow_agent_answers_the_same_as_the_one_it_wraps() {
		HeuristicBot inner = new(seed: 1);
		SlowAgent slow = new(inner, TimeSpan.Zero);

		PlayerView view = BiddingView(MonsterHand());

		Assert.Equal(await inner.ChooseBidAsync(view), await slow.ChooseBidAsync(view));
		Assert.Equal(inner.Name, slow.Name);
	}

	[Fact]
	public async Task A_table_needs_four_agents() {
		await Assert.ThrowsAsync<ArgumentException>(async () =>
			await BotTable.PlayHandAsync(BotTable.NewHand(1), [new RandomBot(1), new RandomBot(2)]));
	}

	// --- helpers ---

	private static IPlayerAgent[] RandomTable(int seed) => [
		new RandomBot(seed * 4),
		new RandomBot((seed * 4) + 1),
		new RandomBot((seed * 4) + 2),
		new RandomBot((seed * 4) + 3)
	];

	private static IPlayerAgent[] HeuristicTable(int seed) => [
		Thinker(seed, 0), Thinker(seed, 1), Thinker(seed, 2), Thinker(seed, 3)
	];

	private static IPlayerAgent Thinker(int seed, int seat) =>
		new HeuristicBot((seed * 4) + seat, seat % 2 == 0 ? BotProfile.Normal : BotProfile.Cautious);

	private static IPlayerAgent Flipper(int seed, int seat) => new RandomBot((seed * 4) + seat);

	private static PlayerView BiddingView(IReadOnlyList<Card> hand) => new() {
		Seat = 1,
		Phase = GamePhase.Bidding,
		CurrentSeat = 1,
		Hand = hand,
		LegalBids = Contracts.From(Contract.Two),
		CanPassBid = true
	};

	/// <summary>Ten trumps including all three five-pointers, plus two kings.</summary>
	private static Card[] MonsterHand() => [
		Card.Skis, Card.Mond, Card.Pagat,
		Card.Trump(20), Card.Trump(19), Card.Trump(18), Card.Trump(17),
		Card.Trump(16), Card.Trump(15), Card.Trump(14),
		Card.Of(Suit.Hearts, SuitRank.King), Card.Of(Suit.Clubs, SuitRank.King)
	];

	private static Card[] JunkHand() => [
		Card.Trump(2), Card.Trump(3),
		Card.Of(Suit.Hearts, SuitRank.Pip1), Card.Of(Suit.Hearts, SuitRank.Pip2),
		Card.Of(Suit.Clubs, SuitRank.Pip1), Card.Of(Suit.Clubs, SuitRank.Pip2),
		Card.Of(Suit.Spades, SuitRank.Pip1), Card.Of(Suit.Spades, SuitRank.Pip2),
		Card.Of(Suit.Diamonds, SuitRank.Pip1), Card.Of(Suit.Diamonds, SuitRank.Pip2),
		Card.Of(Suit.Diamonds, SuitRank.Pip3), Card.Of(Suit.Diamonds, SuitRank.King)
	];

	/// <summary>No king, three tiny trumps, nothing that takes a trick.</summary>
	private static Card[] BeggarHand() => [
		Card.Trump(2), Card.Trump(4), Card.Trump(6),
		Card.Of(Suit.Hearts, SuitRank.Pip1), Card.Of(Suit.Hearts, SuitRank.Pip2),
		Card.Of(Suit.Clubs, SuitRank.Pip1), Card.Of(Suit.Clubs, SuitRank.Pip2),
		Card.Of(Suit.Spades, SuitRank.Pip1), Card.Of(Suit.Spades, SuitRank.Pip2),
		Card.Of(Suit.Diamonds, SuitRank.Pip1), Card.Of(Suit.Diamonds, SuitRank.Pip2),
		Card.Of(Suit.Diamonds, SuitRank.Pip3)
	];
}
