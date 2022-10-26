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

    public static void RegisterRedisStreamConsumer<TConsumer>(this IServiceCollection services, string serviceName)
        where TConsumer : class, IConsumer
    {
        services.AddScoped<TConsumer>();

        services.AddHostedService(ctx =>
            new RedisStreamBackgroundReceiver<TConsumer>(ctx, serviceName));
    }
}