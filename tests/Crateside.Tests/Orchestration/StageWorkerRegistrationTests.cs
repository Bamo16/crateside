using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Crateside.Tests.Orchestration;

/// <summary>How many hosted services survive when they share an implementation type, and when each stage's is its own.</summary>
public sealed class StageWorkerRegistrationTests
{
    private sealed class Evidence;

    private sealed class Verdict;

    private sealed class Worker<TStage>(string name) : BackgroundService
    {
        public string Name => name;

        protected override Task ExecuteAsync(CancellationToken stoppingToken) => Task.CompletedTask;
    }

    [Fact]
    public void AddHostedService_drops_the_second_registration_of_one_type()
    {
        var services = new ServiceCollection();

        services.AddHostedService(_ => new Worker<Evidence>("evidence"));
        services.AddHostedService(_ => new Worker<Evidence>("verdict"));

        var hosted = services.BuildServiceProvider().GetServices<IHostedService>().ToList();

        Assert.Single(hosted);
        Assert.Equal("evidence", Assert.IsType<Worker<Evidence>>(hosted[0]).Name);
    }

    [Fact]
    public void A_worker_typed_by_its_stage_survives_beside_the_others()
    {
        // Why StageWorker is generic over its drainer.
        var services = new ServiceCollection();

        services.AddHostedService(_ => new Worker<Evidence>("evidence"));
        services.AddHostedService(_ => new Worker<Verdict>("verdict"));

        var hosted = services.BuildServiceProvider().GetServices<IHostedService>().ToList();

        Assert.Equal(2, hosted.Count);
    }
}
