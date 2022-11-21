using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using Unitee.EventDriven.Abstraction;
using Unitee.EventDriven.Attributes;
using Unitee.EventDriven.Helpers;
using Unitee.EventDriven.RedisStream;
using Unitee.EventDriven.RedisStream.Models;

namespace Unitee.RedisStream;

public class RedisStreamMessagesProcessor
{
    private readonly IServiceProvider _services;
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<RedisStreamMessagesProcessor> _logger;
    private readonly string _serviceName;

    public RedisStreamMessagesProcessor(string serviceName, IServiceProvider services, IConnectionMultiplexer redis, ILogger<RedisStreamMessagesProcessor> logger)
    {
        _serviceName = serviceName;
        _services = services;
        _redis = redis;
        _logger = logger;
    }

    private async void RegisterConsumer<TMessage>(IRedisStreamConsumer<TMessage> _)
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

        await ProcessStreamEntries<TMessage>(oldMessages);

        _redis.GetSubscriber().Subscribe(subject, async (channel, value) =>
        {
            var db = _redis.GetDatabase();
            var streamEntries = await Read(subject);
            await ProcessStreamEntries<TMessage>(streamEntries);
        });
    }


    public void RegisterConsumers()
    {
        var consumers = _services.GetServices<IConsumer>();
        foreach (var c in consumers)
        {
            RegisterConsumer((dynamic)c);
        }
    }


#pragma warning restore CA1822

    private async Task ProcessStreamEntries<TMessage>(IEnumerable<StreamEntry> entries)
    {
        var db = _redis.GetDatabase();
        var subject = MessageHelper.GetSubject<TMessage>();

        foreach (var entry in entries)
        {
            var processed = ParseStreamEntry<TMessage>(entry);

            if (processed.Item1 is not null && processed.Item2 is not null)
            {
                using var scope = _services.CreateScope();
                var consumers = scope.ServiceProvider.GetServices<IConsumer>();
                var matchedConsumers =
                    consumers.Where(c => MessageHelper.GetSubject(c.GetType().GetInterface("IRedisStreamConsumer`1")?.GenericTypeArguments?[0]) == MessageHelper.GetSubject<TMessage>())
                    .ToList();

                foreach (var consumer in matchedConsumers)
                {
                    try
                    {
                        await TryConsume<TMessage>((dynamic)consumer, (TMessage)processed.Item2);
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

#pragma warning disable CA1822
    private async Task<bool> TryConsume<TMessage>(IRedisStreamConsumer<TMessage> consumer, TMessage message)
    {
        _logger.LogDebug("Consuming message {} with subject: {}", message, MessageHelper.GetSubject<TMessage>());
        await consumer.ConsumeAsync(message);
        return true;
    }
#pragma warning restore CA1822

    public async Task<int> ReadAndPublishScheduledMessagesAsync()
    {
        var db = _redis.GetDatabase();

        var topMessages = await db.SortedSetRangeByRankWithScoresAsync("SCHEDULED_MESSAGES", start: 0, stop: 0);

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
                        // find the right event POCO class in the assembly in order to publish it
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

                        // build the message
                        var body = JsonSerializer.Deserialize((JsonElement)concreteBodyType, type);

                        // publish the message
                        var publisher = _services.GetRequiredService<IRedisStreamPublisher>();

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

                return 1;
            }
        }
        return 0;
    }
}