using System.Text;
using System.Threading.RateLimiting;

using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;

using TaroKing.App.Components;
using TaroKing.App.Services;
using TaroKing.Data;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
	.AddInteractiveServerComponents();

// Give the tables a moment to say goodbye and the journal a moment to flush.
builder.Services.Configure<HostOptions>(options => options.ShutdownTimeout = TimeSpan.FromSeconds(20));

// --- the database ---

// A factory rather than a plain scoped context: a Blazor circuit lives as long as the tab does,
// and the stores open a short-lived context per call. The factory also registers the context
// itself as scoped, which is what Identity's stores want in an HTTP request.
builder.Services.AddDbContextFactory<TaroKingDbContext>(options =>
	options.UseSqlite(builder.Configuration.GetConnectionString("TaroKing") ?? "Data Source=taroking.db"));

builder.Services.AddSingleton<MatchStore>();
builder.Services.AddSingleton<AccountStore>();
builder.Services.AddSingleton<LiveTableStore>();

// Cookies and antiforgery tokens must survive a redeploy, so the key ring lives next to the data.
string dataDirectory = Path.Combine(builder.Environment.ContentRootPath, "App_Data");
string keysDirectory = builder.Configuration["DataProtection:KeysPath"] ?? Path.Combine(dataDirectory, "keys");
Directory.CreateDirectory(keysDirectory);
builder.Services.AddDataProtection()
	.SetApplicationName("TaroKing")
	.PersistKeysToFileSystem(new DirectoryInfo(keysDirectory));

// --- accounts ---

builder.Services.AddHttpContextAccessor();

builder.Services.AddIdentityCore<TaroKingUser>(options => {
	options.Password.RequiredLength = 8;
	options.Password.RequireNonAlphanumeric = false;
	options.Password.RequireUppercase = false;
	options.Password.RequireLowercase = false;
	options.Password.RequireDigit = false;
	options.User.RequireUniqueEmail = false;
	options.User.AllowedUserNameCharacters = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-._čšžČŠŽ";
	options.Lockout.MaxFailedAccessAttempts = 5;
	options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
	options.Lockout.AllowedForNewUsers = true;
	options.SignIn.RequireConfirmedEmail = false;
})
	.AddRoles<IdentityRole>()
	.AddEntityFrameworkStores<TaroKingDbContext>()
	.AddSignInManager()
	.AddDefaultTokenProviders();

builder.Services.AddAuthentication(IdentityConstants.ApplicationScheme)
	.AddIdentityCookies();

builder.Services.ConfigureApplicationCookie(options => {
	options.LoginPath = "/prijava";
	options.LogoutPath = "/account/logout";
	options.AccessDeniedPath = "/prijava";
	options.ExpireTimeSpan = TimeSpan.FromDays(30);
	options.SlidingExpiration = true;
});

// A ban changes the security stamp; the cookie is re-checked this often, so a ban bites within minutes.
builder.Services.Configure<SecurityStampValidatorOptions>(options => options.ValidationInterval = TimeSpan.FromMinutes(5));

builder.Services.AddAuthorization(options => {
	options.AddPolicy("Moderation", policy => policy.RequireRole("Admin", "Moderator"));
});
builder.Services.AddCascadingAuthenticationState();

builder.Services.AddSingleton<IMailSender, SmtpMailSender>();

// Who this browser is: a member from the cookie, or a guest name tag from localStorage.
builder.Services.AddScoped<PlayerSession>();

