using Crateside.Components;
using Crateside.Core;
using Crateside.Core.Staging;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents().AddInteractiveServerComponents();

builder.Services.Configure<CratesidePaths>(
    builder.Configuration.GetSection(CratesidePaths.SectionName)
);

// All stateless readers over files crateside does not own — safe to share.
builder.Services.AddSingleton<StemTagReader>();
builder.Services.AddSingleton<AbletonFolderInfoReader>();
builder.Services.AddSingleton<StagingBoard>();
builder.Services.AddSingleton<StagingBoardService>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

app.Run();
