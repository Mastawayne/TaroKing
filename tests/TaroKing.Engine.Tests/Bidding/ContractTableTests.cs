using TaroKing.Engine.Bidding;

namespace TaroKing.Engine.Tests.Bidding;

public class ContractTableTests {

	[Fact]
	public void Every_contract_has_an_entry_at_its_own_index() {
		Assert.Equal(12, Contracts.All.Count);

		foreach (Contract contract in Enum.GetValues<Contract>()) {
			Assert.Equal(contract, Contracts.Info(contract).Contract);
		}
	}

	[Fact]
	public void Game_values_rise_with_the_bid_from_three_upwards() {
		int previous = 0;
		for (Contract contract = Contract.Three; contract <= Contract.Valat; contract++) {
			int value = Contracts.Info(contract).GameValue;
			Assert.True(value > previous, $"{contract} is worth {value}, not more than {previous}.");
			previous = value;
		}
	}

	[Theory]
	[InlineData(Contract.Klop, 70)]
	[InlineData(Contract.Three, 10)]
	[InlineData(Contract.Two, 20)]
	[InlineData(Contract.One, 30)]
	[InlineData(Contract.SoloThree, 40)]
	[InlineData(Contract.SoloTwo, 50)]
	[InlineData(Contract.SoloOne, 60)]
	[InlineData(Contract.Beggar, 70)]
	[InlineData(Contract.SoloWithout, 80)]
	[InlineData(Contract.OpenBeggar, 90)]
	[InlineData(Contract.ColourValat, 125)]
	[InlineData(Contract.Valat, 500)]
	public void Game_values_match_the_scoring_table(Contract contract, int expected) {
		Assert.Equal(expected, Contracts.Info(contract).GameValue);
	}

	[Theory]
	[InlineData(Contract.Three, 3)]
	[InlineData(Contract.Two, 2)]
	[InlineData(Contract.One, 1)]
	[InlineData(Contract.SoloThree, 3)]
	[InlineData(Contract.SoloTwo, 2)]
	[InlineData(Contract.SoloOne, 1)]
	[InlineData(Contract.Klop, 0)]
	[InlineData(Contract.Beggar, 0)]
	[InlineData(Contract.SoloWithout, 0)]
	[InlineData(Contract.OpenBeggar, 0)]
	[InlineData(Contract.ColourValat, 0)]
	[InlineData(Contract.Valat, 0)]
	public void Talon_sizes_match_the_contract(Contract contract, int expected) {
		Assert.Equal(expected, Contracts.Info(contract).TalonCards);
	}

	[Fact]
	public void Only_three_two_and_one_call_a_king() {
		List<Contract> calling = Contracts.All.Where(info => info.CallsKing).Select(info => info.Contract).ToList();

		Assert.Equal(new[] { Contract.Three, Contract.Two, Contract.One }, calling);
	}

	[Fact]
	public void Only_klop_and_three_belong_to_forehand() {
		List<Contract> forehandOnly = Contracts.All.Where(info => info.ForehandOnly).Select(info => info.Contract).ToList();

		Assert.Equal(new[] { Contract.Klop, Contract.Three }, forehandOnly);
	}

	[Fact]
	public void Card_points_decide_the_seven_positive_contracts() {
		List<Contract> counting = Contracts.All.Where(info => info.CountsCardPoints).Select(info => info.Contract).ToList();

		Assert.Equal(
			new[] {
				Contract.Three, Contract.Two, Contract.One,
				Contract.SoloThree, Contract.SoloTwo, Contract.SoloOne,
				Contract.SoloWithout
			},
			counting);

		// Solo without is decided on card points but scores neither a difference nor bonuses.
		Assert.False(Contracts.Info(Contract.SoloWithout).AllowsBonuses);
		Assert.True(Contracts.Info(Contract.SoloOne).AllowsBonuses);
	}

	[Fact]
	public void Forehand_leads_up_to_solo_one_and_the_declarer_leads_above_it() {
		for (Contract contract = Contract.Klop; contract <= Contract.SoloOne; contract++) {
			Assert.True(Contracts.Info(contract).ForehandLeads, $"{contract} should be led by forehand.");
		}

		for (Contract contract = Contract.Beggar; contract <= Contract.Valat; contract++) {
			Assert.False(Contracts.Info(contract).ForehandLeads, $"{contract} should be led by the declarer.");
		}
	}

	[Fact]
	public void The_negative_contracts_are_klop_and_the_two_beggars() {
		List<Contract> negative = Contracts.All.Where(info => info.TakesNoTricks).Select(info => info.Contract).ToList();

		Assert.Equal(new[] { Contract.Klop, Contract.Beggar, Contract.OpenBeggar }, negative);
	}

	[Fact]
	public void Only_the_valats_demand_every_trick_and_only_colour_valat_flattens_the_trumps() {
		List<Contract> allTricks = Contracts.All.Where(info => info.TakesAllTricks).Select(info => info.Contract).ToList();

		Assert.Equal(new[] { Contract.ColourValat, Contract.Valat }, allTricks);
		Assert.True(Contracts.Info(Contract.ColourValat).TrumpsArePlainSuit);
		Assert.False(Contracts.Info(Contract.Valat).TrumpsArePlainSuit);
	}

	[Fact]
	public void From_returns_the_ladder_above_a_contract() {
		Assert.Equal(12, Contracts.From(Contract.Klop).Count);
		Assert.Equal(10, Contracts.From(Contracts.LowestOpenBid).Count);
		Assert.Equal(new[] { Contract.Valat }, Contracts.From(Contract.Valat));
	}
}
