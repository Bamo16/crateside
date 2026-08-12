using Crateside.Components;
using Crateside.Core;
using Crateside.Core.Queue;
using Crateside.Core.Staging;

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
builder.Services.AddSingleton<QueueService>();

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
