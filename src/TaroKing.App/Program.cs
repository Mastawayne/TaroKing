using TaroKing.App.Components;
using TaroKing.App.Services;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
	.AddInteractiveServerComponents();

// Games outlive the circuit, so refreshing the browser lands back on the same hand.
builder.Services.AddSingleton<LocalGames>();

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
