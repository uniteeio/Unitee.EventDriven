using Microsoft.Extensions.DependencyInjection;
using Unitee.EventDriven.RedisStream;

namespace Unitee.EventDriven.DependencyInjection;

public static class ServicesConfiguration
{
    public static void AddRedisStreamBackgroundReceiver(this IServiceCollection services, string serviceName)
    {
        services.AddHostedService(ctx =>
            new RedisStreamBackgroundReceiver(ctx, serviceName));
    }
}