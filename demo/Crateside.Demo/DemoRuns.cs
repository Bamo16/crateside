using Crateside.Core.Ledger;

namespace Crateside.Demo;

/// <summary>The ledger run a made-up track was judged with: the verdict crateside recorded for the song.</summary>
internal static class DemoRuns
{
    public static RunRecord For(DemoTrack track, Verdict verdict, DateTimeOffset at) =>
        new()
        {
            RecordedAt = at,
            Song = track.Song,
            Artist = track.Artist,
            Title = track.Title,
            SourceUrl = track.Url,
            InputKind = "full_mix",
            HasVocals = true,
            Confidence = verdict.Confidence,
            Center = verdict.Center,
            Mixed = verdict.Mixed,
            Sections =
            [
                .. verdict.Sections.Select(section => new Section(
                    section.Label,
                    new TonalCenter(section.Tonic, section.Mode),
                    section.Start
                )),
            ],
            Sources =
            [
                .. verdict.Sources.Select(source => new RunSource
                {
                    Url = source.Url,
                    Via = source.Via,
                    Kind = source.Kind,
                    States = source.States,
                    StatesKey = source.StatesKey,
                    StatesMode = source.StatesMode,
                    Note = source.Note,
                    Sections = source.Sections,
                }),
            ],
            Candidates = [.. verdict.Candidates],
            TonicCheck = verdict.TonicCheck,
            Reasoning = verdict.Reasoning,
            WithheldReasoning = verdict.WithheldReasoning,
            Evidence = verdict.Evidence.GetRawText(),
            OldBpm = new OldBpm(track.Bpm, "beatdetect", track.Bpm),
        };
}
