using Crateside.Core;
using Crateside.Core.Ledger;
using Crateside.Core.Library;
using Crateside.Core.Naming;
using Crateside.Core.Orchestration;
using Crateside.Core.Queue;
using Crateside.Core.Quota;
using Crateside.Core.Staging;
using Crateside.Core.Tracks;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;

namespace Crateside;

public static class ServiceRegistration
{
    /// <summary>
    /// Configuration sources and the options bound from them.
    /// </summary>
    public static WebApplicationBuilder AddCratesideConfiguration(
        this WebApplicationBuilder builder
    )
    {
        // The Development environment loads user-secrets automatically; this adds it
        // explicitly for other environments.
        builder.Configuration.AddJsonFile(
            "appsettings.Secrets.json",
            optional: true,
            reloadOnChange: true
        );

        if (!builder.Environment.IsDevelopment())
            builder.Configuration.AddUserSecrets<Program>(optional: true);

        builder.Services.Configure<CratesidePaths>(
            builder.Configuration.GetSection(CratesidePaths.SectionName)
        );
        builder.Services.Configure<QueueOptions>(
            builder.Configuration.GetSection(QueueOptions.SectionName)
        );
        builder.Services.Configure<DrainOptions>(
            builder.Configuration.GetSection(DrainOptions.SectionName)
        );

        builder.Services.AddSingleton(sp =>
            sp.GetRequiredService<IOptions<CratesidePaths>>().Value
        );
        builder.Services.AddSingleton(sp => sp.GetRequiredService<IOptions<DrainOptions>>().Value);

        return builder;
    }

    /// <summary>Stateless readers over files crateside does not own; safe to share.</summary>
    public static IServiceCollection AddCratesideReaders(this IServiceCollection services)
    {
        services.AddSingleton<AbletonFolderInfoReader>();
        services.AddSingleton<AbletonIndexReader>();
        services.AddSingleton<LibraryKeyReader>();
        services.AddSingleton<LibrarySummary>();
        services.AddSingleton<StagingBoard>();
        services.AddSingleton<StagingBoardService>();
        services.AddSingleton<YouTubeQueueReader>();
        services.AddSingleton<EvidenceService>();
        services.AddSingleton<QueueSettingsStore>();
        return services;
    }

    /// <summary>
    /// The YouTube queue, and the encrypted store its credential lives in.
    /// </summary>
    public static IServiceCollection AddCratesideQueue(this IServiceCollection services)
    {
        // The key ring is sealed to the current Windows user (docs/secrets.md).
        var dataProtection = services
            .AddDataProtection()
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

        if (OperatingSystem.IsWindows())
            dataProtection.ProtectKeysWithDpapi();

        services.AddSingleton<ProtectedTokenStore>();
        services.AddSingleton<QueueAuthorizer>();
        services.AddSingleton<QueueRemover>();
        services.AddSingleton<BumpedTrack>();
        services.AddSingleton(_ => new PresetCatalogReader());

        // Both trailing delegates and the catalog are passed by hand: the container resolves the
        // two ReadReopenedTracks parameters to the same registration, and they mean different
        // sets.
        services.AddSingleton(sp => new QueueService(
            sp.GetRequiredService<IOptions<CratesidePaths>>(),
            sp.GetRequiredService<QueueSettingsStore>(),
            sp.GetRequiredService<YouTubeQueueReader>(),
            sp.GetRequiredService<QueueAuthorizer>(),
            sp.GetRequiredService<QueueRemover>(),
            sp.GetRequiredService<BumpedTrack>(),
            ledger => ReopenedTracks.From(sp.GetRequiredService<JobStore>().All(), ledger),
            ledger => ReopenedTracks.AwaitingCut(sp.GetRequiredService<JobStore>().All(), ledger),
            sp.GetRequiredService<PresetCatalogReader>().ReadAsync
        ));
        return services;
    }

