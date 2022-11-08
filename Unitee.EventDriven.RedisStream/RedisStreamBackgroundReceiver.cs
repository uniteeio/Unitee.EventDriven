using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Unitee.EventDriven.Abstraction;
using Unitee.EventDriven.Helpers;
using StackExchange.Redis;
using Unitee.EventDriven.RedisStream;
using Unitee.EventDriven.RedisStream.Models;
using Unitee.EventDriven.Attributes;
using Unitee.RedisStream;

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
        catch (RedisServerException e) when (e.Message.StartsWith("BUSYGROUP"))
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
            var db = _redis.GetDatabase();

            var topMessages = await db.SortedSetRangeByRankWithScoresAsync("SCHEDULED_MESSAGES", 0, 0);

            if (topMessages is not { Length: 0 })
            {
                var topMessage = topMessages[0];
                var now = DateTimeOffset.Now.ToUnixTimeMilliseconds();
                if (now >= topMessage.Score && topMessage.Element.HasValue is true && db.LockQuery($"LOCK:{topMessage.Element}").HasValue is false)
                {
                    try
                    {
                        await db.LockTakeAsync($"LOCK:{topMessage.Element}", _serviceName, TimeSpan.FromSeconds(30));

                        var distance = TimeSpan.FromMilliseconds(now - topMessage.Score);
                        _logger.LogInformation("Processing delayed message with delayed time {Distance}", distance);

#pragma warning disable CS8604
                        var message = JsonSerializer.Deserialize<RedisStreamScheduledMessageType<object>>(topMessage.Element);
#pragma warning restore CS8604

                        if (message?.Subject is not null)
                        {
                            // find the right class in the assembly
                            var typesWithMyAttribute =
                                from a in AppDomain.CurrentDomain.GetAssemblies()
                                from t in a.GetTypes()
                                let attributes = t.GetCustomAttributes(typeof(SubjectAttribute), true)
                                where attributes != null && attributes.Length > 0
                                where attributes.Cast<SubjectAttribute>().FirstOrDefault()?.Subject == message.Subject
                                select new { Type = t };

                            var type = typesWithMyAttribute.SingleOrDefault()?.Type;

                            if (type is null)
                            {
                                throw new CannotHandleScheduledMessageException("A scheduled message was found but a class with the subject was not found in the assembly. So the message has been ignored.");
                            }

                            JsonElement? concreteBodyType = message.Body as JsonElement?;
                            if (concreteBodyType is null)
                            {
                                throw new CannotHandleScheduledMessageException("Body cannot be null. As a workaround, you can use an empty object instead.");
                            }

                            var body = JsonSerializer.Deserialize((JsonElement)concreteBodyType, type);


                            var publisher = scope.ServiceProvider.GetRequiredService<IRedisStreamPublisher>();

                            var task = (Task?)publisher
                                .GetType()
                                .GetMethods()
                                .Where(x => x.Name == "PublishAsync" && x.GetParameters().Length == 1)
                                .Single()
                                .MakeGenericMethod(type)
                                .Invoke(publisher, new[] { body });

                            if (task is not null)
                                await task;
                            else
                            {
                                throw new CannotHandleScheduledMessageException("We didn't find any suitable method to publish the message.");
                            }
                        }
                    }
                    catch (CannotHandleScheduledMessageException e)
                    {
                        _logger.LogWarning("{}", e.Message);
                    }
                    catch (Exception e)
                    {
                        _logger.LogError(e, "{}", e.Message);
                    }
                    finally
                    {
                        await db.SortedSetRemoveAsync("SCHEDULED_MESSAGES", topMessage.Element);
                        await db.LockReleaseAsync($"LOCK:{topMessage.Element}", _serviceName);
                    }
                }
            }

            await Task.Delay(3000, stoppingToken);
        }
    }
}