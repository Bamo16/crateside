using Crateside.Core.Orchestration.Loop;

namespace Crateside.Core.Ledger;

/// <summary>Tells keytag that another upload is the same song, carrying its latest verdict over.</summary>
public static class KeytagRestate
{
    /// <summary>Null once done, or why it failed.</summary>
    public static async Task<string?> RunAsync(
        string uv,
        string keytagRepo,
        string fromVideoId,
        string toUrl,
        CancellationToken ct = default
    )
    {
        var outcome = await ExternalProcess.RunAsync(
            uv,
            [
                "run",
                "--project",
                keytagRepo,
                "keytag-restate",
                // Joined, or an ID that starts with a dash is read as an option.
                $"--from={fromVideoId}",
                "--to",
                toUrl,
            ],
            workingDirectory: null,
            ct
        );

        return outcome.Succeeded
            ? null
            : ExternalProcess.Describe(outcome.StandardError, outcome.StandardOutput)
                ?? $"keytag-restate exited with {outcome.ExitCode}";
    }
}
