using TaroKing.Engine;
using TaroKing.Engine.Announcing;
using TaroKing.Engine.Bidding;
using TaroKing.Engine.Cards;
using TaroKing.Engine.Scoring;
using TaroKing.Engine.Session;

namespace TaroKing.Bots;

/// <summary>Any agent, made to think for a moment before answering.</summary>
public sealed class SlowAgent(IPlayerAgent inner, TimeSpan delay) : IPlayerAgent {

	public string Name => inner.Name;

	public async ValueTask<Contract?> ChooseBidAsync(PlayerView view, CancellationToken cancellationToken = default) {
		await ThinkAsync(cancellationToken);
		return await inner.ChooseBidAsync(view, cancellationToken);
	}

	public async ValueTask<Suit> ChooseKingAsync(PlayerView view, CancellationToken cancellationToken = default) {
		await ThinkAsync(cancellationToken);
		return await inner.ChooseKingAsync(view, cancellationToken);
	}

	public async ValueTask<int> ChooseTalonPacketAsync(PlayerView view, CancellationToken cancellationToken = default) {
		await ThinkAsync(cancellationToken);
		return await inner.ChooseTalonPacketAsync(view, cancellationToken);
	}

	public async ValueTask<IReadOnlyList<Card>> ChooseDiscardsAsync(PlayerView view, CancellationToken cancellationToken = default) {
		await ThinkAsync(cancellationToken);
		return await inner.ChooseDiscardsAsync(view, cancellationToken);
	}

	public async ValueTask<bool> ChooseColourValatUpgradeAsync(PlayerView view, CancellationToken cancellationToken = default) {
		await ThinkAsync(cancellationToken);
		return await inner.ChooseColourValatUpgradeAsync(view, cancellationToken);
	}

	public async ValueTask<AnnouncementChoice> ChooseAnnouncementAsync(PlayerView view, CancellationToken cancellationToken = default) {
		await ThinkAsync(cancellationToken);
		return await inner.ChooseAnnouncementAsync(view, cancellationToken);
	}

	public async ValueTask<Card> ChooseCardAsync(PlayerView view, CancellationToken cancellationToken = default) {
		await ThinkAsync(cancellationToken);
		return await inner.ChooseCardAsync(view, cancellationToken);
	}

	private Task ThinkAsync(CancellationToken cancellationToken) =>
		delay <= TimeSpan.Zero ? Task.CompletedTask : Task.Delay(delay, cancellationToken);
}

/// <summary>
/// Sits four agents down and plays. Every answer an agent gives goes back through the engine,
/// so a bot that tries something illegal is refused exactly as a client would be.
/// </summary>
public static class BotTable {

	/// <summary>
	/// A fresh hand. A deal where somebody holds no trump at all is annulled and dealt again,
	/// and that replacement is played as a compulsory klop.
	/// </summary>
	public static HandState NewHand(int seed) {
		HandState hand = HandState.Create(seed);

		return hand.Deal.RequiresRedeal
			? HandState.Create(unchecked(seed + 1), compulsoryKlop: true)
			: hand;
	}

	/// <summary>Play one hand to its score.</summary>
	public static async ValueTask<HandState> PlayHandAsync(
		HandState hand,
		IReadOnlyList<IPlayerAgent> agents,
		ScoreSheet? sheet = null,
		CancellationToken cancellationToken = default) {

		ArgumentNullException.ThrowIfNull(hand);
		ArgumentNullException.ThrowIfNull(agents);

		if (agents.Count != TarokConstants.PlayerCount) {
			throw new ArgumentException($"A table seats {TarokConstants.PlayerCount}.", nameof(agents));
		}

		bool radlcApplied = false;

		while (hand.Phase != GamePhase.Finished) {
			// As soon as the auction names a declarer, the sheet says whether the hand counts double.
			if (!radlcApplied && sheet is not null && hand.Declarer is int declarer) {
				hand.ApplyRadlcMultiplier(sheet.RadlcMultiplierFor(declarer));
				radlcApplied = true;
			}

			int seat = hand.CurrentSeat!.Value;
			await ActAsync(hand, agents[seat], seat, cancellationToken);
		}

		if (sheet is not null) {
			Record(sheet, hand);
		}

		return hand;
	}

	/// <summary>Play a run of hands onto one sheet.</summary>
	public static async ValueTask<ScoreSheet> PlaySessionAsync(
		int hands,
		int firstSeed,
		IReadOnlyList<IPlayerAgent> agents,
		CancellationToken cancellationToken = default) {

		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(hands);

		ScoreSheet sheet = new();

		for (int index = 0; index < hands; index++) {
			await PlayHandAsync(NewHand(firstSeed + index), agents, sheet, cancellationToken);
		}

		return sheet;
	}

	/// <summary>Let one agent take the single action the hand is waiting for from its seat.</summary>
	public static async ValueTask ActAsync(HandState hand, IPlayerAgent agent, int seat, CancellationToken cancellationToken = default) {
		ArgumentNullException.ThrowIfNull(hand);
		ArgumentNullException.ThrowIfNull(agent);

		PlayerView view = PlayerView.For(hand, seat);

		switch (hand.Phase) {
			case GamePhase.Bidding: {
				Contract? bid = await agent.ChooseBidAsync(view, cancellationToken);
				if (bid is null) {
					hand.PassBid(seat);
				} else {
					hand.PlaceBid(seat, bid.Value);
				}

				break;
			}

			case GamePhase.KingCall:
				hand.CallKing(seat, await agent.ChooseKingAsync(view, cancellationToken));
				break;

			case GamePhase.Talon when hand.Talon!.NeedsPacketChoice:
				hand.TakeTalonPacket(seat, await agent.ChooseTalonPacketAsync(view, cancellationToken));
				break;

			case GamePhase.Talon when hand.Talon!.NeedsDiscard:
				hand.Discard(seat, await agent.ChooseDiscardsAsync(view, cancellationToken));
				break;

			case GamePhase.Talon when hand.AwaitsUpgradeDecision: {
				bool upgrade = await agent.ChooseColourValatUpgradeAsync(view, cancellationToken);
				if (upgrade) {
					hand.UpgradeToColourValat(seat);
				} else {
					hand.KeepContract(seat);
				}

				break;
			}

			case GamePhase.Announcing: {
				AnnouncementChoice choice = await agent.ChooseAnnouncementAsync(view, cancellationToken);
				if (choice.Bonus is Bonus bonus) {
					hand.Announce(seat, bonus);
				} else if (choice.Kontra is KontraTarget target) {
					hand.Kontra(seat, target);
				} else {
					hand.PassAnnouncement(seat);
				}

				break;
			}

			case GamePhase.Play:
				hand.PlayCard(seat, await agent.ChooseCardAsync(view, cancellationToken));
				break;

			default:
				throw new InvalidOperationException($"There is nothing for seat {seat} to do in {hand.Phase}.");
		}
	}

	/// <summary>Write a finished hand onto a sheet, radlci and all.</summary>
	public static void Record(ScoreSheet sheet, HandState hand) {
		ArgumentNullException.ThrowIfNull(sheet);
		ArgumentNullException.ThrowIfNull(hand);

		if (hand.Phase != GamePhase.Finished) {
			throw new InvalidOperationException("The hand is not over yet.");
		}

		sheet.Record(hand.PlayedContract!.Value, hand.Declarer!.Value, hand.Score!, WasAValatInPlay(hand));
	}

	private static bool WasAValatInPlay(HandState hand) =>
		hand.PlayedContract is Contract.Valat or Contract.ColourValat
		|| hand.Announcements?.Announcements.Any(announced => announced.Kind == Bonus.Valat) == true;
}
