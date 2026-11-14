using Crateside.Core.Ledger;
using Microsoft.EntityFrameworkCore;

namespace Crateside.Core.Vocals;

/// <summary>
/// The Lead or Sparse the person gave a vocal stem, kept in the ledger by stem identity: its track
/// and the preset that cut it, so a stem moved from staging into the library keeps its answer
/// (ADR 0087, 0090).
/// </summary>
public sealed class VocalAnswerStore(LedgerStore ledger)
{
    private readonly Lock _gate = new();

    private Dictionary<string, string>? _cached;

    /// <summary>Every answer, by stem identity.</summary>
    public IReadOnlyDictionary<string, string> Read()
    {
        lock (_gate)
            return new Dictionary<string, string>(_cached ??= Load(), StringComparer.Ordinal);
    }

    public void Settle(string stem, string value)
    {
        if (!VocalVocabulary.All.Contains(value))
            throw new ArgumentException($"'{value}' is not Lead or Sparse.", nameof(value));

        lock (_gate)
        {
            var answers = _cached ??= Load();

            answers[stem] = value;
            ledger.Save(context =>
                context.Upsert(
                    context.VocalAnswers,
                    row => row.Stem == stem,
                    new VocalAnswerRow { Stem = stem, Answer = value }
                )
            );
        }
    }

    /// <summary>Takes the answer back, once the person has taken their tag off in Live.</summary>
    public void Clear(string stem)
    {
        lock (_gate)
        {
            var answers = _cached ??= Load();

            if (!answers.Remove(stem))
                return;

            ledger.Save(context =>
                context.VocalAnswers.RemoveRange(
                    context.VocalAnswers.Where(row => row.Stem == stem)
                )
            );
        }
    }

    private Dictionary<string, string> Load() =>
        ledger
            .Query(context => context.VocalAnswers.AsNoTracking().ToList())
            .ToDictionary(row => row.Stem, row => row.Answer, StringComparer.Ordinal);
}
