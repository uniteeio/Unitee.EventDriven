using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using Unitee.EventDriven.RedisStream;
using Unitee.RedisStream;

namespace Unitee.EventDriven.DependencyInjection;

public static class ServicesConfiguration
{
    public static void AddRedisStreamBackgroundReceiver(this IServiceCollection services, string serviceName)
    {
        services.AddScoped(provider => new RedisStreamMessagesProcessor(serviceName, provider, provider.GetRequiredService<IConnectionMultiplexer>(), provider.GetRequiredService<ILogger<RedisStreamMessagesProcessor>>()));

        services.AddHostedService(ctx =>
            new RedisStreamBackgroundReceiver(ctx, serviceName));
    }
}