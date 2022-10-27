using Microsoft.Extensions.DependencyInjection;

namespace Unitee.EventDriven.DependencyInjection;

public static class ServicesConfiguration
{
    public static void AddRedisStreamBackgroundReceiver(this IServiceCollection services, string serviceName)
    {
        services.AddHostedService(ctx =>
            new RedisStreamBackgroundReceiver(ctx, serviceName));
    }
}