using Crateside.Core;
using Crateside.Core.Ledger;
using Crateside.Core.Orchestration;
using Crateside.Core.Queue;
using Crateside.Core.Staging;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;

namespace Crateside;

/// <summary>
/// The service graph, in the four groups it actually divides into. Split out of
/// <c>Program.cs</c> so the composition root reads as an outline rather than a list.
/// </summary>
public static class ServiceRegistration
{
    /// <summary>
    /// Configuration sources and the options bound from them.
    /// </summary>
    /// <remarks>
    /// Each options type is also registered as its bare value. Most consumers want the settings,
    /// not an <see cref="IOptions{T}"/> wrapper, and doing the unwrap once here is what keeps
    /// their constructors honest about what they depend on.
    /// </remarks>
    public static WebApplicationBuilder AddCratesideConfiguration(
        this WebApplicationBuilder builder
    )
    {
        // The API key comes from user-secrets, which the Development environment loads by
        // default — it lives outside the working tree so it cannot surface in an editor or a
        // file tree. appsettings.Secrets.json stays supported as a gitignored fallback for a
        // non-dev run.
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

    /// <summary>
    /// Stateless readers over files crateside does not own. Safe to share, and none of them
    /// hold anything between calls.
    /// </summary>
    public static IServiceCollection AddCratesideReaders(this IServiceCollection services)
    {
        services.AddSingleton<AbletonFolderInfoReader>();
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
        // The OAuth refresh token can delete from the playlist, so it is encrypted at rest. The
        // key ring is sealed to the current Windows user (docs/secrets.md).
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

        // DPAPI is Windows-only. crateside is a Windows app (the Ableton roots are P:\ paths),
        // so this always applies in practice; the guard is here because the analyzer is right
        // that the call is not portable, and falling back to the default key protection is the
        // honest alternative rather than suppressing the warning.
        if (OperatingSystem.IsWindows())
            dataProtection.ProtectKeysWithDpapi();

        services.AddSingleton<ProtectedTokenStore>();
        services.AddSingleton<QueueAuthorizer>();
        services.AddSingleton<QueueRemover>();
        services.AddSingleton<QueueService>();
        return services;
    }

    /// <summary>
    /// Draining: what turns the queue into stems on disk without anyone asking it to.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The four factories below are not a style choice and not indirection for its own sake.
    /// Each of these types takes a trailing primitive with a default — <c>maxStrikes</c>,
    /// <c>executable</c>, <c>startPaused</c>, <c>path</c> — and the built-in container does not
    /// use constructor defaults: it tries to resolve <c>int</c> and <c>string</c> from the
    /// graph and fails. A factory is the only way to say "use the default", so every one of
    /// them is a factory that passes nothing but the services.
    /// </para>
    /// <para>
    /// The job store is the one piece of non-derived state in the system, so it is a singleton
    /// with a single writer — the drain worker.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddCratesideOrchestration(this IServiceCollection services)
    {
        services.AddSingleton(_ => new JobStore());
        services.AddSingleton<IProcessProbe, SystemProcessProbe>();
        services.AddSingleton<SeparationInterlock>();
        services.AddSingleton(sp => new SeparationSelector(sp.GetRequiredService<JobStore>()));
        services.AddSingleton(sp => new SeparationRunner(
            sp.GetRequiredService<SeparationInterlock>(),
            sp.GetRequiredService<CratesidePaths>()
        ));
        services.AddSingleton(sp => new DrainControl(
            sp.GetRequiredService<DrainOptions>().StartPaused
        ));
        services.AddSingleton<SeparationDrainer>();

        // Draining is on by default: two separations at once are ruinous, but one is
        // unnoticeable, so there is no moment for the user to pick. Pause is in the Queue view
        // for when the room is being recorded in.
        services.AddHostedService<DrainWorker>();
        return services;
    }
}
