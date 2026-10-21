using Crateside;
using Crateside.Components;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents().AddInteractiveServerComponents();

builder.AddCratesideConfiguration();
builder.Services.AddCrateside();
builder.Services.AddSingleton<ToolsMemory>();

var app = builder.Build();

app.Services.GetRequiredService<Crateside.Core.Ledger.LedgerStore>().Start();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

app.Run();
