using System.Security.Claims;

using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.JSInterop;

using TaroKing.Data;

namespace TaroKing.App.Services;

/// <summary>
/// Who somebody is at a table. A registered player carries their user id and their real rating;
/// a guest carries a name tag the browser remembers and the default rating, and never gets rated.
/// </summary>
public sealed record PlayerIdentity(string Id, string Name, int Rating = PlayerIdentity.DefaultRating) {

	public const int DefaultRating = TaroKingUser.StartingRating;

	public static PlayerIdentity Unknown { get; } = new("", "Gost");

	/// <summary>The Identity user id for a registered player; null for a guest.</summary>
	public string? UserId { get; init; }

	public bool IsKnown => !string.IsNullOrWhiteSpace(Id);

	public bool IsMember => UserId is not null;
}

/// <summary>
/// The identity of the person at this browser. A signed-in user comes from the authentication
/// cookie; anybody else gets a name tag kept in localStorage so a refresh comes back as the same
/// guest and can reclaim their seat. The guest tag is not a login and proves nothing.
/// </summary>
public sealed class PlayerSession(IJSRuntime js, AuthenticationStateProvider authentication, MatchStore matches) {

	private const string IdKey = "taroking.playerId";
	private const string NameKey = "taroking.playerName";

	private static readonly string[] Adjectives = ["Tihi", "Hitri", "Mirni", "Drzni", "Stari", "Novi"];
	private static readonly string[] Nouns = ["Škis", "Mond", "Pagat", "Kralj", "Tarok", "Valat"];

	private PlayerIdentity _identity = PlayerIdentity.Unknown;

	public PlayerIdentity Identity => _identity;

	public bool IsResolved => _identity.IsKnown;

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

			_identity = new PlayerIdentity(userId, user?.UserName ?? principal.Identity.Name ?? "?", user?.Rating ?? PlayerIdentity.DefaultRating) {
				UserId = userId
			};

			return _identity;
		}

		string? id = await GetAsync(IdKey);
		string? name = await GetAsync(NameKey);

		if (string.IsNullOrWhiteSpace(id)) {
			id = "guest-" + Guid.NewGuid().ToString("n");
			await SetAsync(IdKey, id);
		}

		if (string.IsNullOrWhiteSpace(name)) {
			name = SuggestName();
			await SetAsync(NameKey, name);
		}

		_identity = new PlayerIdentity(id, name);

		return _identity;
	}

	/// <summary>Guests may rename themselves; a member's name is their account's.</summary>
	public async Task RenameAsync(string name) {
		if (_identity.IsMember) {
			return;
		}

		string trimmed = string.IsNullOrWhiteSpace(name) ? SuggestName() : name.Trim();
		trimmed = trimmed[..Math.Min(trimmed.Length, 20)];

		_identity = _identity with { Name = trimmed };
		await SetAsync(NameKey, trimmed);
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
