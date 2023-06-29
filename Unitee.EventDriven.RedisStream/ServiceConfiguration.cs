using Microsoft.Extensions.DependencyInjection;
using Unitee.EventDriven.ReadisStream.Configuration;
using Unitee.EventDriven.RedisStream;

namespace Unitee.EventDriven.DependencyInjection;

public static class ServicesConfiguration
{
    public static void AddRedisStreamBackgroundReceiver(this IServiceCollection services, string serviceName, string deadLetterQueueName = "DEAD_LETTER", string instanceName = "Default")
    {
        services.AddScoped(provider => new RedisStreamMessagesProcessor(serviceName, instanceName, deadLetterQueueName, provider));
        services.AddScoped<RedisStreamMessageContextFactory>();

        services.AddHostedService(ctx =>
            new RedisStreamBackgroundReceiver(ctx));
    }

    public static void AddRedisStreamPublisher(this IServiceCollection services)
    {
        services.AddScoped<IRedisStreamPublisher, RedisStreamPublisher>();
    }

    public static void AddRedisStreamOptions(this IServiceCollection services, Action<RedisStreamConfiguration> configure)
    {
        var opts = new RedisStreamConfiguration();
        configure(opts);
        services.AddSingleton<RedisStreamConfiguration>(opts);
    }
}
