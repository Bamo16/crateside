using Crateside.Core.Queue;
using Microsoft.AspNetCore.DataProtection;

namespace Crateside.Tests.Queue;

/// <summary>
/// The refresh token is the credential that can delete from the user's playlist, so what is
/// on disk must not be readable and a failure to decrypt must not be fatal.
/// </summary>
public class ProtectedTokenStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "crateside-tests",
        Guid.NewGuid().ToString("n")
    );

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);

        GC.SuppressFinalize(this);
    }

    private ProtectedTokenStore Store(string? applicationName = null) =>
        new(
            DataProtectionProvider.Create(
                new DirectoryInfo(Path.Combine(_directory, "keys")),
                builder => builder.SetApplicationName(applicationName ?? "crateside-tests")
            ),
            Path.Combine(_directory, "tokens")
        );

    private sealed record StoredToken(string RefreshToken, string AccessToken);

    [Fact]
    public async Task A_stored_value_round_trips()
    {
        var store = Store();
        var token = new StoredToken("refresh-abc", "access-xyz");

        await store.StoreAsync("user", token);

        Assert.Equal(token, await store.GetAsync<StoredToken>("user"));
    }

    [Fact]
    public async Task What_lands_on_disk_is_not_readable()
    {
        await Store().StoreAsync("user", new StoredToken("refresh-abc", "access-xyz"));

        var file = Directory.GetFiles(Path.Combine(_directory, "tokens")).Single();
        var raw = File.ReadAllText(file);

        Assert.DoesNotContain("refresh-abc", raw, StringComparison.Ordinal);
        Assert.DoesNotContain("access-xyz", raw, StringComparison.Ordinal);
        Assert.DoesNotContain("RefreshToken", raw, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_token_this_key_ring_cannot_decrypt_reads_as_absent()
    {
        // What a key-ring rotation or a copied file looks like. One more consent prompt is a
        // state the Queue already handles; a crash is not.
        await Store("crateside-tests").StoreAsync("user", new StoredToken("refresh", "access"));

        var other = new ProtectedTokenStore(
            DataProtectionProvider.Create(
                new DirectoryInfo(Path.Combine(_directory, "other-keys")),
                builder => builder.SetApplicationName("something-else")
            ),
            Path.Combine(_directory, "tokens")
        );

        Assert.Null(await other.GetAsync<StoredToken>("user"));
    }

    [Fact]
    public async Task Garbage_on_disk_reads_as_absent_rather_than_throwing()
    {
        var store = Store();
        await store.StoreAsync("user", new StoredToken("refresh", "access"));

        var file = Directory.GetFiles(Path.Combine(_directory, "tokens")).Single();
        File.WriteAllText(file, "not encrypted anything");

        Assert.Null(await store.GetAsync<StoredToken>("user"));
    }

    [Fact]
    public async Task An_absent_token_is_not_an_error()
    {
        Assert.Null(await Store().GetAsync<StoredToken>("never-stored"));
        Assert.False(Store().HasStoredToken);
    }

    [Fact]
    public async Task Clearing_forgets_everything()
    {
        var store = Store();
        await store.StoreAsync("user", new StoredToken("refresh", "access"));
        Assert.True(store.HasStoredToken);

        await store.ClearAsync();

        Assert.False(store.HasStoredToken);
        Assert.Null(await store.GetAsync<StoredToken>("user"));
    }

    [Fact]
    public async Task A_key_containing_a_path_separator_cannot_escape_the_directory()
    {
        var store = Store();

        await store.StoreAsync(@"..\..\escaped", new StoredToken("refresh", "access"));

        Assert.All(
            Directory.GetFiles(Path.Combine(_directory, "tokens")),
            path =>
                Assert.Equal(
                    Path.GetFullPath(Path.Combine(_directory, "tokens")),
                    Path.GetDirectoryName(Path.GetFullPath(path))
                )
        );
    }
}
