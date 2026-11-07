using System.Text.Json.Serialization;

namespace Crateside.Core.Marks;

/// <summary>A yes/no the maintainer sets on a track in crateside; Live shows it as a Collection.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<TrackMark>))]
public enum TrackMark
{
    /// <summary>The key changes partway (#128).</summary>
    ChangesKey,

    /// <summary>The track is not consistent 4/4 (#86).</summary>
    OddBars,
}
