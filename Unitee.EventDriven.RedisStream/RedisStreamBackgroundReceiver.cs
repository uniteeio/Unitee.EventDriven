using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Unitee.EventDriven.Abstraction;
using Unitee.EventDriven.Helpers;
using StackExchange.Redis;
using Unitee.EventDriven.RedisStream;

namespace Unitee.EventDriven.DependencyInjection;

public class RedisStreamBackgroundReceiver : BackgroundService
{
    private readonly ILogger<RedisStreamBackgroundReceiver> _logger;
    private readonly string _serviceName;
    private readonly IConnectionMultiplexer _redis;
    private readonly IServiceProvider _services;

    public RedisStreamBackgroundReceiver(IServiceProvider services, string serviceName)
    {
        _services = services;
        _logger = services.GetRequiredService<ILogger<RedisStreamBackgroundReceiver>>();
        _serviceName = serviceName;
        _redis = services.GetRequiredService<IConnectionMultiplexer>();
    }

    private static (string?, object?) ParseStreamEntry<TMessage>(StreamEntry entry)
    {
        var body = entry["Body"];

        if (body.IsNull)
        {
            return (null, null);
        }

#pragma warning disable CS8604
        return (entry.Id, JsonSerializer.Deserialize<TMessage>(body));
#pragma warning restore CS8604
    }

    private async void ProcessStreamEntries<TMessage>(IEnumerable<StreamEntry> entries, IConsumer<TMessage> consumer)
    {
        var db = _redis.GetDatabase();
        var subject = MessageHelper.GetSubject<TMessage>();

        foreach (var entry in entries)
        {
            var processed = ParseStreamEntry<TMessage>(entry);

            if (processed.Item1 is not null && processed.Item2 is not null)
            {
                try
                {
                    await consumer.ConsumeAsync((TMessage)processed.Item2);
                    await db.StreamAcknowledgeAsync(subject, _serviceName, processed.Item1);
                }
                catch (Exception e)
                {
                    // throwing stop the background service
                    _logger.LogError(e, "Error while processing message");
                }
            }
        }
    }

    private async Task<StreamEntry[]> Read(string subject)
    {
        var db = _redis.GetDatabase();
        try
        {
            return await db.StreamReadGroupAsync(subject, _serviceName, "Default", ">");
        }
        catch (RedisException e) when (e.Message.StartsWith("NOGROUP"))
        {
            await db.StreamCreateConsumerGroupAsync(subject, _serviceName, StreamPosition.NewMessages);
            return await Read(subject);
        }
    }

    private async void Register<TMessage>(IRedisStreamConsumer<TMessage> consumer)
    {
        var db = _redis.GetDatabase();
        var subject = MessageHelper.GetSubject<TMessage>();

        try
        {
            await db.StreamCreateConsumerGroupAsync(subject, _serviceName, StreamPosition.NewMessages);
        }
        catch (StackExchange.Redis.RedisServerException e) when (e.Message.StartsWith("BUSYGROUP"))
        { }

        var oldMessages = await Read(subject);

        ProcessStreamEntries(oldMessages, consumer);

        _redis.GetSubscriber().Subscribe(subject, async (channel, value) =>
        {
            var db = _redis.GetDatabase();
            var streamEntries = await Read(subject);
            ProcessStreamEntries(streamEntries, consumer);
        });
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();

        using var scope = _services.CreateScope();

        var consumers = scope.ServiceProvider.GetServices<IConsumer>();
        foreach (var c in consumers)
        {
            Register((dynamic)c);
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(3000, stoppingToken);
        }
    }
}