using Crateside;
using Crateside.Components;
using Crateside.Core;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents().AddInteractiveServerComponents();

builder.AddCratesideConfiguration();
builder.AddCratesideLogging();
builder.Services.AddCrateside();
builder.Services.AddSingleton<ToolsMemory>();

var app = builder.Build();

var respelled = app
    .Services.GetRequiredService<Crateside.Core.Orchestration.Jobs.JobStatusSpelling>()
    .Run();

if (respelled > 0)
    app.Logger.LogInformation("Respelled {Count} job lines to American spelling", respelled);

app.Services.GetRequiredService<Crateside.Core.Ledger.LedgerStore>().Start();

var followed = app.Services.GetRequiredService<Crateside.Core.Tracks.SongKeyMigration>().Run();

if (followed > 0)
    app.Logger.LogInformation(
        "Moved {Count} genre, familiarity and mark records from video IDs to their songs",
        followed
    );
app.Services.GetRequiredService<Crateside.Core.Tracks.CacheKeeper>();

var builds = app.Services.GetRequiredService<KnownBuilds>();
await builds.SeedAsync(CancellationToken.None);

foreach (var filter in Enum.GetValues<FfmpegFilter>())
    app.Logger.LogInformation(
        "{Filter} runs on ffmpeg {Build} at {Path}",
        FfmpegBuild.NameOf(filter),
        builds.Pinned(filter)?.Build.Label ?? "(no pinned build)",
        builds.FileNameFor(filter)
    );

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

app.Run();
