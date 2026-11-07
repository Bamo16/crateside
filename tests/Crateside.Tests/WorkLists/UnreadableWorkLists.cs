using Crateside.Core;
using Crateside.Core.Playlist;
using Crateside.Core.WorkLists;
using Crateside.Tests.Ledger;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;

namespace Crateside.Tests.WorkLists;

internal static class UnreadableWorkLists
{
    /// <summary>A service whose reader has no credential, so every read refuses.</summary>
    public static WorkListService In(
        string directory,
        IntakeStore intake,
        out CachedPlaylist playlist
    )
    {
        string PathIn(string name) => Path.Combine(directory, name);

        var options = Options.Create(new PlaylistOptions());
        var file = new QueueFile(options, PathIn("queue.json"));
        var authorizer = new PlaylistAuthorizer(
            options,
            new ProtectedTokenStore(
                DataProtectionProvider.Create("crateside-tests"),
                PathIn("tokens")
            ),
            new PlaylistWriteAccess(TimeProvider.System)
        );
        var settings = new PlaylistSettingsStore(file);
        playlist = new CachedPlaylist(
            new PlaylistReader(options),
            settings,
            new PlaylistRemover(authorizer),
            TimeProvider.System
        );

        return new WorkListService(
            TestLedger.Store(PathIn("runs.jsonl")),
            Options.Create(
                new CratesidePaths
                {
                    StagingFolder = PathIn("staging"),
                    LibraryFolder = PathIn("library"),
                    StemsKeyIdentified = PathIn("identified"),
                    StemsUnresolvedKey = PathIn("unresolved"),
                }
            ),
            new TrackInstructionStore(file),
            new BumpedTrack(),
            intake,
            playlist,
            new PlaylistConsumer(playlist, settings, authorizer, intake, TimeProvider.System)
        );
    }
}
