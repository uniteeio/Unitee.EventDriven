using System.Text.Json;
using CSRedis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ServiceBus.Abstraction;
using ServiceBus.Helpers;
using StackExchange.Redis;

namespace ServiceBus.DependencyInjection;

public class RedisStreamBackgroundReceiver<TConsumer> : BackgroundService where TConsumer : class, IConsumer
{
    private readonly IConnectionMultiplexer _redis;
    private readonly CSRedisClient _csRedisClient;
    private readonly ILogger<RedisStreamBackgroundReceiver<TConsumer>> _logger;
    private readonly string _serviceName;
    private readonly IServiceProvider _services;

    public RedisStreamBackgroundReceiver(IServiceProvider services, string serviceName)
    {
        _services = services;
        _redis = services.GetRequiredService<IConnectionMultiplexer>();
        _csRedisClient = services.GetRequiredService<CSRedisClient>();
        _logger = services.GetRequiredService<ILogger<RedisStreamBackgroundReceiver<TConsumer>>>();
        _serviceName = serviceName;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var TMessage = typeof(TConsumer).GetInterface("IConsumer`1")?.GenericTypeArguments[0];

        ArgumentNullException.ThrowIfNull(TMessage);

        var db = _redis.GetDatabase();
        var subject = MessageHelper.GetSubject<TConsumer>();

        try
        {
            await db.StreamCreateConsumerGroupAsync(subject, _serviceName, StreamPosition.NewMessages, createStream: true);
        }
        catch (RedisServerException) { }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var result = _csRedisClient.XReadGroup(_serviceName, "Default", 1, 3000, new (string key, string id)[] { new(subject, ">") });

                if (result is null)
                {
                    continue;
                }

                var (streamName, messages) = result.First();
                var (id, kv) = messages[0];
                var body = kv[1];

                using var scope = _services.CreateScope();
                var consumer = scope.ServiceProvider.GetRequiredService<TConsumer>() as IConsumer;

                var deserialized = JsonSerializer.Deserialize(body, TMessage);

                try
                {
                    Task? t = consumer.GetType().GetMethod("ConsumeAsync")?.Invoke(consumer, new[] { deserialized }) as Task;
                    if (t is not null)
                    {
                        await t;
                        db.StreamAcknowledge(subject, _serviceName, id);
                    }
                }
                catch (Exception e)
                {
                    _logger.LogError(e, "An exception occured in the consumer {}", consumer.GetType().Name);
                }
            }
            catch (CSRedis.RedisException)
            {
                await db.StreamCreateConsumerGroupAsync(subject, _serviceName, StreamPosition.NewMessages, createStream: true);
            }
        }
    }
}