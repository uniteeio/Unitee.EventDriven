using Microsoft.Extensions.DependencyInjection;
using Unitee.EventDriven.RedisStream;

namespace Unitee.EventDriven.DependencyInjection;

public static class ServicesConfiguration
{
    public static void AddRedisStreamBackgroundReceiver(this IServiceCollection services, string serviceName)
    {
        services.AddScoped(provider => new RedisStreamMessagesProcessor(serviceName, provider));

        services.AddScoped<RedisStreamMessageContextFactory>();

        services.AddHostedService(ctx =>
            new RedisStreamBackgroundReceiver(ctx));
    }
}