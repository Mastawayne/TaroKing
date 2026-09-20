using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace TaroKing.App.Services;

/// <summary>
/// Every local game currently in the air, keyed by the id that appears in the URL.
///
/// A Blazor circuit dies when the browser reloads, so a game that lived in the circuit would die
/// with it. Keeping games here instead means a refresh — or a phone picking up where a laptop left
/// off — finds the same table, mid-trick if need be. Games nobody has touched for a while are
/// swept up, because this is memory, not a database; durable storage arrives with the accounts.
/// </summary>
public sealed class LocalGames(Action<LocalGame>? onFinished = null) : IDisposable {

	/// <summary>A game nobody has looked at for this long is forgotten.</summary>
	public static readonly TimeSpan IdleLifetime = TimeSpan.FromHours(6);

	private readonly ConcurrentDictionary<string, LocalGame> _games = new(StringComparer.Ordinal);

	/// <summary>A snapshot of the games in flight.</summary>
	public IReadOnlyList<LocalGame> All => [.. _games.Values];

	/// <summary>Start a new game for somebody and hand it back.</summary>
	public LocalGame Create(SessionOptions options, PlayerIdentity? owner = null) {
		ArgumentNullException.ThrowIfNull(options);

		Sweep();

		LocalGame game = new(NewId(), options, owner);

		if (onFinished is not null) {
			game.Finished += onFinished;
		}

		_games[game.Id] = game;

		return game;
	}

	/// <summary>The game with this id, or null once it has been swept up.</summary>
	public LocalGame? Find(string? id) =>
		string.IsNullOrWhiteSpace(id) ? null : _games.GetValueOrDefault(id);

	public void Drop(string id) {
		if (_games.TryRemove(id, out LocalGame? game)) {
			game.Dispose();
		}
	}

	/// <summary>Forget whatever has been sitting untouched.</summary>
	public int Sweep() {
		DateTimeOffset cutoff = DateTimeOffset.UtcNow - IdleLifetime;
		int swept = 0;

		foreach (LocalGame game in _games.Values) {
			if (game.LastTouched < cutoff && _games.TryRemove(game.Id, out LocalGame? removed)) {
				removed.Dispose();
				swept++;
			}
		}

		return swept;
	}

	private static string NewId() => Convert.ToHexString(RandomNumberGenerator.GetBytes(6)).ToLowerInvariant();

	public void Dispose() {
		foreach (LocalGame game in _games.Values) {
			game.Dispose();
		}

		_games.Clear();
	}
}
