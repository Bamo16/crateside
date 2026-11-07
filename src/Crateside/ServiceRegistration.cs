using AbleKit.Tags;
using Crateside.Core;
using Crateside.Core.Ableton;
using Crateside.Core.Beats;
using Crateside.Core.Familiarity;
using Crateside.Core.Genres;
using Crateside.Core.Imports;
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
using Crateside.Core.Staging;
using Crateside.Core.Tracks;
using Crateside.Core.Warps;
using Crateside.Core.Watching;
using Crateside.Core.WorkLists;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.Extensions.Options;
using Serilog;
using Serilog.Events;

namespace Crateside;

public static class ServiceRegistration
{
    /// <summary>
    /// Configuration sources and the options bound from them.
    /// </summary>
    public static WebApplicationBuilder AddCratesideLogging(this WebApplicationBuilder builder)
    {
        var logsFolder =
            builder
                .Configuration.GetSection(CratesidePaths.SectionName)
                .Get<CratesidePaths>()
                ?.LogsFolder
            ?? new CratesidePaths().LogsFolder;

        var fileLogger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.Hosting.Lifetime", LogEventLevel.Information)
            .MinimumLevel.Override("System", LogEventLevel.Warning)
            .WriteTo.File(
                Path.Combine(logsFolder, "crateside-.log"),
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} {Level:u3} {SourceContext}: {Message:lj}{NewLine}{Exception}",
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14,
                shared: true
            )
            .CreateLogger();
        builder.Logging.AddSerilog(fileLogger, dispose: true);

