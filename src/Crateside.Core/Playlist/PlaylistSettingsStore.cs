using Crateside.Core.Ledger;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Crateside.Core.Playlist;

/// <summary>
/// The playlist settings the user can change from the UI, kept in the ledger database (ADR 0083).
/// Configuration supplies the playlist until one is saved.
/// </summary>
public sealed class PlaylistSettingsStore(LedgerStore ledger, IOptions<PlaylistOptions> fallback)
{
    private readonly string? _configuredPlaylistId = fallback.Value.PlaylistId;

    public PlaylistSettings Read() =>
        ledger.Query(context =>
            context.PlaylistSettings.AsNoTracking().FirstOrDefault() is { } row
                ? new PlaylistSettings
                {
                    PlaylistId = row.PlaylistId ?? _configuredPlaylistId,
                    RefreshMinutes = row.RefreshMinutes,
                    ConsumeOnArrival = row.ConsumeOnArrival,
                }
                : new PlaylistSettings { PlaylistId = _configuredPlaylistId }
        );

    /// <summary>Saves the playlist ID. Pass null or empty to clear it.</summary>
    public void SavePlaylistId(string? playlistId) =>
        Save(row =>
            row.PlaylistId = PlaylistReference.TryParse(playlistId, out var parsed) ? parsed : null
        );

    /// <summary>How often to look at the playlist unprompted. Null restores the default.</summary>
    public void SaveRefreshMinutes(int? minutes) => Save(row => row.RefreshMinutes = minutes);

    /// <summary>Whether crateside empties the playlist as it takes tracks on.</summary>
    public void SaveConsumeOnArrival(bool consume) => Save(row => row.ConsumeOnArrival = consume);

    private void Save(Action<PlaylistSettingsRow> change) =>
        ledger.Save(context =>
        {
            var row = context.PlaylistSettings.FirstOrDefault();

            if (row is null)
                context.PlaylistSettings.Add(row = new PlaylistSettingsRow());

            change(row);
        });
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
