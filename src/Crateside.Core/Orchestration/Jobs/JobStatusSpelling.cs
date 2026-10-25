using System.Text.RegularExpressions;

namespace Crateside.Core.Orchestration.Jobs;

/// <summary>One-time startup step: job files written before the spelling change say <c>Cancelled</c>.</summary>
public sealed partial class JobStatusSpelling
{
    private readonly string _jobsPath;
    private readonly string _backupFolder;
    private readonly TimeProvider _time;

    public JobStatusSpelling(JobStore jobs, CratesidePaths paths, TimeProvider time)
        : this(jobs.Path, paths.SpellingBackups, time) { }

    internal JobStatusSpelling(string jobsPath, string backupFolder, TimeProvider time)
    {
        _jobsPath = jobsPath;
        _backupFolder = backupFolder;
        _time = time;
    }

    [GeneratedRegex(@"(?<Key>""status""\s*:\s*)""Cancelled""")]
    private static partial Regex OldStatus { get; }

    /// <summary>Rewrites the job file with the new spelling, after copying it aside; returns how many lines changed.</summary>
    public int Run()
    {
        if (!File.Exists(_jobsPath))
            return 0;

        var text = File.ReadAllText(_jobsPath);
        var changed = OldStatus.Count(text);

        if (changed is 0)
            return 0;

        Directory.CreateDirectory(_backupFolder);
        File.Copy(
            _jobsPath,
            Path.Combine(
                _backupFolder,
                $"{Path.GetFileName(_jobsPath)}.{_time.GetLocalNow():yyyyMMdd-HHmmss}"
            )
        );

        var temporary = $"{_jobsPath}.tmp";

        File.WriteAllText(temporary, OldStatus.Replace(text, "${Key}\"Canceled\""));
        File.Move(temporary, _jobsPath, overwrite: true);

        return changed;
    }
}
