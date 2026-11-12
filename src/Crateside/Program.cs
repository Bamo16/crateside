using Crateside;
using Crateside.Api;
using Crateside.Components;
using Crateside.Core;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents().AddInteractiveServerComponents();

builder.AddCratesideConfiguration();
builder.Services.AddCrateside();
builder.Services.AddSingleton<ToolsMemory>();
builder.Services.AddCratesideApi();

if (DocumentBuild.IsRunning)
    builder.Services.WithoutWorkers();
else
    builder.AddCratesideLogging();

// An instance that only shows state it was given: nothing downloads, separates, judges or looks
// anything up (ADR 0074).
if (builder.Configuration.GetValue<bool>("Workers:Disabled"))
    builder.Services.WithoutWorkers();

var app = builder.Build();

if (!DocumentBuild.IsRunning)
    await StartAsync(app);

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
app.UseCratesideApiBindingErrors();
app.MapCratesideApi();
app.MapCoverArt();
app.MapAudio();

app.Run();

// What each start does before serving: respell, back up and migrate the stores, import the person's decisions, seed the builds.
static async Task StartAsync(WebApplication app)
{
    var respelled = app
        .Services.GetRequiredService<Crateside.Core.Orchestration.Jobs.JobStatusSpelling>()
        .Run();

    if (respelled > 0)
        app.Logger.LogInformation("Respelled {Count} job lines to American spelling", respelled);

    app.Services.GetRequiredService<Crateside.Core.Ledger.LedgerStore>().Start();

    foreach (
        var file in app.Services.GetRequiredService<Crateside.Core.Ledger.DecisionsImport>().Run()
    )
        app.Logger.LogInformation(
            "Decisions import: {File} {Outcome}, {Rows} rows",
            file.File,
            file.Outcome,
            file.Rows
        );

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
}
