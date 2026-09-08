using System.Text;
using Google.Apis.Json;
using Google.Apis.Util.Store;
using Microsoft.AspNetCore.DataProtection;

namespace Crateside.Core.Queue;

/// <summary>
/// Google's token store, encrypted at rest with Windows DPAPI.
/// </summary>
/// <remarks>
/// Defends against the file being copied off the box or read by another user; it does not
/// defend against anything running as this user. See <c>docs/secrets.md</c>.
/// </remarks>
public sealed class ProtectedTokenStore(IDataProtectionProvider provider, string? directory = null)
    : IDataStore
{
    private const string Purpose = "crateside.queue.oauth.v1";

    private readonly IDataProtector _protector = provider.CreateProtector(Purpose);

    private readonly string _directory =
        directory
        ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "crateside",
            "tokens"
        );

    /// <summary>Whether any token is stored at all — "have we ever consented?".</summary>
    public bool HasStoredToken =>
        Directory.Exists(_directory) && Directory.EnumerateFiles(_directory).Any();

    public Task StoreAsync<T>(string key, T value)
    {
        Directory.CreateDirectory(_directory);

        var json = NewtonsoftJsonSerializer.Instance.Serialize(value);
        var protectedBytes = _protector.Protect(Encoding.UTF8.GetBytes(json));

        return File.WriteAllBytesAsync(PathFor(key), protectedBytes);
    }

    public async Task<T> GetAsync<T>(string key)
    {
        var path = PathFor(key);

        if (!File.Exists(path))
            return default!;

        try
        {
            var plaintext = _protector.Unprotect(await File.ReadAllBytesAsync(path));

            return NewtonsoftJsonSerializer.Instance.Deserialize<T>(
                Encoding.UTF8.GetString(plaintext)
            );
        }
        catch (Exception)
        {
            // Corrupt or undecryptable — treated as absent; the caller re-consents.
            return default!;
        }
    }

    public Task DeleteAsync<T>(string key)
    {
        File.Delete(PathFor(key));

        return Task.CompletedTask;
    }

    public Task ClearAsync()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);

        return Task.CompletedTask;
    }

    // Not guaranteed to be safe filename characters; sanitized to avoid writing outside the directory.
    private string PathFor(string key) =>
        Path.Combine(
            _directory,
            string.Concat(key.Split(Path.GetInvalidFileNameChars())) + ".protected"
        );
}
