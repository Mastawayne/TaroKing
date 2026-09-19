using TaroKing.App.Components;
using TaroKing.App.Services;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
	.AddInteractiveServerComponents();

// Games and tables outlive the circuit, so refreshing the browser lands back on the same hand.
builder.Services.AddSingleton<LocalGames>();
builder.Services.AddSingleton<TableService>();

// The table's own clock: bots move and seats change hands whether or not anybody is watching.
builder.Services.AddHostedService<TableHeartbeat>();

// Who this browser is. Not a login — a name tag kept in localStorage until accounts arrive.
builder.Services.AddScoped<PlayerSession>();

WebApplication app = builder.Build();

if (!app.Environment.IsDevelopment()) {
	app.UseExceptionHandler("/Error", createScopeForErrors: true);
	app.UseHsts();
}

app.UseHttpsRedirection();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
	.AddInteractiveServerRenderMode();

app.Run();
