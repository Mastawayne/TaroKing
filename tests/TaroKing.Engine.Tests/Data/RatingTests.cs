using TaroKing.Data;

namespace TaroKing.Engine.Tests.Data;

public class RatingTests {

	[Fact]
	public void Equals_who_finish_level_do_not_move() {
		RatedSeat[] seats = [new(0, 1000, 40), new(1, 1000, 40)];

		IReadOnlyDictionary<int, int> deltas = Rating.Deltas(seats, rounds: 12);

		Assert.Equal(0, deltas[0]);
		Assert.Equal(0, deltas[1]);
	}

	[Fact]
	public void The_winner_goes_up_and_the_loser_down_by_the_same_amount() {
		RatedSeat[] seats = [new(0, 1000, 250), new(1, 1000, -250)];

		IReadOnlyDictionary<int, int> deltas = Rating.Deltas(seats, rounds: 12);

		Assert.True(deltas[0] > 0);
		Assert.Equal(-deltas[0], deltas[1]);
	}

	[Fact]
	public void A_wider_margin_moves_more() {
		int narrow = Rating.Deltas([new(0, 1000, 40), new(1, 1000, 30)], rounds: 12)[0];
		int wide = Rating.Deltas([new(0, 1000, 300), new(1, 1000, -300)], rounds: 12)[0];

		Assert.True(wide > narrow, $"wide {wide} should beat narrow {narrow}");
	}

	[Fact]
	public void A_longer_match_moves_more() {
		int shortMatch = Rating.Deltas([new(0, 1000, 200), new(1, 1000, -200)], rounds: 7)[0];
		int longMatch = Rating.Deltas([new(0, 1000, 200), new(1, 1000, -200)], rounds: 30)[0];

		Assert.True(longMatch > shortMatch, $"long {longMatch} should beat short {shortMatch}");
	}

	[Fact]
	public void Beating_a_stronger_player_pays_more_than_beating_a_weaker_one() {
		int overStronger = Rating.Deltas([new(0, 1000, 200), new(1, 1400, -200)], rounds: 12)[0];
		int overWeaker = Rating.Deltas([new(0, 1000, 200), new(1, 600, -200)], rounds: 12)[0];

		Assert.True(overStronger > overWeaker, $"stronger {overStronger} should beat weaker {overWeaker}");
	}

	[Fact]
	public void Four_rated_players_roughly_balance_out() {
		RatedSeat[] seats = [new(0, 1000, 180), new(1, 1000, 60), new(2, 1000, -90), new(3, 1000, -150)];

		IReadOnlyDictionary<int, int> deltas = Rating.Deltas(seats, rounds: 12);

		Assert.True(deltas[0] > deltas[1]);
		Assert.True(deltas[1] > deltas[2]);
		Assert.True(deltas[2] > deltas[3]);

		// Rounding leaves at most a point or two on the table.
		Assert.InRange(deltas.Values.Sum(), -4, 4);
	}

	[Fact]
	public void One_rated_player_is_not_a_rated_match() {
		Assert.Empty(Rating.Deltas([new(0, 1000, 300)], rounds: 12));
	}

	[Fact]
	public void The_expectation_is_the_familiar_curve() {
		Assert.Equal(0.5, Rating.Expected(1000, 1000), precision: 6);
		Assert.True(Rating.Expected(1400, 1000) > 0.9);
		Assert.True(Rating.Expected(1000, 1400) < 0.1);
	}
}
