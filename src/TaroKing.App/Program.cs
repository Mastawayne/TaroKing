using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

using TaroKing.App.Components;
using TaroKing.App.Services;
using TaroKing.Data;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
	.AddInteractiveServerComponents();

// --- the database ---

// A factory rather than a plain scoped context: a Blazor circuit lives as long as the tab does,
// and the match store opens a short-lived context per call. The factory also registers the
// context itself as scoped, which is what Identity's stores want in an HTTP request.
builder.Services.AddDbContextFactory<TaroKingDbContext>(options =>
	options.UseSqlite(builder.Configuration.GetConnectionString("TaroKing") ?? "Data Source=taroking.db"));

builder.Services.AddSingleton<MatchStore>();
builder.Services.AddSingleton<MatchArchiver>();

// --- accounts ---

builder.Services.AddHttpContextAccessor();

builder.Services.AddIdentityCore<TaroKingUser>(options => {
	// A card game, not a bank: a memorable password is enough.
	options.Password.RequiredLength = 6;
	options.Password.RequireNonAlphanumeric = false;
	options.Password.RequireUppercase = false;
	options.Password.RequireLowercase = false;
	options.Password.RequireDigit = false;
	options.User.RequireUniqueEmail = false;
	options.User.AllowedUserNameCharacters = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-._čšžČŠŽ";
})
	.AddEntityFrameworkStores<TaroKingDbContext>()
	.AddSignInManager();

builder.Services.AddAuthentication(IdentityConstants.ApplicationScheme)
	.AddIdentityCookies();

builder.Services.ConfigureApplicationCookie(options => {
	options.LoginPath = "/prijava";
	options.LogoutPath = "/account/logout";
	options.ExpireTimeSpan = TimeSpan.FromDays(30);
	options.SlidingExpiration = true;
});

builder.Services.AddAuthorization();
builder.Services.AddCascadingAuthenticationState();

// Who this browser is: a member from the cookie, or a guest name tag from localStorage.
builder.Services.AddScoped<PlayerSession>();

// --- tables ---

// Games and tables outlive the circuit, so refreshing the browser lands back on the same hand.
builder.Services.AddSingleton(provider => {
	MatchArchiver archiver = provider.GetRequiredService<MatchArchiver>();
	return new LocalGames(archiver.ArchiveLater);
});
builder.Services.AddSingleton<TableService>();

// The table's own clock: bots move and seats change hands whether or not anybody is watching.
builder.Services.AddHostedService<TableHeartbeat>();

WebApplication app = builder.Build();

// --- the schema ---

await using (AsyncServiceScope scope = app.Services.CreateAsyncScope()) {
	TaroKingDbContext db = scope.ServiceProvider.GetRequiredService<TaroKingDbContext>();
	ILogger<Program> log = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

	if (db.Database.GetMigrations().Any()) {
		await db.Database.MigrateAsync();
	} else {
		// No migration has been generated yet: build the schema straight from the model so the app
		// still runs. Generate one before shipping — see Tasks.md, Phase 13.
		log.LogWarning("No EF migrations found; creating the schema from the model. Run 'dotnet ef migrations add Initial' before deploying.");
		await db.Database.EnsureCreatedAsync();
	}
}

// --- the pipeline ---

if (!app.Environment.IsDevelopment()) {
	app.UseExceptionHandler("/Error", createScopeForErrors: true);
	app.UseHsts();
}

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
	.AddInteractiveServerRenderMode();

// --- account endpoints ---
// Plain form posts, so the sign-in happens on an HTTP request where the cookie can be written.
// A Blazor circuit cannot set cookies, which is why these are not component methods.

RouteGroupBuilder account = app.MapGroup("/account");

account.MapPost("/login", async ([FromForm] LoginForm form, SignInManager<TaroKingUser> signIn) => {
	// Fully qualified: Mvc has a SignInResult of its own, and both namespaces are in scope here.
	Microsoft.AspNetCore.Identity.SignInResult result = await signIn.PasswordSignInAsync(
		form.UserName?.Trim() ?? "",
		form.Password ?? "",
		form.RememberMe,
		lockoutOnFailure: false);

	return result.Succeeded
		? Results.LocalRedirect(SafeReturn(form.ReturnUrl))
		: Results.LocalRedirect($"/prijava?napaka=1&returnUrl={Uri.EscapeDataString(SafeReturn(form.ReturnUrl))}");
});

account.MapPost("/register", async ([FromForm] RegisterForm form, UserManager<TaroKingUser> users, SignInManager<TaroKingUser> signIn) => {
	string name = form.UserName?.Trim() ?? "";

	if (form.Password != form.Confirm) {
		return Results.LocalRedirect("/registracija?napaka=gesli");
	}

	TaroKingUser user = new() { UserName = name };
	IdentityResult created = await users.CreateAsync(user, form.Password ?? "");

	if (!created.Succeeded) {
		string reason = string.Join(" ", created.Errors.Select(error => error.Description));
		return Results.LocalRedirect($"/registracija?napaka={Uri.EscapeDataString(reason)}");
	}

	await signIn.SignInAsync(user, isPersistent: true);

	return Results.LocalRedirect(SafeReturn(form.ReturnUrl));
});

account.MapPost("/logout", async ([FromForm] LogoutForm form, SignInManager<TaroKingUser> signIn) => {
	await signIn.SignOutAsync();

	return Results.LocalRedirect(SafeReturn(form.ReturnUrl));
});

app.Run();

// Only ever send people back somewhere on this site.
static string SafeReturn(string? returnUrl) =>
	!string.IsNullOrWhiteSpace(returnUrl) && returnUrl.StartsWith('/') && !returnUrl.StartsWith("//", StringComparison.Ordinal)
		? returnUrl
		: "/";

/// <summary>What the login form posts.</summary>
public sealed record LoginForm(string? UserName, string? Password, bool RememberMe, string? ReturnUrl);

/// <summary>What the registration form posts.</summary>
public sealed record RegisterForm(string? UserName, string? Password, string? Confirm, string? ReturnUrl);

public sealed record LogoutForm(string? ReturnUrl);
