using System.Runtime.InteropServices;

namespace Crateside;

/// <summary>Whether crateside runs inside a Windows job that takes its child processes down with it.</summary>
internal static class JobObject
{
    private const int BasicLimitInformation = 2;
    private const uint KillOnJobClose = 0x2000;

    /// <summary>
    /// True under <c>dotnet run</c>, whose job ends any <c>stemforge-cli</c> when crateside exits
    /// (ADR 0042).
    /// </summary>
    public static bool KillsChildrenOnExit() =>
        OperatingSystem.IsWindows()
        && IsProcessInJob(GetCurrentProcess(), IntPtr.Zero, out var inJob)
        && inJob
        && QueryInformationJobObject(
            IntPtr.Zero,
            BasicLimitInformation,
            out var limits,
            Marshal.SizeOf<BasicLimits>(),
            out _
        )
        && (limits.LimitFlags & KillOnJobClose) != 0;

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsProcessInJob(
        IntPtr process,
        IntPtr job,
        [MarshalAs(UnmanagedType.Bool)] out bool result
    );

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryInformationJobObject(
        IntPtr job,
        int infoClass,
        out BasicLimits info,
        int length,
        out int returned
    );

    [StructLayout(LayoutKind.Sequential)]
    private struct BasicLimits
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }
}