        return builder;
    }

    public static WebApplicationBuilder AddCratesideConfiguration(
        this WebApplicationBuilder builder
    )
    {
        // Crateside__Paths__DataRoot=<folder> is the same setting as Paths:DataRoot (ADR 0074).
        builder.Configuration.AddEnvironmentVariables("Crateside__");

        // A data root is an instance of its own: nothing is read from the profile, secrets included.
        if (
            builder.Configuration.GetValue<string>($"{CratesidePaths.SectionName}:DataRoot")
            is not { Length: > 0 }
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
        }

        builder.Configuration.AddJsonFile(
            (
                builder.Configuration.GetSection(CratesidePaths.SectionName).Get<CratesidePaths>()
                ?? new CratesidePaths()
            ).PathOverridesFile,
            optional: true
        );

        builder.Services.Configure<CratesidePaths>(
            builder.Configuration.GetSection(CratesidePaths.SectionName)
        );
        builder.Services.Configure<PlaylistOptions>(
            builder.Configuration.GetSection(PlaylistOptions.SectionName)
        );
        builder.Services.AddSingleton<JudgeSettingsStore>();
        builder.Services.AddSingleton<IClaudeInstall, ClaudeOnPath>();
        builder.Services.AddSingleton<IPostConfigureOptions<JudgeOptions>, DefaultJudgeKind>();
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

    /// <summary>Every service crateside runs, grouped by the domain folder each belongs to.</summary>
    public static IServiceCollection AddCrateside(this IServiceCollection services) =>
        services
            .AddAppWide()
            .AddAbleton()
            .AddBeats()
            .AddFamiliarity()
            .AddGenres()
            .AddImports()
            .AddKeyDetection()
            .AddLedger()
            .AddLibrary()
            .AddLoudness()
            .AddMarks()
            .AddNaming()
            .AddOrchestration()
            .AddPlaylist()
            .AddQuota()
            .AddStaging()
            .AddTracks()
            .AddWarps()
            .AddWatching()
            .AddWorkLists();

    private static IServiceCollection AddAppWide(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.ConfigureHttpClientDefaults(http =>
        {
            http.ConfigureHttpClient(client =>
            {
                client.Timeout = TimeSpan.FromSeconds(15);
                client.DefaultRequestHeaders.UserAgent.ParseAdd(CratesideHttp.UserAgent);
            });
            http.SetHandlerLifetime(TimeSpan.FromMinutes(5));
        });
        services.AddSingleton<FolderBrowser>();
        services.AddSingleton<PathOverrideStore>();
        services.AddSingleton<FolderInfoReader>();
        services.AddSingleton<FolderInfoWriter>();
        services.AddSingleton(sp => new FileIndexReader(
            sp.GetRequiredService<IOptions<CratesidePaths>>().Value.AbletonIndex
        ));

        return services;
    }

    private static IServiceCollection AddAbleton(this IServiceCollection services)
    {
        services.AddSingleton<ISetLinks, AbletonSetLinks>();
        services.AddSingleton<WarpTempoReader>();

        return services;
    }

    private static IServiceCollection AddBeats(this IServiceCollection services)
    {
        services.AddSingleton<KnownBuilds>();
        services.AddSingleton<BeatReader>();
        // Unattended: beatdetect runs on the CPU and contends for nothing (#75, #76).
        services.AddHostedService<BeatWorker>();

        return services;
    }

    private static IServiceCollection AddFamiliarity(this IServiceCollection services)
    {
        services.AddSingleton<FamiliarityStore>();
        services.AddSingleton<FamiliarityLookup>();
        services.AddHostedService<FamiliarityWorker>();

        return services;
    }

    private static IServiceCollection AddGenres(this IServiceCollection services)
    {
        services.AddSingleton<GenreStore>();
        services.AddSingleton<GenreLookup>();
        services.AddHostedService<GenreWorker>();

        return services;
    }

    private static IServiceCollection AddImports(this IServiceCollection services)
    {
        services.AddSingleton<ImportStore>();
        services.AddSingleton<RecipeStore>();
        services.AddSingleton<TrackIntake>();
        services.AddSingleton<LinkIntake>();
        services.AddSingleton<InboxScanner>();
        services.AddHostedService<InboxWorker>();
        services.AddSingleton<ImportNameRepair>();
        services.AddHostedService<ImportNameWorker>();

        return services;
    }

    private static IServiceCollection AddKeyDetection(this IServiceCollection services) =>
        services.AddSingleton<IDetectorSet, DetectorSet>();

    private static IServiceCollection AddLedger(this IServiceCollection services) =>
        services.AddSingleton<LedgerStore>();

    private static IServiceCollection AddLibrary(this IServiceCollection services)
    {
        services.AddSingleton<LibraryKeyReader>();
        services.AddSingleton<LibrarySummary>();
        services.AddSingleton<ILibraryFiles, LibraryFiles>();

        return services;
    }

    private static IServiceCollection AddLoudness(this IServiceCollection services)
    {
        services.AddSingleton<LoudnessReader>();
        services.AddSingleton<ClipGains>();
        services.AddSingleton<Leveling>();
        // Unattended: reading loudness is ffmpeg on the CPU (ADR 0059).
        services.AddHostedService<LoudnessWorker>();

        return services;
    }

    private static IServiceCollection AddMarks(this IServiceCollection services) =>
        services.AddSingleton<TrackMarkStore>();

    private static IServiceCollection AddNaming(this IServiceCollection services)
    {
        services.AddSingleton<ResolvedNameStore>();
        services.AddSingleton<NameResolver>();
        // Not beside the pausable stages on purpose (ADR 0014): a name resolve spends one
        // metadata fetch and contends for nothing, so it runs unattended like detection.
        services.AddHostedService<NameWorker>();

        return services;
    }

    /// <summary>What the stages share, then each stage in pipeline order, then the track edits.</summary>
    private static IServiceCollection AddOrchestration(this IServiceCollection services)
    {
        services.AddSingleton<WorkingSet>();
        services.AddSingleton<SourceRetentionStore>();
        services.AddSingleton<SourceIndex>();
        services.AddSingleton<SourceBackfill>();
        services.AddSingleton<UnavailableSources>();
        services.AddSingleton<SourceAvailability>();
        services.AddHttpClient(SourceAvailability.ClientName);
        services.AddSingleton<WorkingSetJanitor>();
        services.AddHostedService<JanitorWorker>();

        services.AddSingleton<JobStore>();
        services.AddSingleton<JobStatusSpelling>();
        services.AddSingleton<IReopenedWork, ReopenedWork>();
        services.AddSingleton<DeletionLog>();
        services.AddSingleton<ActionLog>();
        services.AddSingleton<StageNotes>();

        services.AddSingleton<StageHub>();
        services.AddSingleton<StageSignals>();
        services.AddSingleton<PauseStore>();
        services.AddSingleton<IProcessRunner, ProcessRunner>();
        services.AddHostedService<DrainWorker>();

        services.AddSingleton<SourceProbe>();
        services.AddSingleton<DownloadRunner>();
        services.AddSingleton<DownloadSelector>();
        services.AddSingleton<DownloadDrainer>();
        services.AddHostedService<StageWorker<DownloadDrainer>>();

        services.AddSingleton<PresetCatalogReader>();
        services.AddSingleton<IProcessProbe, SystemProcessProbe>();
        services.AddSingleton<SeparationInterlock>();
        services.AddSingleton<SeparationSelector>();
        services.AddSingleton<SeparationRunner>();
        services.AddSingleton<DrainControl>();
        services.AddSingleton<SeparationDrainer>();

        services.AddSingleton<EvidenceSelector>();
        services.AddSingleton<EvidenceRunner>();
        services.AddSingleton<IWindowDetector, EvidenceRunner>();
        services.AddSingleton<DetectorBackfill>();
        services.AddSingleton<EvidenceCompleter>();
        services.AddSingleton<EvidenceDrainer>();
        services.AddHostedService<StageWorker<EvidenceDrainer>>();

        services.AddSingleton<VerdictControl>();
        services.AddSingleton<VerdictSelector>();
        services.AddSingleton<RejudgeQueue>();
        services.AddSingleton<ISourcePages, SourcePages>();
        services.AddSingleton<PageLookups>();
        services.AddSingleton<VerdictByRules>();
        services.AddSingleton<PageChoiceStore>();
        services.AddSingleton<PageChoices>();
        services.AddSingleton<PersonsKey>();
        services.AddSingleton<IVerdictJudge, VerdictRunner>();
        services.AddSingleton<VerdictDrainer>();
        services.AddSingleton<BarJudging>();
        services.AddSingleton<LookalikeAnswer>();
        services.AddHostedService<StageWorker<VerdictDrainer>>();

        services.AddSingleton<HandoffSelector>();
        services.AddSingleton<HandoffRunner>();
        services.AddSingleton<HandoffDrainer>();
        services.AddSingleton<TagStoreBackups>();
        services.AddSingleton<BackingRunner>();
        services.AddHostedService<StageWorker<HandoffDrainer>>();
        // Unattended: the gap opens on every import and is collected by an ordinary
        // hand-applied tag (ADR 0023).
        services.AddHostedService<BackingWorker>();

        services.AddSingleton<TaggingSelector>();
        services.AddSingleton<TaggingRunner>();
        services.AddSingleton<TaggingDrainer>();
        services.AddHostedService<StageWorker<TaggingDrainer>>();

        services.AddSingleton<TrackAdd>();
        services.AddSingleton<TrackRemoval>();
        services.AddSingleton<SampleAdoption>();
        services.AddSingleton<SourceAdd>();
        services.AddSingleton<StemRecut>();
        services.AddSingleton<TrackRename>();
        services.AddSingleton<TrackSwap>();

        return services;
    }

    /// <summary>The YouTube playlist, and the encrypted store its credential lives in.</summary>
    private static IServiceCollection AddPlaylist(this IServiceCollection services)
    {
        // The key ring is sealed to the current Windows user (docs/secrets.md).
        var dataProtection = services.AddDataProtection().SetApplicationName("crateside");

        services
            .AddOptions<KeyManagementOptions>()
            .Configure<IOptions<CratesidePaths>, ILoggerFactory>(
                (options, paths, logs) =>
                    options.XmlRepository = new FileSystemXmlRepository(
                        new DirectoryInfo(paths.Value.KeyRingFolder),
                        logs
                    )
            );

        if (OperatingSystem.IsWindows())
            dataProtection.ProtectKeysWithDpapi();

        services.AddSingleton<ProtectedTokenStore>();
        services.AddSingleton<PlaylistWriteAccess>();
        services.AddSingleton<PlaylistAuthorizer>();
        services.AddSingleton<PlaylistReader>();
        services.AddSingleton<PlaylistRemover>();
        services.AddSingleton<PlaylistSettingsStore>();
        services.AddSingleton<IntakeStore>();
        services.AddSingleton<CachedPlaylist>();
        services.AddSingleton<PlaylistConsumer>();

        return services;
    }

    private static IServiceCollection AddQuota(this IServiceCollection services)
    {
        // QuotaReader sets its own authorization and beta headers per call; this configures
        // transport only.
        services.AddHttpClient(
            QuotaReader.ClientName,
            client =>
            {
                client.BaseAddress = new Uri("https://api.anthropic.com");
                client.Timeout = TimeSpan.FromSeconds(5);
            }
        );
        services.AddSingleton<IQuotaReader, QuotaReader>();
        services.AddSingleton<QuotaGuard>();
        // The drainer's own instance, so a spend-down pass and the verdict stage share its one-at-a-time gate.
        services.AddSingleton<IBackfillJudge>(sp => sp.GetRequiredService<VerdictDrainer>());
        services.AddSingleton<SpendDownStore>();
        services.AddSingleton<SpendDown>();
        services.AddHostedService<SpendDownWorker>();

        return services;
    }

    private static IServiceCollection AddStaging(this IServiceCollection services)
    {
        services.AddSingleton<StagingReader>();
        services.AddSingleton<IStagedFiles, StagedFiles>();
        services.AddSingleton<StagingMarks>();
        services.AddSingleton<KeptStemMarks>();
        services.AddSingleton<WarpGuideKeeper>();
        // Unattended: keeping a filed track's warp guide is a file move (ADR 0046).
        services.AddHostedService<WarpGuideWorker>();

        return services;
    }

    /// <summary>The Tracks page's view of everything, the cache that keeps it affordable, and one track in full.</summary>
    private static IServiceCollection AddTracks(this IServiceCollection services)
    {
        services.AddSingleton<ITracksReader, TracksReader>();
        services.AddSingleton<TracksService>();
        services.AddSingleton<TrackAnnotationReader>();
        services.AddSingleton<SongKeyMigration>();
        services.AddSingleton<SampleReadingsReader>();
        services.AddSingleton<CacheKeeper>();
        services.AddSingleton<TrackDetailsReader>();
        services.AddSingleton<StemDetailsReader>();
        services.AddSingleton<FiledRecordsReader>();

        return services;
    }

    private static IServiceCollection AddWarps(this IServiceCollection services)
    {
        services.AddSingleton<WrittenWarps>();
        services.AddSingleton<TransientOrigins>();
        services.AddSingleton<LiveAnalyses>();
        services.AddSingleton<RetypedWarps>();
        services.AddSingleton<SidecarBackups>();
        services.AddSingleton<IAudioDecoder, FfmpegAudioDecoder>();
        services.AddSingleton<ITransientDetector, OnsetDetector>();
        services.AddSingleton<SidecarWriter>();
        services.AddSingleton<IStarterGrids, StarterGrids>();
        services.AddSingleton<WarpLending>();
        services.AddSingleton<WarpActions>();
        services.AddSingleton<WarpTrueUp>();
        services.AddSingleton<TrueUpAll>();
        services.AddSingleton<StemWarpReader>();
        services.AddSingleton<HandoffSidecars>();
        // Unattended: writing a sidecar decodes on the CPU (ADR 0043, 0047).
        services.AddHostedService<WarpWorker>();

        return services;
    }

    private static IServiceCollection AddWatching(this IServiceCollection services)
    {
        services.AddSingleton<FileChanges>();
        services.AddHostedService<FileWatchService>();

        return services;
    }

    private static IServiceCollection AddWorkLists(this IServiceCollection services)
    {
        services.AddSingleton<QueueFile>();
        services.AddSingleton<TrackInstructionStore>();
        services.AddSingleton<BumpedTrack>();
        services.AddSingleton<WorkListService>();

        return services;
    }
}
