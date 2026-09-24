using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Crateside.Core.Staging;

/// <summary>
/// Deletes a sample to the recycle bin, never showing a dialog: one would block the drain loop
/// this runs in.
/// </summary>
public static class RecycleBin
{
    private const uint DeleteOperation = 0x0003;

    // NOCONFIRMATION | SILENT | ALLOWUNDO | NOERRORUI | NOCONFIRMMKDIR
    private const ushort SilentAndUndoable = 0x0010 | 0x0004 | 0x0040 | 0x0400 | 0x0200;

    /// <summary>
    /// Removes the file if it is there, recoverably where the platform allows; already gone is
    /// success.
    /// </summary>
    public static void Remove(string path)
    {
        if (!File.Exists(path))
            return;

        if (!OperatingSystem.IsWindows())
        {
            File.Delete(path);
            return;
        }

        if (SendToBin(path) is var code && code is not 0)
            throw new IOException($"{Path.GetFileName(path)} could not be recycled (0x{code:X}).");
    }

    [SupportedOSPlatform("windows")]
    private static int SendToBin(string path)
    {
        // The shell reads the list as double-null terminated, not as one string.
        var operation = new ShellFileOperation
        {
            Function = DeleteOperation,
            From = path + '\0' + '\0',
            Flags = SilentAndUndoable,
        };

        return SHFileOperation(ref operation);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShellFileOperation
    {
        public IntPtr Window;
        public uint Function;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string From;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string? To;

        public ushort Flags;

        [MarshalAs(UnmanagedType.Bool)]
        public bool Aborted;

        public IntPtr NameMappings;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string? ProgressTitle;
    }

#pragma warning disable SYSLIB1054 // LibraryImport cannot marshal this struct's string fields.
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHFileOperation(ref ShellFileOperation operation);
#pragma warning restore SYSLIB1054
}
