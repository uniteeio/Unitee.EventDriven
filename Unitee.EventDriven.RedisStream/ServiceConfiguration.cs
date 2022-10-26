using Microsoft.Extensions.DependencyInjection;
using Unitee.EventDriven.Abstraction;

namespace Unitee.EventDriven.DependencyInjection;

public static class ServicesConfiguration
{
    public static void RegisterRedisStreamConsumer<TConsumer>(this IServiceCollection services, string serviceName)
        where TConsumer : class, IConsumer
    {
        services.AddScoped<TConsumer>();

        services.AddHostedService(ctx =>
            new RedisStreamBackgroundReceiver<TConsumer>(ctx, serviceName));
    }
}