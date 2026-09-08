using System.Diagnostics;

namespace Crateside.Core.Orchestration;

/// <summary>
/// Asks the OS whether a process is still alive. Abstracted so the interlock — the one piece
/// that must not be got wrong — is testable without spawning anything.
/// </summary>
public interface IProcessProbe
{
    /// <summary>
    /// The start time of a live process, or false if no process is running under this id.
    /// </summary>
    /// <remarks>
    /// A PID is not an identity: Windows reuses process ids freely, so a live PID matching one
    /// we wrote down is not proof it is the same process. The start time is what turns a
    /// coincidence into a match.
    /// </remarks>
    bool TryGetStartTime(int processId, out DateTimeOffset startedAt);
}

/// <summary>The real one.</summary>
public sealed class SystemProcessProbe : IProcessProbe
{
    public bool TryGetStartTime(int processId, out DateTimeOffset startedAt)
    {
        startedAt = default;

        try
        {
            using var process = Process.GetProcessById(processId);

            if (process.HasExited)
                return false;

            startedAt = process.StartTime;

            return true;
        }
        // No such process is the ordinary answer, not a fault. Access-denied means the id
        // belongs to something we do not own — which is equally "not our separation".
        catch (Exception e)
            when (e is ArgumentException or InvalidOperationException or SystemException)
        {
            return false;
        }
    }
}
