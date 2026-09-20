using System.Security.Claims;
using System.Text.RegularExpressions;

using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.JSInterop;

using TaroKing.Data;

namespace TaroKing.App.Services;

/// <summary>
/// Who somebody is at a table. A registered player carries their user id and their real rating;
/// a guest carries a name tag the browser remembers and the default rating, and never gets rated.
///
/// Ids live in two namespaces that cannot collide: <c>user-{identityId}</c> for members and
/// <c>guest-{32 hex}</c> for guests. Nothing a browser sends can turn one into the other.
/// </summary>
public sealed record PlayerIdentity(string Id, string Name, int Rating = PlayerIdentity.DefaultRating) {

	public const int DefaultRating = TaroKingUser.StartingRating;

	public static PlayerIdentity Unknown { get; } = new("", "Gost");

	/// <summary>The Identity user id for a registered player; null for a guest.</summary>
	public string? UserId { get; init; }

	/// <summary>Chat is refused until then. Set from the account when the session starts.</summary>
	public DateTimeOffset? MutedUntil { get; init; }

	/// <summary>Sitting down is refused until then.</summary>
	public DateTimeOffset? BannedUntil { get; init; }

	public bool IsKnown => !string.IsNullOrWhiteSpace(Id);

	public bool IsMember => UserId is not null;

	public bool IsMuted => MutedUntil is DateTimeOffset until && until > DateTimeOffset.UtcNow;

	public bool IsBanned => BannedUntil is DateTimeOffset until && until > DateTimeOffset.UtcNow;

	public static string MemberId(string userId) => "user-" + userId;
}

/// <summary>
/// The identity of the person at this browser. A signed-in user comes from the authentication
/// cookie; anybody else gets a name tag kept in localStorage so a refresh comes back as the same
/// guest and can reclaim their seat. The guest tag is not a login and proves nothing — which is
/// why it is only ever accepted in its own shape, and regenerated otherwise.
/// </summary>
public sealed partial class PlayerSession(IJSRuntime js, AuthenticationStateProvider authentication, MatchStore matches, AccountStore accounts) {

	private const string IdKey = "taroking.playerId";
	private const string NameKey = "taroking.playerName";

	private static readonly string[] Adjectives = ["Tihi", "Hitri", "Mirni", "Drzni", "Stari", "Novi"];
	private static readonly string[] Nouns = ["Škis", "Mond", "Pagat", "Kralj", "Tarok", "Valat"];

	private PlayerIdentity _identity = PlayerIdentity.Unknown;
	private IReadOnlySet<string> _blocked = new HashSet<string>();

	public PlayerIdentity Identity => _identity;

	/// <summary>Members this person has blocked; their chat is hidden and they cannot sit at tables this person hosts.</summary>
	public IReadOnlySet<string> Blocked => _blocked;

	public bool IsResolved => _identity.IsKnown;

	[GeneratedRegex("^guest-[0-9a-f]{32}$")]
	private static partial Regex GuestId();

	/// <summary>
	/// Work out who this is. Only callable once the page is interactive — a guest's tag lives in
	/// localStorage, and there is no localStorage during a prerender.
	/// </summary>
	public async Task<PlayerIdentity> ResolveAsync() {
		if (_identity.IsKnown) {
			return _identity;
		}

		AuthenticationState state = await authentication.GetAuthenticationStateAsync();
		ClaimsPrincipal principal = state.User;

		if (principal.Identity?.IsAuthenticated == true && principal.FindFirstValue(ClaimTypes.NameIdentifier) is string userId) {
			TaroKingUser? user = await matches.UserAsync(userId);

			_identity = new PlayerIdentity(PlayerIdentity.MemberId(userId), user?.UserName ?? principal.Identity.Name ?? "?", user?.Rating ?? PlayerIdentity.DefaultRating) {
				UserId = userId,
				MutedUntil = user?.MutedUntil,
				BannedUntil = user?.BannedUntil
			};

			_blocked = await accounts.BlockedIdsAsync(userId);

			return _identity;
		}

		string? id = await GetAsync(IdKey);
		string? name = await GetAsync(NameKey);

		if (id is null || !GuestId().IsMatch(id)) {
			// Anything that is not a guest tag of our own making is replaced, not honoured.
			id = "guest-" + Guid.NewGuid().ToString("n");
			await SetAsync(IdKey, id);
		}

		if (string.IsNullOrWhiteSpace(name) || await accounts.UserNameExistsAsync(name)) {
			name = SuggestName();
			await SetAsync(NameKey, name);
		}

		_identity = new PlayerIdentity(id, name);

		return _identity;
	}

	/// <summary>Reload what the account says about muting, banning and blocks — after a moderator acted, or after the profile changed.</summary>
	public async Task RefreshAsync() {
		if (_identity.UserId is not string userId) {
			return;
		}

		TaroKingUser? user = await matches.UserAsync(userId);
		if (user is null) {
			return;
		}

		_identity = _identity with {
			Name = user.UserName ?? _identity.Name,
			Rating = user.Rating,
			MutedUntil = user.MutedUntil,
			BannedUntil = user.BannedUntil
		};

		_blocked = await accounts.BlockedIdsAsync(userId);
	}

	/// <summary>Guests may rename themselves; a member's name is their account's. Returns null when accepted, or the reason.</summary>
	public async Task<string?> RenameAsync(string name) {
		if (_identity.IsMember) {
			return "Ime člana je ime računa.";
		}

		string trimmed = string.IsNullOrWhiteSpace(name) ? SuggestName() : name.Trim();
		trimmed = trimmed[..Math.Min(trimmed.Length, 20)];

		if (await accounts.UserNameExistsAsync(trimmed)) {
			return "To ime ima registriran igralec.";
		}

		_identity = _identity with { Name = trimmed };
		await SetAsync(NameKey, trimmed);

		return null;
	}

	private static string SuggestName() =>
		$"{Adjectives[Random.Shared.Next(Adjectives.Length)]} {Nouns[Random.Shared.Next(Nouns.Length)]}";

	private async Task<string?> GetAsync(string key) {
		try {
			return await js.InvokeAsync<string?>("localStorage.getItem", key);
		} catch (JSException) {
			return null;
		} catch (InvalidOperationException) {
			// Called too early, before the circuit could talk to the browser.
			return null;
		}
	}

	private async Task SetAsync(string key, string value) {
		try {
			await js.InvokeVoidAsync("localStorage.setItem", key, value);
		} catch (JSException) {
			// A browser that refuses storage still gets to play; it just forgets who it was.
		} catch (InvalidOperationException) {
		}
	}
}
