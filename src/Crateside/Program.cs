using Crateside;
using Crateside.Components;
using Crateside.Core;
using Crateside.Core.Ledger;
using Crateside.Core.Orchestration;
using Crateside.Core.Queue;
using Crateside.Core.Staging;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents().AddInteractiveServerComponents();

// The API key comes from user-secrets, which the Development environment loads by default —
// it lives outside the working tree so it cannot surface in an editor or a file tree.
// appsettings.Secrets.json stays supported as a gitignored fallback for a non-dev run.
builder.Configuration.AddJsonFile("appsettings.Secrets.json", optional: true, reloadOnChange: true);

if (!builder.Environment.IsDevelopment())
    builder.Configuration.AddUserSecrets<Program>(optional: true);

builder.Services.Configure<CratesidePaths>(
    builder.Configuration.GetSection(CratesidePaths.SectionName)
);
builder.Services.Configure<QueueOptions>(
    builder.Configuration.GetSection(QueueOptions.SectionName)
);

// All stateless readers over files crateside does not own — safe to share.
builder.Services.AddSingleton<StemTagReader>();
builder.Services.AddSingleton<AbletonFolderInfoReader>();
builder.Services.AddSingleton<StagingBoard>();
builder.Services.AddSingleton<StagingBoardService>();
builder.Services.AddSingleton<YouTubeQueueReader>();
builder.Services.AddSingleton<EvidenceService>();
builder.Services.AddSingleton<QueueSettingsStore>();

// The OAuth refresh token can delete from the playlist, so it is encrypted at rest. The key
// ring is sealed to the current Windows user (docs/secrets.md).
var dataProtection = builder
    .Services.AddDataProtection()
    .PersistKeysToFileSystem(
        new DirectoryInfo(
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "crateside",
                "keys"
            )
        )
    )
    .SetApplicationName("crateside");

// DPAPI is Windows-only. crateside is a Windows app (the Ableton roots are P:\ paths), so
// this always applies in practice; the guard is here because the analyzer is right that the
// call is not portable, and falling back to the default key protection is the honest
// alternative rather than suppressing the warning.
if (OperatingSystem.IsWindows())
    dataProtection.ProtectKeysWithDpapi();

builder.Services.AddSingleton<ProtectedTokenStore>();
builder.Services.AddSingleton<QueueAuthorizer>();
builder.Services.AddSingleton<QueueRemover>();
builder.Services.AddSingleton<QueueService>();

// Orchestration. The job store is the one piece of non-derived state in the system, so it is a
// singleton with a single writer -- the drain worker below.
builder.Services.AddSingleton<JobStore>(_ => new JobStore());
builder.Services.AddSingleton<IProcessProbe, SystemProcessProbe>();
builder.Services.AddSingleton<SeparationInterlock>();
builder.Services.AddSingleton<SeparationSelector>(sp => new SeparationSelector(
    sp.GetRequiredService<JobStore>()
));
builder.Services.AddSingleton<SeparationRunner>(sp => new SeparationRunner(
    sp.GetRequiredService<SeparationInterlock>(),
    sp.GetRequiredService<IOptions<CratesidePaths>>().Value
));
builder.Services.Configure<DrainOptions>(
    builder.Configuration.GetSection(DrainOptions.SectionName)
);
builder.Services.AddSingleton<DrainControl>(sp => new DrainControl(
    sp.GetRequiredService<IOptions<DrainOptions>>().Value.StartPaused
));
builder.Services.AddSingleton<SeparationDrainer>();

// Draining is on by default: two separations at once are ruinous, but one is unnoticeable, so
// there is no moment for the user to pick. Pause is in the Queue view for when the room is
// being recorded in.
builder.Services.AddHostedService<DrainWorker>();

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
