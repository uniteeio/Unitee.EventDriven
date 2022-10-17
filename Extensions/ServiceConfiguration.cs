using Microsoft.Extensions.DependencyInjection;
using ServiceBus.Abstraction;
using ServiceBus.AzureServiceBus;

namespace ServiceBus.DependencyInjection;

public static class ServicesConfiguration
{
    public static void AddAzureServiceBus(this IServiceCollection services, string connectionString, string defaultTopic)
    {
        services.AddScoped<IAzureServiceBusPublisher, AzureServiceBusPublisher>(
            ctx => new AzureServiceBusPublisher(connectionString, defaultTopic));
    }
}