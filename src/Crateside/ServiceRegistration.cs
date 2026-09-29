using Crateside.Core;
using Crateside.Core.Ableton;
using Crateside.Core.Beats;
using Crateside.Core.Familiarity;
using Crateside.Core.Genres;
using Crateside.Core.Ledger;
using Crateside.Core.Library;
using Crateside.Core.Naming;
using Crateside.Core.Orchestration;
using Crateside.Core.Orchestration.Download;
using Crateside.Core.Orchestration.Edits;
using Crateside.Core.Orchestration.Evidence;
using Crateside.Core.Orchestration.Handoff;
using Crateside.Core.Orchestration.Jobs;
using Crateside.Core.Orchestration.Loop;
using Crateside.Core.Orchestration.Separation;
using Crateside.Core.Orchestration.Tagging;
using Crateside.Core.Orchestration.Verdict;
using Crateside.Core.Playlist;
using Crateside.Core.Quota;
using Crateside.Core.Samples;
using Crateside.Core.Staging;
using Crateside.Core.Tracks;
using Crateside.Core.WorkLists;
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
        builder.Services.Configure<PlaylistOptions>(
            builder.Configuration.GetSection(PlaylistOptions.SectionName)
        );

        builder.Services.AddSingleton(sp =>
            sp.GetRequiredService<IOptions<CratesidePaths>>().Value
        );

        return builder;
    }

    /// <summary>Stateless readers over files crateside does not own; safe to share.</summary>
    public static IServiceCollection AddCratesideReaders(this IServiceCollection services)
    {
        services.AddSingleton<AbletonFolderInfoReader>();
        services.AddSingleton<AbletonIndexReader>();
        services.AddSingleton<LibraryKeyReader>();
        services.AddSingleton<LibrarySummary>();
        services.AddSingleton<StagingReader>();
        services.AddSingleton(_ => new WarpTempoReader(
            Environment.SpecialFolder.ApplicationData.GetFolderPath("crateside", "tempos.json")
        ));
        services.AddSingleton(_ => new BeatReader(
            Environment.SpecialFolder.ApplicationData.GetFolderPath("crateside", "beats.json")
        ));
        services.AddSingleton<ISetLinks>(sp => new AbletonSetLinks(
            sp.GetRequiredService<CratesidePaths>(),
            TimeProvider.System
        ));
        services.AddSingleton<PlaylistReader>();
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
                    Environment.SpecialFolder.LocalApplicationData.GetFolderPath(
                        "crateside",
                        "keys"
                    )
                )
            )
            .SetApplicationName("crateside");

        if (OperatingSystem.IsWindows())
            dataProtection.ProtectKeysWithDpapi();

        services.AddSingleton<ProtectedTokenStore>();
        services.AddSingleton<PlaylistWriteAccess>();
        services.AddSingleton<PlaylistAuthorizer>();
        services.AddSingleton<PlaylistRemover>();
        services.AddSingleton<IntakeStore>();
        services.AddSingleton<BumpedTrack>();
        services.AddSingleton(_ => new PresetCatalogReader());

        // Both trailing delegates and the catalog are passed by hand: the container resolves the
        // two ReadReopenedTracks parameters to the same registration, and they mean different
        // sets.
        services.AddSingleton(sp => new QueueService(
            sp.GetRequiredService<IOptions<CratesidePaths>>(),
            sp.GetRequiredService<QueueSettingsStore>(),
            sp.GetRequiredService<PlaylistReader>(),
            sp.GetRequiredService<PlaylistAuthorizer>(),
            sp.GetRequiredService<PlaylistRemover>(),
            sp.GetRequiredService<BumpedTrack>(),
            sp.GetRequiredService<IntakeStore>(),
            ledger =>
                ReopenedTracks
                    .From(sp.GetRequiredService<JobStore>().All(), ledger)
                    .Union(
                        CutRequests.Unsettled(
                            sp.GetRequiredService<QueueSettingsStore>().Read(),
                            sp.GetRequiredService<JobStore>()
                        )
                    )
                    .ToHashSet(StringComparer.Ordinal),
            ledger =>
                ReopenedTracks
                    .AwaitingCut(sp.GetRequiredService<JobStore>().All(), ledger)
                    .Union(
                        CutRequests.Cutting(
                            sp.GetRequiredService<QueueSettingsStore>().Read(),
                            sp.GetRequiredService<JobStore>()
                        )
                    )
                    .ToHashSet(StringComparer.Ordinal),
            sp.GetRequiredService<PresetCatalogReader>().ReadAsync
        ));
        return services;
    }

    /// <summary>
    /// Draining, registered by factory: the container cannot resolve a constructor default like
    /// <c>maxStrikes</c>.
    /// </summary>
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
        // Running unless the user paused it, and a pause of theirs outlives a restart.
        services.AddSingleton(_ => new PauseStore());
        services.AddSingleton(sp => new DrainControl(
            sp.GetRequiredService<PauseStore>().Read().Separation
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

        services.AddSingleton(sp => new VerdictControl(
            sp.GetRequiredService<PauseStore>().Read().Judging
        ));
        services.AddSingleton(sp =>
        {
            var remembered = sp.GetRequiredService<PauseStore>().Read();

            return new WindowRoll(remembered.Backfill, remembered.BackfillJudgedAt);
        });

        services.AddSingleton(sp => new StageSignals(
            sp.GetRequiredService<DrainControl>(),
            sp.GetRequiredService<VerdictControl>()
        ));

        services.AddSingleton(sp => new VerdictSelector(
            sp.GetRequiredService<JobStore>(),
            sp.GetRequiredService<WorkingSet>(),
            resolvedName: videoId =>
                sp.GetRequiredService<ResolvedNameStore>()
                    .Read()
                    .GetValueOrDefault(videoId)
                    ?.DisplayTitle
        ));
        services.AddSingleton(sp => new VerdictRunner(
            sp.GetRequiredService<WorkingSet>(),
            sp.GetRequiredService<CratesidePaths>()
        ));
        services.AddSingleton<VerdictDrainer>();

        services.AddSingleton(sp => new HandoffSelector(
            sp.GetRequiredService<JobStore>(),
            sp.GetRequiredService<CratesidePaths>(),
            sp.GetRequiredService<SeparationInterlock>()
        ));
        services.AddSingleton(_ => DeletionLog.Default());
        services.AddSingleton(_ => ActionLog.Default());
        services.AddSingleton(sp => new HandoffRunner(
            sp.GetRequiredService<CratesidePaths>(),
            sp.GetRequiredService<DeletionLog>()
        ));
        services.AddSingleton<HandoffDrainer>();

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

        services.AddSingleton(sp => new BackingRunner(
            sp.GetRequiredService<AbletonFolderInfoReader>(),
            sp.GetRequiredService<AbletonFolderInfoWriter>()
        ));

        // Factories for the same reason as the rest: the container will not apply the trailing
        // optional path and executable-name defaults.
        services.AddSingleton(_ => new ResolvedNameStore());
        services.AddSingleton(_ => new NameResolver());

        // Not registered beside the pausable stages on purpose (ADR 0014): a name resolve spends
        // one metadata fetch and contends for nothing, so it runs unattended like detection.
        services.AddHostedService<NameWorker>();

        // Unattended for the same reason, and it has to be: the gap opens on every import and is
        // collected by an ordinary hand-applied tag (ADR 0023).
        services.AddHostedService<BackingWorker>();

        // Unattended too: beatdetect runs on the CPU and contends for nothing (#75, #76).
        services.AddHostedService<BeatWorker>();

        services.AddHostedService<DrainWorker>();

        // AddSingleton<IHostedService> rather than AddHostedService: the latter routes through
        // TryAddEnumerable, which de-duplicates by implementation type and would silently
        // discard the second StageWorker.
        services.AddSingleton<IHostedService>(sp => Worker<DownloadDrainer>(sp));
        services.AddSingleton<IHostedService>(sp => Worker<EvidenceDrainer>(sp));
        services.AddSingleton<IHostedService>(sp => Worker<VerdictDrainer>(sp));
        services.AddSingleton<IHostedService>(sp => Worker<HandoffDrainer>(sp));
        services.AddSingleton<IHostedService>(sp => Worker<TaggingDrainer>(sp));

        services.AddHostedService<WindowRollWorker>();

        services.AddSingleton(_ => new SourceProbe());
        services.AddSingleton(sp => new TrackAdd(
            sp.GetRequiredService<QueueSettingsStore>(),
            sp.GetRequiredService<CratesidePaths>(),
            sp.GetRequiredService<SourceProbe>(),
            sp.GetRequiredService<ResolvedNameStore>()
        ));
        services.AddSingleton(sp => new TrackRemoval(
            sp.GetRequiredService<QueueSettingsStore>(),
            sp.GetRequiredService<IntakeStore>(),
            sp.GetRequiredService<WorkingSet>(),
            StagedFiles(sp),
            sp.GetRequiredService<DeletionLog>()
        ));
        services.AddSingleton(sp => new SampleAdoption(
            sp.GetRequiredService<QueueSettingsStore>(),
            sp.GetRequiredService<CratesidePaths>()
        ));
        services.AddSingleton(sp => new SourceAdd(
            sp.GetRequiredService<QueueSettingsStore>(),
            sp.GetRequiredService<CratesidePaths>(),
            sp.GetRequiredService<SourceProbe>()
        ));

        services.AddSingleton(sp => new StemRecut(
            sp.GetRequiredService<QueueSettingsStore>(),
            sp.GetRequiredService<WorkingSet>(),
            StagedFiles(sp),
            sp.GetRequiredService<DeletionLog>()
        ));
        services.AddSingleton(sp => new TrackRename(
            sp.GetRequiredService<QueueSettingsStore>(),
            StagedFiles(sp),
            sp.GetRequiredService<AbletonFolderInfoWriter>(),
            sp.GetRequiredService<CratesidePaths>(),
            videoId =>
                StemTagReader.FilesCutFrom(
                    sp.GetRequiredService<CratesidePaths>().MashupSamples,
                    videoId
                ),
            sp.GetRequiredService<ISetLinks>()
        ));
        services.AddSingleton(sp => new LookalikeAnswer(
            sp.GetRequiredService<QueueSettingsStore>(),
            sp.GetRequiredService<CratesidePaths>()
        ));

        services.AddSingleton(sp => new TrackSwap(
            sp.GetRequiredService<QueueSettingsStore>(),
            sp.GetRequiredService<WorkingSet>(),
            StagedFiles(sp),
            sp.GetRequiredService<CratesidePaths>(),
            deletions: sp.GetRequiredService<DeletionLog>()
        ));

        services.AddSingleton(sp => new WorkingSetJanitor(
            sp.GetRequiredService<WorkingSet>(),
            sp.GetRequiredService<JobStore>(),
            sp.GetRequiredService<IOptions<CratesidePaths>>(),
            videoId =>
                sp.GetRequiredService<QueueSettingsStore>()
                    .Read()
                    .Tracks.FirstOrDefault(track =>
                        track.VideoId.Equals(videoId, StringComparison.Ordinal)
                    )
                    ?.CutRequestedAt
        ));
        services.AddHostedService<JanitorWorker>();
        return services;
    }

    private static StageWorker Worker<TDrainer>(IServiceProvider services)
        where TDrainer : notnull, IStageDrainer =>
        new(
            services.GetRequiredService<TDrainer>(),
            services.GetRequiredService<JobStore>(),
            services.GetRequiredService<ILogger<StageWorker>>(),
            services.GetRequiredService<StageSignals>()
        );

    /// <summary>One track's files as they currently sit in staging, read fresh on every call.</summary>
    private static ReadStagedFiles StagedFiles(IServiceProvider services) =>
        videoId =>
            services
                .GetRequiredService<StagingReader>()
                .Read(services.GetRequiredService<CratesidePaths>())
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

        services.AddSingleton(_ => new GenreStore());
        services
            .AddHttpClient<GenreLookup>(client => client.Timeout = TimeSpan.FromSeconds(15))
            .SetHandlerLifetime(TimeSpan.FromMinutes(5));
        services.AddHostedService<GenreWorker>();

        services.AddSingleton(_ => new FamiliarityStore());
        services
            .AddHttpClient<FamiliarityLookup>(client => client.Timeout = TimeSpan.FromSeconds(15))
            .SetHandlerLifetime(TimeSpan.FromMinutes(5));
        services.AddHostedService<FamiliarityWorker>();

        services.AddSingleton(sp => new TracksService(
            async ct =>
            {
                var scan = await sp.GetRequiredService<QueueService>().ScanAsync(ct);

                var view = TracksView.Compose(
                    scan.View,
                    scan.Ledger,
                    sp.GetRequiredService<StagingReader>()
                        .Read(sp.GetRequiredService<CratesidePaths>()),
                    scan.Processed.StemsByVideoId,
                    sp.GetRequiredService<CratesidePaths>().MashupSamples,
                    sp.GetRequiredService<JobStore>(),
                    scan.Instructions,
                    sp.GetRequiredService<ResolvedNameStore>().Read(),
                    sp.GetRequiredService<WorkingSet>().Acquisitions(),
                    await sp.GetRequiredService<PresetCatalogReader>().ReadAsync(ct),
                    sp.GetRequiredService<AbletonIndexReader>()
                        .Read(sp.GetRequiredService<CratesidePaths>().MashupSamples),
                    sp.GetRequiredService<DeletionLog>().All(),
                    sp.GetRequiredService<GenreStore>().Read(),
                    sp.GetRequiredService<FamiliarityStore>().Read(),
                    sp.GetRequiredService<WarpTempoReader>().Read,
                    sp.GetRequiredService<BeatReader>().Read
                );

                sp.GetRequiredService<WarpTempoReader>().Save();

                return view with
                {
                    ReadAt = scan.ReadAt,
                    PlaylistProblem = scan.PlaylistProblem,
                };
            },
            sp.GetRequiredService<StageHub>(),
            freshness: () => sp.GetRequiredService<QueueSettingsStore>().Read().RefreshInterval
        ));

        return services;
    }
}
