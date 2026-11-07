using Crateside.Core;

namespace Crateside.Tests;

public sealed class CratesidePathsTests
{
    [Fact]
    public void A_data_root_moves_state_stores_and_default_folders_under_it()
    {
        var root = Path.Combine(Path.GetTempPath(), "demo-root");
        var paths = new CratesidePaths { DataRoot = root };

        Assert.All(
            new[]
            {
                paths.LedgerDatabase,
                paths.JobsFile,
                paths.IntakeFile,
                paths.KeyRingFolder,
                paths.WorkingFolder,
                paths.ClaudeCredentialsFile,
                paths.AbletonIndex,
                paths.StagingFolder,
                paths.LibraryFolder,
                paths.StemsKeyIdentified,
            },
            path => Assert.StartsWith(root, path, StringComparison.OrdinalIgnoreCase)
        );
    }

    [Fact]
    public void A_path_set_by_name_wins_over_the_data_root()
    {
        var paths = new CratesidePaths { DataRoot = "root", LibraryFolder = "elsewhere" };

        Assert.Equal("elsewhere", paths.LibraryFolder);
    }

    [Fact]
    public void Without_a_data_root_state_stays_in_the_profile()
    {
        Assert.StartsWith(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            new CratesidePaths().JobsFile,
            StringComparison.OrdinalIgnoreCase
        );
    }
}
