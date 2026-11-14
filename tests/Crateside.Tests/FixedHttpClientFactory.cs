namespace Crateside.Tests;

/// <summary>Hands out the same client whatever name is asked for.</summary>
internal sealed class FixedHttpClientFactory(HttpClient client) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => client;
}
