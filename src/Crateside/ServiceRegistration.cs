using AbleKit.Tags;
using Crateside.Core;
using Crateside.Core.Ableton;
using Crateside.Core.Beats;
using Crateside.Core.Familiarity;
using Crateside.Core.Genres;
using Crateside.Core.KeyDetection;
using Crateside.Core.Ledger;
using Crateside.Core.Library;
using Crateside.Core.Loudness;
using Crateside.Core.Marks;
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
using Crateside.Core.Tempo;
using Crateside.Core.Tracks;
using Crateside.Core.Warps;
using Crateside.Core.Watching;
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

        builder.Configuration.AddJsonFile(PathOverrideStore.DefaultPath, optional: true);

        builder.Services.Configure<CratesidePaths>(
            builder.Configuration.GetSection(CratesidePaths.SectionName)
        );
        builder.Services.Configure<PlaylistOptions>(
            builder.Configuration.GetSection(PlaylistOptions.SectionName)
        );
        builder.Services.AddSingleton<JudgeSettingsStore>();
        builder
            .Services.AddOptions<JudgeOptions>()
            .Bind(builder.Configuration.GetSection(JudgeOptions.SectionName))
            .PostConfigure<JudgeSettingsStore>(
                (options, store) =>
                {
                    if (store.ReadEar() is { } ear)
                        options.Ear = ear;
                }
            );
        builder.Services.Configure<LoudnessOptions>(
            builder.Configuration.GetSection(LoudnessOptions.SectionName)
        );
        builder.Services.AddSingleton<WarpSettingsStore>();
        builder
            .Services.AddOptions<WarpOptions>()
            .Bind(builder.Configuration.GetSection(WarpOptions.SectionName))
            .PostConfigure<WarpSettingsStore>(
                (options, store) =>
                {
                    if (store.Read() is { } chosen)
                        options.StarterWarps = chosen;
                }
            );

        builder.Services.AddSingleton(sp =>
            sp.GetRequiredService<IOptions<CratesidePaths>>().Value
        );

        return builder;
    }

    /// <summary>Stateless readers over files crateside does not own; safe to share.</summary>
    public static IServiceCollection AddCratesideReaders(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<LedgerStore>();
        services.AddSingleton<FolderInfoReader>();
        services.AddSingleton(sp => new FileIndexReader(
            sp.GetRequiredService<IOptions<CratesidePaths>>().Value.AbletonIndex
        ));
        services.AddSingleton<LibraryKeyReader>();
        services.AddSingleton<LibrarySummary>();
        services.AddSingleton<StagingReader>();
        services.AddSingleton(sp => new WarpTempoReader(
            sp.GetRequiredService<CratesidePaths>().TempoCache,
            sp.GetRequiredService<WrittenWarps>().IsStarter
        ));
        // hpss matched an AI drum stem on the instrumental, measured 2026-09-30 (#83).
        services.AddSingleton<KnownBuilds>();
        services.AddSingleton(sp => new BeatReader(
            sp.GetRequiredService<CratesidePaths>().BeatCache,
            sp.GetRequiredService<CratesidePaths>().BeatdetectFfmpeg,
            "beatdetect=hpss=1",
            sp.GetRequiredService<KnownBuilds>()
        ));
        services.AddSingleton<ISetLinks, AbletonSetLinks>();
        services.AddSingleton<PlaylistReader>();
        services.AddSingleton<QueueFile>();
        services.AddSingleton<PlaylistSettingsStore>();
        services.AddSingleton<TrackInstructionStore>();
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
        services.AddSingleton<PresetCatalogReader>();

        services.AddSingleton<IReopenedWork, ReopenedWork>();
        services.AddSingleton<CachedPlaylist>();
        services.AddSingleton<PlaylistConsumer>();
        services.AddSingleton<WorkListService>();
        return services;
    }

    /// <summary>Draining, and the stores and runners each stage reads and writes through.</summary>
    public static IServiceCollection AddCratesideOrchestration(this IServiceCollection services)
    {
        services.AddSingleton<StageHub>();
        services.AddSingleton<JobStore>();
        services.AddSingleton<IProcessProbe, SystemProcessProbe>();
        services.AddSingleton<SeparationInterlock>();
        services.AddSingleton<SeparationSelector>();
        services.AddSingleton<SeparationRunner>();
        // Running unless the user paused it, and a pause of theirs outlives a restart.
        services.AddSingleton<FolderBrowser>();
        services.AddSingleton<PathOverrideStore>();
        services.AddSingleton<PauseStore>();
        services.AddSingleton(sp => new DrainControl(
            sp.GetRequiredService<PauseStore>().Read().Separation
        ));
        services.AddSingleton<SeparationDrainer>();

        services.AddSingleton<WorkingSet>();

        services.AddSingleton<DownloadRunner>();
        services.AddSingleton<DownloadSelector>();
        services.AddSingleton<DownloadDrainer>();

        services.AddSingleton<EvidenceSelector>();
        services.AddSingleton(sp =>
            DetectorSet.For(
                sp.GetRequiredService<IProcessRunner>(),
                sp.GetRequiredService<CratesidePaths>().KeydetectFfmpeg
            )
        );
        services.AddSingleton<EvidenceRunner>();
        services.AddSingleton<EvidenceDrainer>();

        services.AddSingleton(sp => new VerdictControl(
            sp.GetRequiredService<PauseStore>().Read().Judging
        ));
        services.AddSingleton<QuotaGuard>();
        services.AddSingleton(sp =>
        {
            var remembered = sp.GetRequiredService<PauseStore>().Read();

            return new WindowRoll(remembered.Backfill, remembered.BackfillJudgedAt);
        });

        services.AddSingleton<StageSignals>();

        services.AddSingleton<VerdictSelector>();
        services.AddSingleton<IProcessRunner, ProcessRunner>();
        services.AddHttpClient(
            SourcePages.ClientName,
            client =>
            {
                client.Timeout = TimeSpan.FromSeconds(15);
                client.DefaultRequestHeaders.UserAgent.ParseAdd(SourcePages.UserAgent);
            }
        );
        services.AddSingleton<ISourcePages, SourcePages>();
        services.AddSingleton(sp => new VerdictRunner(
            sp.GetRequiredService<LedgerStore>(),
            sp.GetRequiredService<WorkingSet>(),
            sp.GetRequiredService<IProcessRunner>(),
            sp.GetRequiredService<EvidenceRunner>(),
            sp.GetRequiredService<ISourcePages>(),
            sp.GetRequiredService<IOptions<JudgeOptions>>().Value,
            sp.GetRequiredService<TimeProvider>()
        ));
        services.AddSingleton<VerdictDrainer>();
        services.AddSingleton<BarJudging>();

        services.AddSingleton<HandoffSelector>();
        services.AddSingleton<DeletionLog>();
        services.AddSingleton<ActionLog>();
        services.AddSingleton<HandoffRunner>();
        services.AddSingleton<HandoffDrainer>();

        services.AddSingleton<FolderInfoWriter>();
        services.AddSingleton<TaggingSelector>();
        services.AddSingleton<TaggingRunner>();
        services.AddSingleton<TaggingDrainer>();

        services.AddSingleton<BackingRunner>();
        services.AddSingleton<KeyCorrections>();
        services.AddSingleton(sp => new TagStoreBackups(
            TagStoreBackups.DefaultRoot,
            sp.GetRequiredService<TimeProvider>()
        ));

        services.AddSingleton<ResolvedNameStore>();
        services.AddSingleton<NameResolver>();

        // Not registered beside the pausable stages on purpose (ADR 0014): a name resolve spends
        // one metadata fetch and contends for nothing, so it runs unattended like detection.
        services.AddHostedService<NameWorker>();

        // Unattended for the same reason, and it has to be: the gap opens on every import and is
        // collected by an ordinary hand-applied tag (ADR 0023).
        services.AddHostedService<BackingWorker>();

        // Unattended too: beatdetect runs on the CPU and contends for nothing (#75, #76).
        services.AddHostedService<BeatWorker>();

        // Unattended for the same reason: writing a sidecar decodes on the CPU (ADR 0043, 0047).
        services.AddSingleton(_ => new WrittenWarps(WrittenWarps.DefaultPath));
        services.AddSingleton(_ => new TransientOrigins(TransientOrigins.DefaultPath));
        services.AddSingleton(_ => new LiveAnalyses(LiveAnalyses.DefaultPath));
        services.AddSingleton(sp => new LoudnessReader(
            sp.GetRequiredService<IProcessRunner>(),
            LoudnessReader.DefaultPath
        ));
        // Read on every use, so a target changed in appsettings re-levels every sidecar.
        services.AddSingleton(sp => new ClipGains(
            sp.GetRequiredService<LoudnessReader>(),
            () => sp.GetRequiredService<IOptionsMonitor<LoudnessOptions>>().CurrentValue
        ));
        services.AddSingleton<IAudioDecoder, FfmpegAudioDecoder>();
        services.AddSingleton<ITransientDetector, OnsetDetector>();
        services.AddSingleton<SidecarWriter>();
        services.AddSingleton(sp => new WarpLending(
            sp.GetRequiredService<CratesidePaths>(),
            sp.GetRequiredService<SidecarWriter>(),
            sp.GetRequiredService<IOptions<WarpOptions>>().Value,
            path => StarterGrid.Of(sp.GetRequiredService<BeatReader>().Read(path)),
            sp.GetRequiredService<LiveAnalyses>()
        ));
        services.AddSingleton(sp => new WarpActions(
            sp.GetRequiredService<SidecarWriter>(),
            sp.GetRequiredService<WrittenWarps>(),
            sp.GetRequiredService<LiveAnalyses>(),
            new SidecarBackups(SidecarBackups.DefaultRoot, sp.GetRequiredService<TimeProvider>()),
            sp.GetRequiredService<TimeProvider>()
        ));
        services.AddSingleton(_ => new RetypedWarps(RetypedWarps.DefaultPath));
        services.AddSingleton(sp => new WarpTrueUp(
            sp.GetRequiredService<RetypedWarps>(),
            sp.GetRequiredService<WrittenWarps>(),
            new SidecarBackups(SidecarBackups.DefaultRoot, sp.GetRequiredService<TimeProvider>()),
            sp.GetRequiredService<WarpActions>(),
            sp.GetRequiredService<TimeProvider>()
        ));
        services.AddSingleton<TrueUpAll>();
        services.AddSingleton<StemWarpReader>();
        services.AddSingleton<StagingMarks>();
        services.AddSingleton(sp => new HandoffSidecars(
            sp.GetRequiredService<CratesidePaths>(),
            sp.GetRequiredService<SidecarWriter>(),
            sp.GetRequiredService<BeatReader>(),
            sp.GetRequiredService<IOptions<WarpOptions>>().Value
        ));
        services.AddHostedService<WarpWorker>();

        // Unattended too: reading loudness is ffmpeg on the CPU (ADR 0059).
        services.AddSingleton<Levelling>();
        services.AddHostedService<LoudnessWorker>();

        // Unattended too: keeping a filed track's warp guide is a file move (ADR 0046).
        services.AddSingleton<WarpGuideKeeper>();
        services.AddHostedService<WarpGuideWorker>();

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

        services.AddSingleton<IStagedFiles, StagedFiles>();
        services.AddSingleton<SourceProbe>();
        services.AddSingleton<TrackAdd>();
        services.AddSingleton<TrackRemoval>();
        services.AddSingleton<SampleAdoption>();
        services.AddSingleton<SourceAdd>();

        services.AddSingleton<StemRecut>();
        services.AddSingleton<ILibraryFiles, LibraryFiles>();
        services.AddSingleton<TrackRename>();
        services.AddSingleton<LookalikeAnswer>();

        services.AddSingleton<TrackSwap>();

        services.AddSingleton<SourceRetentionStore>();
        services.AddSingleton<SourceIndex>();
        services.AddSingleton<SourceBackfill>();
        services.AddSingleton(sp => new WorkingSetJanitor(
            sp.GetRequiredService<LedgerStore>(),
            sp.GetRequiredService<WorkingSet>(),
            sp.GetRequiredService<JobStore>(),
            sp.GetRequiredService<IOptions<CratesidePaths>>(),
            sp.GetRequiredService<SourceRetentionStore>(),
            sp.GetRequiredService<TimeProvider>(),
            videoId =>
                sp.GetRequiredService<TrackInstructionStore>()
                    .Read()
                    .FirstOrDefault(track =>
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

    /// <summary>
    /// The Tracks page's view of everything, and the cache that keeps it affordable.
    /// </summary>
    public static IServiceCollection AddCratesideTracks(this IServiceCollection services)
    {
        // QuotaReader sets its own authorization and beta headers per call; this configures
        // transport only.
        services
            .AddHttpClient(
                QuotaReader.ClientName,
                client =>
                {
                    client.BaseAddress = new Uri("https://api.anthropic.com");
                    client.Timeout = TimeSpan.FromSeconds(5);
                }
            )
            .SetHandlerLifetime(TimeSpan.FromMinutes(5));
        services.AddSingleton<IQuotaReader, QuotaReader>();

        services.AddSingleton<GenreStore>();
        services
            .AddHttpClient<GenreLookup>(client => client.Timeout = TimeSpan.FromSeconds(15))
            .SetHandlerLifetime(TimeSpan.FromMinutes(5));
        services.AddHostedService<GenreWorker>();

        services.AddSingleton<FileChanges>();
        services.AddHostedService<FileWatchService>();

        services.AddSingleton(sp =>
        {
            var store = new TrackMarkStore();
            store.Changed += sp.GetRequiredService<FileChanges>().Notify;

            return store;
        });

        services.AddSingleton<FamiliarityStore>();
        services
            .AddHttpClient<FamiliarityLookup>(client => client.Timeout = TimeSpan.FromSeconds(15))
            .SetHandlerLifetime(TimeSpan.FromMinutes(5));
        services.AddHostedService<FamiliarityWorker>();

        services.AddSingleton(sp => new TracksService(
            async ct =>
            {
                try
                {
                    var scan = await sp.GetRequiredService<WorkListService>().ScanAsync(ct);

                    var view = TracksView.Compose(
                        scan.View,
                        scan.Ledger,
                        sp.GetRequiredService<StagingReader>()
                            .Read(sp.GetRequiredService<CratesidePaths>()),
                        scan.Processed.StemsByVideoId.Concat(
                                SourcelessSongs.Scan(
                                    sp.GetRequiredService<CratesidePaths>().LibraryFolder
                                )
                            )
                            .ToDictionary(),
                        sp.GetRequiredService<CratesidePaths>().LibraryFolder,
                        sp.GetRequiredService<JobStore>(),
                        scan.Instructions,
                        sp.GetRequiredService<ResolvedNameStore>().Read(),
                        sp.GetRequiredService<WorkingSet>().Acquisitions(),
                        await sp.GetRequiredService<PresetCatalogReader>().ReadAsync(ct),
                        AbletonReads.TagsOrEmpty(
                            sp.GetRequiredService<FileIndexReader>().Read,
                            sp.GetRequiredService<CratesidePaths>().LibraryFolder
                        ),
                        sp.GetRequiredService<DeletionLog>().All(),
                        sp.GetRequiredService<GenreStore>().Read(),
                        sp.GetRequiredService<FamiliarityStore>().Read(),
                        sp.GetRequiredService<WarpTempoReader>().Read,
                        sp.GetRequiredService<BeatReader>().Read,
                        sp.GetRequiredService<WrittenWarps>().Of,
                        sp.GetRequiredService<TrackMarkStore>().Read()
                    );

                    sp.GetRequiredService<WarpTempoReader>().Save();
                    sp.GetRequiredService<SourceIndex>().Refresh(scan.Processed, scan.Ledger);

                    return view with
                    {
                        ReadAt = scan.ReadAt,
                        PlaylistProblem = scan.PlaylistProblem,
                    };
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    sp.GetRequiredService<ILoggerFactory>()
                        .CreateLogger("Crateside.Tracks")
                        .LogError(ex, "Reading the queue failed");

                    throw;
                }
            },
            sp.GetRequiredService<StageHub>(),
            freshness: () => sp.GetRequiredService<PlaylistSettingsStore>().Read().RefreshInterval,
            actions: sp.GetRequiredService<ActionLog>()
        ));

        return services;
    }
}
