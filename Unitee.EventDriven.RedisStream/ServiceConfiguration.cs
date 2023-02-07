using Microsoft.Extensions.DependencyInjection;
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
}