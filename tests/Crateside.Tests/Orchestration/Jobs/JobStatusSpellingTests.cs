using Crateside.Core.Orchestration.Jobs;
using Microsoft.Extensions.Time.Testing;

namespace Crateside.Tests.Orchestration.Jobs;

public sealed class JobStatusSpellingTests : IDisposable
{
    private const string Old =
        """{"id":"a:Evidence","videoId":"a","stage":"Evidence","status":"Cancelled","timestamp":"2026-08-17T21:30:07+00:00","error":"said \"status\":\"Cancelled\""}""";

    private const string Spaced =
        """{"id": "b:Separation", "videoId": "b", "stage": "Separation", "status": "Cancelled", "timestamp": "2026-09-19T21:05:36+00:00"}""";

    private const string Running =
        """{"id":"a:Evidence","videoId":"a","stage":"Evidence","status":"Running","timestamp":"2026-08-17T21:00:00+00:00"}""";

    private readonly string _folder = Path.Combine(
        Path.GetTempPath(),
        "crateside-tests",
        Guid.NewGuid().ToString("n")
    );

    private readonly FakeTimeProvider _time = new(
        new DateTimeOffset(2026, 10, 25, 12, 0, 0, TimeSpan.Zero)
    );

    private string JobsPath => Path.Combine(_folder, "jobs.jsonl");

    private string BackupFolder => Path.Combine(_folder, "backups");

    public void Dispose()
    {
        if (Directory.Exists(_folder))
            Directory.Delete(_folder, recursive: true);

        GC.SuppressFinalize(this);
    }

    private JobStatusSpelling Migration() => new(JobsPath, BackupFolder, _time);

    private void Write(params string[] lines)
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(JobsPath, string.Join('\n', lines) + "\n");
    }

    [Fact]
    public void A_cancelled_job_is_read_back_as_canceled_after_the_migration()
    {
        Write(Running, Old);

        Assert.Equal(1, Migration().Run());

        var job = new JobStore(JobsPath, _time).All().Single();

        Assert.Equal(JobStatus.Canceled, job.Status);
    }

    [Fact]
    public void Only_the_status_is_respelled_and_everything_else_stays_byte_for_byte()
    {
        Write(Running, Old);

        Migration().Run();

        Assert.Equal(
            string.Join(
                '\n',
                Running,
                Old.Replace("\"status\":\"Cancelled\",", "\"status\":\"Canceled\",")
            ) + "\n",
            File.ReadAllText(JobsPath)
        );
    }

    [Fact]
    public void A_line_written_with_spaces_around_the_colon_is_respelled_too()
    {
        Write(Spaced);

        Assert.Equal(1, Migration().Run());

        Assert.True(new JobStore(JobsPath, _time).TryGet("b", JobStage.Separation, out var job));
        Assert.Equal(JobStatus.Canceled, job.Status);
    }

    [Fact]
    public void The_file_as_it_was_is_kept_in_the_backup_folder()
    {
        Write(Running, Old);

        Migration().Run();

        var backup = Assert.Single(Directory.GetFiles(BackupFolder));

        Assert.StartsWith("jobs.jsonl.20261025-", Path.GetFileName(backup));
        Assert.Equal(string.Join('\n', Running, Old) + "\n", File.ReadAllText(backup));
    }

    [Fact]
    public void Running_it_again_changes_nothing_and_makes_no_second_backup()
    {
        Write(Running, Old);
        Migration().Run();
        var migrated = File.ReadAllText(JobsPath);
        _time.Advance(TimeSpan.FromMinutes(5));

        Assert.Equal(0, Migration().Run());

        Assert.Equal(migrated, File.ReadAllText(JobsPath));
        Assert.Single(Directory.GetFiles(BackupFolder));
    }

    [Fact]
    public void A_file_with_nothing_to_respell_is_left_alone_with_no_backup()
    {
        Write(Running);

        Assert.Equal(0, Migration().Run());
        Assert.False(Directory.Exists(BackupFolder));
    }

    [Fact]
    public void A_missing_file_is_nothing_to_do()
    {
        Assert.Equal(0, Migration().Run());
    }
}
