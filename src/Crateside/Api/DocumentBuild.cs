using System.Reflection;
using Microsoft.AspNetCore.DataProtection;

namespace Crateside.Api;

/// <summary>The build's run of the app to write <c>docs/openapi.json</c>, which must start nothing and write nothing.</summary>
public static class DocumentBuild
{
    public static bool IsRunning =>
        Assembly.GetEntryAssembly()?.GetName().Name == "GetDocument.Insider";

    /// <summary>Drops the hosted services that touch state: crateside's workers, and the data protection key ring's.</summary>
    public static IServiceCollection WithoutWorkers(this IServiceCollection services)
    {
        Assembly[] touchState =
        [
            typeof(DocumentBuild).Assembly,
            typeof(DataProtectionOptions).Assembly,
        ];

        foreach (
            var hosted in services
                .Where(service =>
                    service.ServiceType == typeof(IHostedService)
                    && touchState.Contains(service.ImplementationType?.Assembly)
                )
                .ToList()
        )
            services.Remove(hosted);

        return services;
    }
}
