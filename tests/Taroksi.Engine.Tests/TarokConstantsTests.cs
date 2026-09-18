namespace Taroksi.Engine.Tests;

public class TarokConstantsTests {

	[Fact]
	public void Deck_is_made_of_trumps_and_four_suits() {
		Assert.Equal(TarokConstants.DeckSize, TarokConstants.TrumpCount + (4 * TarokConstants.SuitSize));
	}

	[Fact]
	public void Deal_uses_every_card() {
		int dealt = (TarokConstants.PlayerCount * TarokConstants.HandSize) + TarokConstants.TalonSize;
		Assert.Equal(TarokConstants.DeckSize, dealt);
	}

	[Fact]
	public void Winning_threshold_is_one_above_half_the_pack() {
		Assert.Equal(TarokConstants.WinningCardPoints, TarokConstants.DifferenceBaseline + 1);
	}
}
