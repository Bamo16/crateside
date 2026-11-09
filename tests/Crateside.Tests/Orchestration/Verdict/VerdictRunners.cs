using Crateside.Core.Ledger;
using Crateside.Core.Orchestration;
using Crateside.Core.Orchestration.Evidence;
using Crateside.Core.Orchestration.Loop;
using Crateside.Core.Orchestration.Verdict;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Crateside.Tests.Orchestration.Verdict;

internal static class VerdictRunners
{
    public static VerdictRunner Create(
        LedgerStore store,
        WorkingSet workingSet,
        SourceIndex sources,
        IProcessRunner processes,
        IWindowDetector detector,
        ISourcePages pages,
        IOptions<JudgeOptions> options,
        TimeProvider time,
        ILogger<VerdictRunner> log
    ) =>
        new(
            store,
            workingSet,
            sources,
            new JudgeSessions(processes, options, NullLogger<JudgeSessions>.Instance),
            new JudgeDetection(detector),
            new JudgePrompt(workingSet, options),
            new VerdictCompletion(workingSet, options, time),
            new JudgeAvailability(options),
            pages,
            options,
            log
        );
}
