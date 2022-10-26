using Microsoft.Extensions.DependencyInjection;
using Unitee.EventDriven.Abstraction;
using Unitee.EventDriven.AzureServiceBus;

namespace Unitee.EventDriven.DependencyInjection;

public static class ServicesConfiguration
{
    public static void AddAzureServiceBus(this IServiceCollection services, string connectionString, string defaultTopic)
    {
        services.AddScoped<IAzureServiceBusPublisher, AzureServiceBusPublisher>(
            ctx => new AzureServiceBusPublisher(connectionString, defaultTopic));
    }

    public static void AddBackgroundReceiver(this IServiceCollection services, string connectionString, string queue)
    {
        services.AddHostedService(ctx =>
            new AzureServiceBusBackgroundReceiver(ctx, connectionString, queue));
    }

    public static void AddAzureServiceBusBackgroundReceiver(this IServiceCollection services, string connectionString, string topic, string subscription)
    {
        services.AddHostedService(ctx =>
            new AzureServiceBusBackgroundReceiver(ctx, connectionString, topic, subscription));
    }
}