// Login and registration are the doors people kick: ten tries a minute per address.
builder.Services.AddRateLimiter(options => {
	options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
	options.AddPolicy("account", context => RateLimitPartition.GetFixedWindowLimiter(
		context.Connection.RemoteIpAddress?.ToString() ?? "?",
		_ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});

// --- tables ---

builder.Services.AddSingleton<ServerMetrics>();
builder.Services.AddSingleton(provider => ChatFilter.FromFile(Path.Combine(dataDirectory, "badwords.txt")));
builder.Services.AddSingleton<LiveTableJournal>();
builder.Services.AddSingleton<ArchiveQueue>();
builder.Services.AddSingleton<MatchArchiver>();

// Games and tables outlive the circuit, so refreshing the browser lands back on the same hand.
builder.Services.AddSingleton(provider => {
	MatchArchiver archiver = provider.GetRequiredService<MatchArchiver>();
	return new LocalGames(archiver.ArchiveLater);
});
builder.Services.AddSingleton(provider => new TableService(
	provider.GetRequiredService<LiveTableJournal>(),
	provider.GetRequiredService<ChatFilter>(),
	provider.GetRequiredService<ServerMetrics>(),
	builder.Configuration.GetValue("Tables:Max", 200)));

// The table's own clock, the journal writer, the archive worker and the nightly backup.
builder.Services.AddHostedService<TableHeartbeat>();
builder.Services.AddHostedService<LiveTableWriter>();
builder.Services.AddHostedService<ArchiveWorker>();
builder.Services.AddSingleton<SqliteBackupService>();
builder.Services.AddHostedService(provider => provider.GetRequiredService<SqliteBackupService>());

WebApplication app = builder.Build();

// --- the schema ---

await using (AsyncServiceScope scope = app.Services.CreateAsyncScope()) {
	TaroKingDbContext db = scope.ServiceProvider.GetRequiredService<TaroKingDbContext>();

	// Migrations only. A database made by EnsureCreated cannot be upgraded — delete it and start over.
	await db.Database.MigrateAsync();

	// Roles, and the admins named in configuration ("Admins": ["ime"]).
	RoleManager<IdentityRole> roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
	foreach (string role in new[] { "Admin", "Moderator" }) {
		if (!await roles.RoleExistsAsync(role)) {
			await roles.CreateAsync(new IdentityRole(role));
		}
	}

	UserManager<TaroKingUser> users = scope.ServiceProvider.GetRequiredService<UserManager<TaroKingUser>>();
	foreach (string name in app.Configuration.GetSection("Admins").Get<string[]>() ?? []) {
		TaroKingUser? admin = await users.FindByNameAsync(name);
		if (admin is not null && !await users.IsInRoleAsync(admin, "Admin")) {
			await users.AddToRoleAsync(admin, "Admin");
		}
	}
}

// --- the pipeline ---

// Behind a reverse proxy the scheme and the client address come from the proxy's headers.
app.UseForwardedHeaders(new ForwardedHeadersOptions {
	ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
});

if (!app.Environment.IsDevelopment()) {
	app.UseExceptionHandler("/Error", createScopeForErrors: true);
	app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
	.AddInteractiveServerRenderMode();

// A liveness probe for the container: 200 when the app is up.
app.MapGet("/zdravje", () => Results.Text("ok", "text/plain"));

// --- metrics ---
// Prometheus text format, behind a token from configuration ("Metrics": { "Token": "..." }). No token, no endpoint.

app.MapGet("/metrics", (HttpContext context, ServerMetrics metrics, IConfiguration configuration) => {
	string? token = configuration["Metrics:Token"];
	if (string.IsNullOrWhiteSpace(token)) {
		return Results.NotFound();
	}

	string header = context.Request.Headers.Authorization.ToString();
	string given = header.StartsWith("Bearer ", StringComparison.Ordinal)
		? header[7..]
		: context.Request.Query["token"].ToString();

	return given == token
		? Results.Text(metrics.Render(), "text/plain; version=0.0.4; charset=utf-8")
		: Results.Unauthorized();
});

// --- account endpoints ---
// Plain form posts, so the sign-in happens on an HTTP request where the cookie can be written.
// A Blazor circuit cannot set cookies, which is why these are not component methods.

RouteGroupBuilder account = app.MapGroup("/account").RequireRateLimiting("account");

account.MapPost("/login", async ([FromForm] LoginForm form, SignInManager<TaroKingUser> signIn, UserManager<TaroKingUser> users) => {
	string name = form.UserName?.Trim() ?? "";
	string back = Uri.EscapeDataString(SafeReturn(form.ReturnUrl));

	TaroKingUser? user = await users.FindByNameAsync(name);
	if (user is not null && (user.IsDeleted || user.IsBanned(DateTimeOffset.UtcNow))) {
		return Results.LocalRedirect($"/prijava?napaka=prepoved&returnUrl={back}");
	}

	// Fully qualified: Mvc has a SignInResult of its own, and both namespaces are in scope here.
	Microsoft.AspNetCore.Identity.SignInResult result = await signIn.PasswordSignInAsync(
		name,
		form.Password ?? "",
		form.RememberMe,
		lockoutOnFailure: true);

	if (result.Succeeded) {
		return Results.LocalRedirect(SafeReturn(form.ReturnUrl));
	}

	string reason = result.IsLockedOut ? "zaklenjeno" : "1";
	return Results.LocalRedirect($"/prijava?napaka={reason}&returnUrl={back}");
});

account.MapPost("/register", async ([FromForm] RegisterForm form, UserManager<TaroKingUser> users, SignInManager<TaroKingUser> signIn) => {
	string name = form.UserName?.Trim() ?? "";

	if (form.Password != form.Confirm) {
		return Results.LocalRedirect("/registracija?napaka=gesli");
	}

	if (!form.AcceptTerms) {
		return Results.LocalRedirect("/registracija?napaka=pogoji");
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

// Set or change the e-mail; a confirmation link goes out, the address counts once it is clicked.
account.MapPost("/email", async (HttpContext context, [FromForm] EmailForm form, UserManager<TaroKingUser> users, IMailSender mail) => {
	TaroKingUser? user = await users.GetUserAsync(context.User);
	if (user is null) {
		return Results.LocalRedirect("/prijava?returnUrl=/profil");
	}

	string email = form.Email?.Trim() ?? "";
	if (!email.Contains('@') || email.Length > 254) {
		return Results.LocalRedirect("/profil?napaka=email");
	}

	user.Email = email;
	user.EmailConfirmed = false;
	await users.UpdateAsync(user);

	string token = await users.GenerateEmailConfirmationTokenAsync(user);
	string link = $"{context.Request.Scheme}://{context.Request.Host}/account/confirm?userId={Uri.EscapeDataString(user.Id)}&token={Encode(token)}";

	await mail.SendAsync(email, "TaroKing — potrdi e-poštni naslov", $"Živjo, {user.UserName}!\n\nE-poštni naslov potrdiš s klikom na povezavo:\n{link}\n\nČe tega nisi zahteval, sporočilo prezri.");

	return Results.LocalRedirect("/profil?sporocilo=email-poslan");
}).RequireAuthorization();

account.MapGet("/confirm", async (string? userId, string? token, UserManager<TaroKingUser> users) => {
	if (userId is null || token is null) {
		return Results.LocalRedirect("/profil?napaka=potrditev");
	}

	TaroKingUser? user = await users.FindByIdAsync(userId);
	if (user is null) {
		return Results.LocalRedirect("/profil?napaka=potrditev");
	}

	IdentityResult confirmed = await users.ConfirmEmailAsync(user, Decode(token));

	return Results.LocalRedirect(confirmed.Succeeded ? "/profil?sporocilo=email-potrjen" : "/profil?napaka=potrditev");
});

// Forgotten password: the link goes to the confirmed address, and the reply is the same either way.
account.MapPost("/forgot", async (HttpContext context, [FromForm] ForgotForm form, UserManager<TaroKingUser> users, IMailSender mail) => {
	TaroKingUser? user = await users.FindByNameAsync(form.UserName?.Trim() ?? "");

	if (user is not null && user.EmailConfirmed && user.Email is string email && !user.IsDeleted) {
		string token = await users.GeneratePasswordResetTokenAsync(user);
		string link = $"{context.Request.Scheme}://{context.Request.Host}/ponastavi-geslo?userId={Uri.EscapeDataString(user.Id)}&token={Encode(token)}";

		await mail.SendAsync(email, "TaroKing — novo geslo", $"Živjo, {user.UserName}!\n\nNovo geslo nastaviš tukaj (povezava velja eno uro):\n{link}\n\nČe tega nisi zahteval, sporočilo prezri — geslo ostane, kot je.");
	}

	return Results.LocalRedirect("/pozabljeno-geslo?sporocilo=poslano");
});

account.MapPost("/reset", async ([FromForm] ResetForm form, UserManager<TaroKingUser> users) => {
	string userId = Uri.EscapeDataString(form.UserId ?? "");

	if (form.Password != form.Confirm) {
		return Results.LocalRedirect($"/ponastavi-geslo?userId={userId}&token={form.Token}&napaka=gesli");
	}

	TaroKingUser? user = form.UserId is null ? null : await users.FindByIdAsync(form.UserId);
	if (user is null || form.Token is null) {
		return Results.LocalRedirect("/ponastavi-geslo?napaka=povezava");
	}

	IdentityResult reset = await users.ResetPasswordAsync(user, Decode(form.Token), form.Password ?? "");
	if (!reset.Succeeded) {
		string reason = string.Join(" ", reset.Errors.Select(error => error.Description));
		return Results.LocalRedirect($"/ponastavi-geslo?userId={userId}&token={form.Token}&napaka={Uri.EscapeDataString(reason)}");
	}

	return Results.LocalRedirect("/prijava?sporocilo=geslo");
});

account.MapPost("/password", async (HttpContext context, [FromForm] PasswordForm form, UserManager<TaroKingUser> users, SignInManager<TaroKingUser> signIn) => {
	TaroKingUser? user = await users.GetUserAsync(context.User);
	if (user is null) {
		return Results.LocalRedirect("/prijava?returnUrl=/profil");
	}

	if (form.NewPassword != form.Confirm) {
		return Results.LocalRedirect("/profil?napaka=gesli");
	}

	IdentityResult changed = await users.ChangePasswordAsync(user, form.CurrentPassword ?? "", form.NewPassword ?? "");
	if (!changed.Succeeded) {
		string reason = string.Join(" ", changed.Errors.Select(error => error.Description));
		return Results.LocalRedirect($"/profil?napaka={Uri.EscapeDataString(reason)}");
	}

	await signIn.RefreshSignInAsync(user);

	return Results.LocalRedirect("/profil?sporocilo=geslo");
}).RequireAuthorization();

// The right to be forgotten: the password once more, then the account is anonymised and signed out.
account.MapPost("/delete", async (HttpContext context, [FromForm] DeleteForm form, UserManager<TaroKingUser> users, SignInManager<TaroKingUser> signIn, AccountStore accounts) => {
	TaroKingUser? user = await users.GetUserAsync(context.User);
	if (user is null) {
		return Results.LocalRedirect("/prijava");
	}

	if (!await users.CheckPasswordAsync(user, form.Password ?? "") || form.Confirm != "IZBRIŠI") {
		return Results.LocalRedirect("/profil?napaka=izbris");
	}

	await accounts.AnonymiseAsync(user.Id);
	await signIn.SignOutAsync();

	return Results.LocalRedirect("/?sporocilo=izbrisano");
}).RequireAuthorization();

// The right to a copy: everything we hold, as a JSON download.
account.MapGet("/export", async (HttpContext context, UserManager<TaroKingUser> users, AccountStore accounts) => {
	TaroKingUser? user = await users.GetUserAsync(context.User);
	if (user is null) {
		return Results.Unauthorized();
	}

	string json = await accounts.ExportAsync(user.Id);

	return Results.File(Encoding.UTF8.GetBytes(json), "application/json", $"taroking-{user.UserName}.json");
}).RequireAuthorization();

app.Run();

// Only ever send people back somewhere on this site: one leading slash, no scheme, no backslash tricks.
static string SafeReturn(string? returnUrl) =>
	!string.IsNullOrWhiteSpace(returnUrl)
	&& returnUrl.StartsWith('/')
	&& (returnUrl.Length == 1 || returnUrl[1] is not '/' and not '\\')
		? returnUrl
		: "/";

// Identity tokens are long and full of characters URLs dislike; they travel base64url-encoded.
static string Encode(string token) => WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));

static string Decode(string encoded) {
	try {
		return Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(encoded));
	} catch (FormatException) {
		return "";
	}
}

/// <summary>What the login form posts.</summary>
public sealed record LoginForm(string? UserName, string? Password, bool RememberMe, string? ReturnUrl);

/// <summary>What the registration form posts.</summary>
public sealed record RegisterForm(string? UserName, string? Password, string? Confirm, bool AcceptTerms, string? ReturnUrl);

public sealed record LogoutForm(string? ReturnUrl);

public sealed record EmailForm(string? Email);

public sealed record ForgotForm(string? UserName);

public sealed record ResetForm(string? UserId, string? Token, string? Password, string? Confirm);

public sealed record PasswordForm(string? CurrentPassword, string? NewPassword, string? Confirm);

public sealed record DeleteForm(string? Password, string? Confirm);
