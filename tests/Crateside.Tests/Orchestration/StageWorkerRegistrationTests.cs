using Crateside.Core.Orchestration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Crateside.Tests.Orchestration;

/// <summary>
/// How many hosted services survive when two of them share an implementation type.
/// </summary>
/// <remarks>
/// The verdict stage silently never ran: no jobs, no logs, no ticks, while evidence worked
/// perfectly beside it. Both were registered with <c>AddHostedService(factory)</c> and both are
/// a <c>StageWorker</c> — one worker parameterised by stage being the whole point of the design.
/// </remarks>
public class StageWorkerRegistrationTests
{
    private sealed class Worker(string name) : BackgroundService
    {
        public string Name => name;

        protected override Task ExecuteAsync(CancellationToken stoppingToken) => Task.CompletedTask;
    }

    [Fact]
    public void AddHostedService_drops_the_second_registration_of_one_type()
    {
        // AddHostedService goes through TryAddEnumerable, which de-duplicates on implementation
        // type -- so the second call is discarded in silence. Exactly the trap a "one worker,
        // several stages" design walks into, and nothing anywhere reports it.
        var services = new ServiceCollection();

        services.AddHostedService(_ => new Worker("evidence"));
        services.AddHostedService(_ => new Worker("verdict"));

        var hosted = services.BuildServiceProvider().GetServices<IHostedService>().ToList();

        Assert.Single(hosted);
        Assert.Equal("evidence", Assert.IsType<Worker>(hosted[0]).Name);
    }

    [Fact]
    public void Registering_the_interface_directly_keeps_both()
    {
        // The fix, and the reason ServiceRegistration must not be "tidied" back to
        // AddHostedService: these are two workers of one type on purpose.
        var services = new ServiceCollection();

        services.AddSingleton<IHostedService>(_ => new Worker("evidence"));
        services.AddSingleton<IHostedService>(_ => new Worker("verdict"));

        var hosted = services.BuildServiceProvider().GetServices<IHostedService>().ToList();

        Assert.Equal(2, hosted.Count);
        Assert.Equal(["evidence", "verdict"], hosted.Select(h => ((Worker)h).Name));
    }
}
