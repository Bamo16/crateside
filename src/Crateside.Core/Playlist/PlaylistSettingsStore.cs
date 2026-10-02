using Crateside.Core.WorkLists;

namespace Crateside.Core.Playlist;

/// <summary>The playlist settings the user can change from the UI, kept in <see cref="QueueFile"/>.</summary>
public sealed class PlaylistSettingsStore(QueueFile file)
{
    public PlaylistSettings Read()
    {
        var document = file.Read();

        return new PlaylistSettings
        {
            PlaylistId = document.PlaylistId,
            RefreshMinutes = document.RefreshMinutes,
            ConsumeOnArrival = document.ConsumeOnArrival,
        };
    }

    /// <summary>Saves the playlist ID. Pass null or empty to clear it.</summary>
    public void SavePlaylistId(string? playlistId) =>
        file.Save(document =>
            document with
            {
                PlaylistId = PlaylistReference.TryParse(playlistId, out var parsed) ? parsed : null,
            }
        );

    /// <summary>How often to look at the playlist unprompted. Null restores the default.</summary>
    public void SaveRefreshMinutes(int? minutes) =>
        file.Save(document => document with { RefreshMinutes = minutes });

    /// <summary>Whether crateside empties the playlist as it takes tracks on.</summary>
    public void SaveConsumeOnArrival(bool consume) =>
        file.Save(document => document with { ConsumeOnArrival = consume });
}

/// <summary>User-editable playlist settings. Contains no secrets.</summary>
public sealed record PlaylistSettings
{
    /// <summary>What <see cref="RefreshMinutes"/> means when nobody has said.</summary>
    public const int DefaultRefreshMinutes = 5;

    public string? PlaylistId { get; init; }

    /// <summary>
    /// How often to rebuild the rows unprompted, in minutes; zero is never. Stage workers read
    /// the queue on their own loop regardless.
    /// </summary>
    public int? RefreshMinutes { get; init; }

    /// <summary>
    /// Whether an entry leaves the playlist once recorded; turning it on removes every known entry
    /// on the next scan, for good (ADR 0021).
    /// </summary>
    public bool ConsumeOnArrival { get; init; }

    /// <summary>How long the rows may go unrebuilt. Zero means never.</summary>
    public TimeSpan RefreshInterval =>
        TimeSpan.FromMinutes(RefreshMinutes ?? DefaultRefreshMinutes);
}
