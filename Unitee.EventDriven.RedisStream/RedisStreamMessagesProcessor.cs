using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using Unitee.EventDriven.Abstraction;
using Unitee.EventDriven.Attributes;
using Unitee.EventDriven.Helpers;
using Unitee.EventDriven.RedisStream.Models;

namespace Unitee.EventDriven.RedisStream;

public record ParsedStreamEntry(string? Id, object? Body, string? ReplyTo);

public class RedisStreamMessagesProcessor
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConnectionMultiplexer _redis;
    private readonly IServiceProvider _services;
    private readonly ILogger<RedisStreamMessagesProcessor> _logger;
    private readonly RedisStreamMessageContextFactory _msgContextFactory;
    private readonly string _serviceName;

    public RedisStreamMessagesProcessor(string serviceName, IServiceProvider services)
    {
        _serviceName = serviceName;
        _services = services;
        _scopeFactory = services.GetRequiredService<IServiceScopeFactory>();
        _redis = services.GetRequiredService<IConnectionMultiplexer>();
        _logger = services.GetRequiredService<ILogger<RedisStreamMessagesProcessor>>();
        _msgContextFactory = services.GetRequiredService<RedisStreamMessageContextFactory>();
    }

    private async Task InnerRegisterConsumer<TMessage>()
    {
        var db = _redis.GetDatabase();
        var subject = MessageHelper.GetSubject<TMessage>();

        if (subject is null)
            throw new ApplicationException("found a consumer without a subject.");

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

    private async void RegisterConsumer<TMessage>(IRedisStreamConsumer<TMessage> _)
    {
        await InnerRegisterConsumer<TMessage>();
    }

    private async void RegisterConsumerWithContext<TMessage>(IRedisStreamConsumerWithContext<TMessage> _)
    {
        await InnerRegisterConsumer<TMessage>();
    }

    public void RegisterConsumers()
    {
        var scope = _scopeFactory.CreateScope();
        var consumers = scope.ServiceProvider.GetServices<IConsumer>();
        foreach (var c in consumers)
        {
            try
            {
                RegisterConsumer((dynamic)c);
            }
            catch { }

            try
            {
                RegisterConsumerWithContext((dynamic)c);
            }
            catch { }
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

            if (processed.Id is not null && processed.Body is not null)
            {
                using var scope = _scopeFactory.CreateScope();
                var consumers = scope.ServiceProvider.GetServices<IConsumer>();

                var matchedConsumers =
                    consumers.Where(c => MessageHelper.GetSubject(c.GetType()?.GetInterface("IRedisStreamConsumer`1")?.GenericTypeArguments?[0]) == MessageHelper.GetSubject<TMessage>())
                    .ToList();

                foreach (var consumer in matchedConsumers)
                {
                    try
                    {
                        await TryConsume<TMessage>((dynamic)consumer, (TMessage)processed.Body);
                        await db.StreamAcknowledgeAsync(subject, _serviceName, processed.Id);
                    }
                    catch (Exception e)
                    {
                        // throwing stop the background service
                        _logger.LogError(e, "Error while processing message");
                    }
                }

                var matchedConsumersWithContext =  consumers
                    .Where(c => MessageHelper.GetSubject(
                        c.GetType()?.GetInterface("IRedisStreamConsumerWithContext`1")?.GenericTypeArguments?[0]) == MessageHelper.GetSubject<TMessage>())
                    .ToList();

                foreach (var consumer in matchedConsumersWithContext)
                {
                    try
                    {
                        await TryConsumeWithContext<TMessage>((dynamic)consumer, (TMessage)processed.Body, processed.ReplyTo);
                        await db.StreamAcknowledgeAsync(subject, _serviceName, processed.Id);
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

    private static ParsedStreamEntry ParseStreamEntry<TMessage>(StreamEntry entry)
    {
        var body = entry["Body"];
        var replyTo = entry["ReplyTo"];

        if (body.IsNull)
        {
            return new ParsedStreamEntry(entry.Id, null, replyTo);
        }

#pragma warning disable CS8604
        return new ParsedStreamEntry(entry.Id, JsonSerializer.Deserialize<TMessage>(body), replyTo);
#pragma warning restore CS8604
    }

#pragma warning disable CA1822
    private async Task<bool> TryConsume<TMessage>(IRedisStreamConsumer<TMessage> consumer, TMessage message)
    {
        _logger.LogDebug("Consuming message {} with subject: {}", message, MessageHelper.GetSubject<TMessage>());
        await consumer.ConsumeAsync(message);
        return true;
    }

    private async Task<bool> TryConsumeWithContext<TMessage>(IRedisStreamConsumerWithContext<TMessage> consumer, TMessage message, string replyTo)
    {
        _logger.LogDebug("Consuming message with context {} with subject: {}", message, MessageHelper.GetSubject<TMessage>());
        var ctx = _msgContextFactory.Create(replyTo);
        await consumer.ConsumeAsync(message, ctx);
        return true;
    }
#pragma warning restore CA1822

    public async Task<int> ReadAndPublishScheduledMessagesAsync()
    {
        var db = _redis.GetDatabase();

        using var services = _scopeFactory.CreateScope();

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
                        var body = ((JsonElement)concreteBodyType).Deserialize(type);

                        // publish the message
                        var publisher = services.ServiceProvider.GetRequiredService<IRedisStreamPublisher>();

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