    /// <summary>
    /// Draining: what turns the queue into stems on disk without anyone asking it to.
    /// </summary>
    /// <remarks>
    /// The registrations below use factories rather than <c>AddSingleton&lt;T&gt;</c> because the
    /// built-in container does not apply constructor defaults for trailing primitives like
    /// <c>maxStrikes</c> or <c>startPaused</c> — it tries to resolve them from the graph and fails.
    /// </remarks>
    public static IServiceCollection AddCratesideOrchestration(this IServiceCollection services)
    {
        services.AddSingleton<StageHub>();
        services.AddSingleton(sp => new JobStore(hub: sp.GetRequiredService<StageHub>()));
        services.AddSingleton<IProcessProbe, SystemProcessProbe>();
        services.AddSingleton<SeparationInterlock>();
        services.AddSingleton(sp => new SeparationSelector(sp.GetRequiredService<JobStore>()));
        services.AddSingleton(sp => new SeparationRunner(
            sp.GetRequiredService<SeparationInterlock>(),
            sp.GetRequiredService<WorkingSet>(),
            sp.GetRequiredService<CratesidePaths>()
        ));
        services.AddSingleton(sp => new DrainControl(
            sp.GetRequiredService<DrainOptions>().StartPaused
        ));
        services.AddSingleton<SeparationDrainer>();

        services.AddSingleton(_ => new WorkingSet());

        services.AddSingleton(_ => new DownloadRunner());
        services.AddSingleton(sp => new DownloadSelector(
            sp.GetRequiredService<JobStore>(),
            sp.GetRequiredService<WorkingSet>(),
            sp.GetRequiredService<CratesidePaths>()
        ));
        services.AddSingleton<DownloadDrainer>();

        services.AddSingleton(sp => new EvidenceSelector(
            sp.GetRequiredService<JobStore>(),
            sp.GetRequiredService<WorkingSet>()
        ));
        services.AddSingleton(sp => new EvidenceRunner(
            sp.GetRequiredService<WorkingSet>(),
            sp.GetRequiredService<CratesidePaths>()
        ));
        services.AddSingleton<EvidenceDrainer>();

        // Starts paused: launching the app should never itself be the act that spends quota.
        services.AddSingleton(_ => new VerdictControl());

        services.AddSingleton(sp => new VerdictSelector(
            sp.GetRequiredService<JobStore>(),
            sp.GetRequiredService<WorkingSet>()
        ));
        services.AddSingleton(sp => new VerdictRunner(
            sp.GetRequiredService<WorkingSet>(),
            sp.GetRequiredService<CratesidePaths>()
        ));
        services.AddSingleton<VerdictDrainer>();

        services.AddSingleton(sp => new HandoffSelector(
            sp.GetRequiredService<JobStore>(),
            sp.GetRequiredService<CratesidePaths>()
        ));
        services.AddSingleton(sp => new HandoffRunner(sp.GetRequiredService<CratesidePaths>()));
        services.AddSingleton<HandoffDrainer>();

        // Writes Sample Staging's tag store only, never Mashup Samples (ADR 0003).
        services.AddSingleton<AbletonFolderInfoWriter>();
        services.AddSingleton(sp => new TaggingSelector(
            sp.GetRequiredService<JobStore>(),
            sp.GetRequiredService<CratesidePaths>()
        ));
        services.AddSingleton(sp => new TaggingRunner(
            sp.GetRequiredService<CratesidePaths>(),
            sp.GetRequiredService<AbletonFolderInfoWriter>()
        ));
        services.AddSingleton<TaggingDrainer>();

        // Factories for the same reason as the rest: the container will not apply the trailing
        // optional path and executable-name defaults.
        services.AddSingleton(_ => new ResolvedNameStore());
        services.AddSingleton(_ => new NameResolver());

        // Not registered beside the pausable stages on purpose (ADR 0014): a name resolve spends
        // one metadata fetch and contends for nothing, so it runs unattended like detection.
        services.AddHostedService<NameWorker>();

        services.AddHostedService<DrainWorker>();

        // AddSingleton<IHostedService> rather than AddHostedService: the latter routes through
        // TryAddEnumerable, which de-duplicates by implementation type and would silently
        // discard the second StageWorker.
        services.AddSingleton<IHostedService>(sp => new StageWorker(
            sp.GetRequiredService<DownloadDrainer>(),
            sp.GetRequiredService<JobStore>(),
            sp.GetRequiredService<ILogger<StageWorker>>()
        ));
        services.AddSingleton<IHostedService>(sp => new StageWorker(
            sp.GetRequiredService<EvidenceDrainer>(),
            sp.GetRequiredService<JobStore>(),
            sp.GetRequiredService<ILogger<StageWorker>>()
        ));
        services.AddSingleton<IHostedService>(sp => new StageWorker(
            sp.GetRequiredService<VerdictDrainer>(),
            sp.GetRequiredService<JobStore>(),
            sp.GetRequiredService<ILogger<StageWorker>>(),
            sp.GetRequiredService<VerdictControl>()
        ));
        services.AddSingleton<IHostedService>(sp => new StageWorker(
            sp.GetRequiredService<HandoffDrainer>(),
            sp.GetRequiredService<JobStore>(),
            sp.GetRequiredService<ILogger<StageWorker>>()
        ));
        services.AddSingleton<IHostedService>(sp => new StageWorker(
            sp.GetRequiredService<TaggingDrainer>(),
            sp.GetRequiredService<JobStore>(),
            sp.GetRequiredService<ILogger<StageWorker>>()
        ));

        services.AddSingleton(_ => new SourceProbe());
        services.AddSingleton(sp => new TrackAdd(
            sp.GetRequiredService<QueueSettingsStore>(),
            sp.GetRequiredService<CratesidePaths>(),
            sp.GetRequiredService<SourceProbe>()
        ));
        services.AddSingleton(sp => new SourceAdd(
            sp.GetRequiredService<QueueSettingsStore>(),
            sp.GetRequiredService<CratesidePaths>(),
            sp.GetRequiredService<SourceProbe>()
        ));

        services.AddSingleton(sp => new StemRecut(
            sp.GetRequiredService<QueueSettingsStore>(),
            sp.GetRequiredService<JobStore>(),
            sp.GetRequiredService<WorkingSet>(),
            StagedFiles(sp)
        ));
        services.AddSingleton(sp => new TrackRename(
            sp.GetRequiredService<QueueSettingsStore>(),
            StagedFiles(sp),
            sp.GetRequiredService<AbletonFolderInfoWriter>(),
            sp.GetRequiredService<CratesidePaths>()
        ));
        services.AddSingleton(sp => new TrackSwap(
            sp.GetRequiredService<QueueSettingsStore>(),
            sp.GetRequiredService<JobStore>(),
            sp.GetRequiredService<WorkingSet>(),
            StagedFiles(sp),
            sp.GetRequiredService<CratesidePaths>()
        ));

        services.AddSingleton<WorkingSetJanitor>();
        services.AddHostedService<JanitorWorker>();
        return services;
    }

