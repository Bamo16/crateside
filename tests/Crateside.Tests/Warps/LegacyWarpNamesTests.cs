using Crateside.Core.Samples;
using Crateside.Core.Warps;

namespace Crateside.Tests.Warps;

public sealed class LegacyWarpNamesTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("legacy-warp").FullName;

    private string Audio => Path.Combine(_root, "song - Full.flac");

    private string Sidecar => Path.GetFullPath(StemFileName.AnalysisFor(Audio));

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Theory]
    [InlineData("""{ "starterWarps": "Off" }""", AutoWarps.Off)]
    [InlineData(
        """{ "StarterWarps": "OnTheGrid", "flagClipOffBarLine": false }""",
        AutoWarps.OnTheGrid
    )]
    [InlineData("""{ "autoWarps": "Off" }""", AutoWarps.Off)]
    public void A_setting_saved_under_the_starter_name_reads_as_the_same_choice(
        string saved,
        AutoWarps expected
    )
    {
        var path = Path.Combine(_root, "warp-settings.json");
        File.WriteAllText(path, saved);

        Assert.Equal(expected, new WarpSettingsStore(path).Read());
    }

    [Fact]
    public void Remembering_a_choice_over_an_old_file_writes_the_new_name_and_keeps_the_rest()
    {
        var path = Path.Combine(_root, "warp-settings.json");
        File.WriteAllText(path, """{ "starterWarps": "Off", "flagClipOffBarLine": false }""");

        new WarpSettingsStore(path).Remember(AutoWarps.OnTheGrid);

        var written = File.ReadAllText(path);
        Assert.DoesNotContain("starter", written, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(AutoWarps.OnTheGrid, new WarpSettingsStore(path).Read());
        Assert.False(new WarpSettingsStore(path).ReadFlagClipOffBarLine());
    }

    [Theory]
    [InlineData("Off", AutoWarps.Off)]
    [InlineData("onthegrid", AutoWarps.OnTheGrid)]
    [InlineData("TempoOnly", null)]
    [InlineData("7", null)]
    [InlineData(null, null)]
    public void The_old_appsettings_key_reads_as_a_mode_only_when_it_names_one(
        string? value,
        AutoWarps? expected
    ) => Assert.Equal(expected, LegacyWarpNames.AutoWarpsFromOldSetting(value));

    [Fact]
    public void A_written_warp_recorded_as_a_starter_reads_as_the_same_auto_warp()
    {
        var path = Path.Combine(_root, "written-warps.json");
        var sidecar = JsonEscaped(Sidecar);
        File.WriteAllText(
            path,
            $$"""
            {
              "{{sidecar}}": {
                "from": null,
                "length": 1234,
                "written": "2026-11-01T10:00:00Z",
                "isUnwarped": false,
                "starter": {
                  "bpm": 126,
                  "readBpm": 125.99913,
                  "markerSeconds": 0.232,
                  "markerBeat": 0,
                  "readBeatSeconds": 0.23209,
                  "downbeat": { "seconds": 1.18447, "confidence": 0.317, "beatsPerBar": 4 },
                  "barFrom": "UnsureDownbeat",
                  "downbeatSureAt": 0.6
                },
                "warpOffBecause": null,
                "shift": null
              }
            }
            """
        );

        var read = new WrittenWarps(path).Ever(Audio);

        Assert.Equal(
            new WrittenWarp(
                null,
                1234,
                new DateTime(2026, 11, 1, 10, 0, 0, DateTimeKind.Utc),
                AutoWarp: new WrittenAutoWarp(
                    126,
                    125.99913,
                    0.232,
                    0,
                    0.23209,
                    new ReadDownbeat(1.18447, 0.317, 4),
                    AutoWarpBar.UnsureDownbeat,
                    0.6
                )
            ),
            read
        );
    }

    [Fact]
    public void A_written_warp_file_is_rewritten_under_the_new_name_on_the_next_record()
    {
        var path = Path.Combine(_root, "written-warps.json");
        File.WriteAllText(
            path,
            $$"""
            { "{{JsonEscaped(
                Sidecar
            )}}": { "from": null, "length": 1, "written": "2026-11-01T10:00:00Z", "starter": null } }
            """
        );
        File.WriteAllText(Sidecar, "x");

        new WrittenWarps(path).RecordUnwarped(Audio);

        Assert.DoesNotContain(
            "starter",
            File.ReadAllText(path),
            StringComparison.OrdinalIgnoreCase
        );
    }

    [Fact]
    public void A_save_that_started_from_a_starter_reads_as_one_that_started_from_an_auto_warp()
    {
        File.WriteAllText(Sidecar, "sidecar");
        var info = new FileInfo(Sidecar);
        var path = Path.Combine(_root, "saved-warps.json");
        File.WriteAllText(
            path,
            $$"""
            {
              "{{JsonEscaped(Sidecar)}}": {
                "at": "2026-11-01T10:00:00+00:00",
                "length": {{info.Length}},
                "written": "{{info.LastWriteTimeUtc:O}}",
                "edits": {
                  "barShift": 1,
                  "nudgeMs": 2.5,
                  "tempoFactor": 2,
                  "clipStartBar": 3,
                  "clipWasBar": 1,
                  "fromStarter": true
                }
              }
            }
            """
        );

        var read = new SavedWarps(path).Of(Audio);

        Assert.Equal(new SavedEdits(1, 2.5, 2, 3, 1, FromAutoWarp: true), read?.Edits);
    }

    [Fact]
    public void An_analysis_awaited_for_a_starter_reads_as_one_awaited_for_an_auto_warp()
    {
        var path = Path.Combine(_root, "live-analyses.json");
        File.WriteAllText(
            path,
            $$"""
            {
              "{{JsonEscaped(Audio)}}": {
                "backup": "backup.asd",
                "from": null,
                "wasStarter": true,
                "sent": "2026-11-01T10:00:00Z",
                "wasUnwarped": false,
                "starter": {
                  "bpm": 126,
                  "readBpm": 126,
                  "markerSeconds": 0.5,
                  "markerBeat": 2,
                  "readBeatSeconds": null,
                  "downbeat": null,
                  "barFrom": "NoDownbeat",
                  "downbeatSureAt": 0.6
                }
              }
            }
            """
        );

        var read = new LiveAnalyses(path).Of(Audio);

        Assert.True(read?.WasAutoWarp);
        Assert.Equal(
            new WrittenAutoWarp(126, 126, 0.5, 2, null, null, AutoWarpBar.NoDownbeat, 0.6),
            read?.AutoWarp
        );
    }

    private static string JsonEscaped(string path) => path.Replace("\\", "\\\\");
}