    /// <summary>One track's files as they currently sit in staging, read fresh on every call.</summary>
    private static ReadStagedFiles StagedFiles(IServiceProvider services) =>
        videoId =>
            services
                .GetRequiredService<StagingBoardService>()
                .Read()
                .FirstOrDefault(song => song.SourceVideoId == videoId)
                ?.Stems
            ?? [];

    /// <summary>
    /// The Tracks page's view of everything, and the cache that keeps it affordable.
    /// </summary>
    public static IServiceCollection AddCratesideTracks(this IServiceCollection services)
    {
        // QuotaReader sets its own authorization and beta headers per call; this configures
        // transport only.
        services
            .AddHttpClient<QuotaReader>(client =>
            {
                client.BaseAddress = new Uri("https://api.anthropic.com");
                client.Timeout = TimeSpan.FromSeconds(5);
            })
            .SetHandlerLifetime(TimeSpan.FromMinutes(5));

        services.AddSingleton(sp => new TracksService(
            async cancellationToken =>
            {
                var scan = await sp.GetRequiredService<QueueService>().ScanAsync(cancellationToken);

                var view = TracksView.Compose(
                    scan.View,
                    scan.Ledger,
                    sp.GetRequiredService<StagingBoardService>().Read(),
                    scan.Processed.StemsByVideoId,
                    sp.GetRequiredService<CratesidePaths>().MashupSamples,
                    sp.GetRequiredService<JobStore>(),
                    scan.Instructions,
                    sp.GetRequiredService<ResolvedNameStore>().Read(),
                    sp.GetRequiredService<WorkingSet>().Acquisitions(),
                    await sp.GetRequiredService<PresetCatalogReader>().ReadAsync(cancellationToken)
                );

                return view with
                {
                    ReadAt = scan.ReadAt,
                };
            },
            sp.GetRequiredService<StageHub>(),
            freshness: () => sp.GetRequiredService<QueueSettingsStore>().Read().RefreshInterval
        ));

        return services;
    }
}
