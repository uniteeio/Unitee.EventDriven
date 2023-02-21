using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using Unitee.EventDriven.Abstraction;
using Unitee.EventDriven.Attributes;
using Unitee.EventDriven.Helpers;
using Unitee.EventDriven.RedisStream.Events;
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
    private readonly IRedisStreamPublisher _publisher;
    private readonly string _serviceName;
    private readonly string _instanceName;
    private readonly string _deadLetterQueueName;

    public RedisStreamMessagesProcessor(string serviceName, string instanceName, string deadLetterQueueName, IServiceProvider services)
    {
        _serviceName = serviceName;
        _services = services;
        _scopeFactory = services.GetRequiredService<IServiceScopeFactory>();
        _redis = services.GetRequiredService<IConnectionMultiplexer>();
        _logger = services.GetRequiredService<ILogger<RedisStreamMessagesProcessor>>();
        _msgContextFactory = services.GetRequiredService<RedisStreamMessageContextFactory>();
        _publisher = services.GetRequiredService<IRedisStreamPublisher>();
        _instanceName = instanceName;
        _deadLetterQueueName = deadLetterQueueName;
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

        ProcessStreamEntries<TMessage>(oldMessages);

        _redis.GetSubscriber().Subscribe(subject, async (channel, value) =>
        {
            var db = _redis.GetDatabase();
            var streamEntries = await Read(subject);
            ProcessStreamEntries<TMessage>(streamEntries);
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
        var consumers = _services.GetServices<IConsumer>();
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

    private void ProcessStreamEntries<TMessage>(IEnumerable<StreamEntry> entries)
    {
        var db = _redis.GetDatabase();
        var subject = MessageHelper.GetSubject<TMessage>();

        foreach (var entry in entries)
        {
            _ = Task.Run(async () =>
            {
                var processed = ParseStreamEntry<TMessage>(entry);

                if (processed.Id is not null && processed.Body is not null)
                {
                    var consumers = _services.GetServices<IConsumer>();

                    var matchedConsumers =
                        consumers.Where(c => MessageHelper.GetSubject(c.GetType()?.GetInterface("IRedisStreamConsumer`1")?.GenericTypeArguments?[0]) == MessageHelper.GetSubject<TMessage>())
                        .ToList();

                    var taskList = new List<Task>();

                    foreach (var consumer in matchedConsumers)
                    {
                        var task = Task.Run(async () =>
                        {
                            try
                            {
                                await TryConsume<TMessage>((dynamic)consumer, (TMessage)processed.Body);
                                await db.StreamAcknowledgeAsync(subject, _serviceName, processed.Id);
                            }
                            catch (Exception e)
                            {
                                // don't push to dead letter queue if the dead letter queue handler has a problem
                                if (subject != _deadLetterQueueName && subject is not null && _publisher is not null)
                                {
                                    await _publisher.PublishAsync(new DeadLetterEvent(subject, processed.Body, e.ToString()), _deadLetterQueueName);
                                }

                                // throwing stop the background service
                                _logger.LogError(e, "Error while processing message");
                            }
                        });

                        taskList.Add(task);
                    }

                    var matchedConsumersWithContext = consumers
                        .Where(c => MessageHelper.GetSubject(
                            c.GetType()?.GetInterface("IRedisStreamConsumerWithContext`1")?.GenericTypeArguments?[0]) == MessageHelper.GetSubject<TMessage>())
                        .ToList();

                    foreach (var consumer in matchedConsumersWithContext)
                    {
                        var task = Task.Run(async () =>
                        {
                            try
                            {
                                await TryConsumeWithContext<TMessage>((dynamic)consumer, (TMessage)processed.Body, processed.ReplyTo);
                                await db.StreamAcknowledgeAsync(subject, _serviceName, processed.Id);
                            }
                            catch (Exception e)
                            {
                                // don't push to dead letter queue if the dead letter queue handler has a problem
                                if (subject != _deadLetterQueueName && subject is not null)
                                {
                                    await _publisher.PublishAsync(new DeadLetterEvent(subject, processed.Body, e.ToString()), _deadLetterQueueName);
                                }

                                // throwing stop the background service
                                _logger.LogError(e, "Error while processing message");
                            }
                        });

                        taskList.Add(task);
                    }

                    // not sure we need that
                    await Task.WhenAll(taskList);
                }
            });
        }
    }

    private async Task<StreamEntry[]> Read(string subject)
    {
        var db = _redis.GetDatabase();
        try
        {
            return await db.StreamReadGroupAsync(subject, _serviceName, _instanceName, ">");
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

        return new ParsedStreamEntry(entry.Id, JsonSerializer.Deserialize<TMessage>(body!), replyTo);
    }

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

    private IEnumerable<RedisKey> GetKeys(string pattern)
    {
        foreach (var endpoint in _redis.GetEndPoints())
        {
            foreach (var key in _redis.GetServer(endpoint).Keys(pattern: pattern))
            {
                yield return key;
            }
        }
    }

    public async Task ExecuteCrons()
    {
        var db = _redis.GetDatabase();

        var lockTaken = db.LockTake("CRON_LOCK", "1", TimeSpan.FromSeconds(10));

        if (!lockTaken)
        {
            return;
        }

        var cron = GetKeys("Cron:Schedule:*");


        foreach (var key in cron)
        {
            var values = db.HashGetAll(key);

            var expression = values[0];
            var queueName = values[1];

            var cronName = key.ToString().Split(":").Last();
            var parsed = Cronos.CronExpression.Parse(expression.Value);
            var previous = parsed.GetOccurrences(DateTime.UtcNow.AddMinutes(-10), DateTime.UtcNow).Reverse().FirstOrDefault();

            if (previous == default)
            {
                continue;
            }

            var alreadyExecutedLastElement = db.StreamRange($"Cron:ExecutionHistory:{cronName}", "-", "+", 1, Order.Descending);

            if (alreadyExecutedLastElement.Length is not 0)
            {
                var lastExecutedTimestamp = alreadyExecutedLastElement.First().Values[0].Value.ToString();
                var toExecuteTimeStamp = previous.ToString("yyyyMMddHHmmssffff");
                if (lastExecutedTimestamp.ToString() == toExecuteTimeStamp)
                {
                    continue;
                }
            }

            // Execute
            db.StreamAdd($"Cron:ExecutionHistory:{cronName}", new NameValueEntry[] { new("ExecutedAt", previous.ToString("yyyyMMddHHmmssffff")) }, maxLength: 100);
            db.StreamAdd(queueName.Value.ToString(), "Body", "{}", maxLength: 100);
            db.Publish(queueName.Value.ToString(), "1");
        }

        await db.LockReleaseAsync("CRON_LOCK", "1");
    }

    public async Task<int> ReadAndPublishScheduledMessagesAsync()
    {
        var db = _redis.GetDatabase();

        using var services = _scopeFactory.CreateScope();

        var lockTaken = db.LockTake("SCHEDULED_MESSAGES_LOCK", "1", TimeSpan.FromSeconds(10));

        if (!lockTaken)
        {
            return 0;
        }

        var topMessages = await db.SortedSetRangeByRankWithScoresAsync("SCHEDULED_MESSAGES", start: 0, stop: 0);

        if (topMessages is not { Length: 0 })
        {
            var topMessage = topMessages[0];
            var now = DateTimeOffset.Now.ToUnixTimeMilliseconds();
            if (now >= topMessage.Score && topMessage.Element.HasValue is true)
            {
                topMessage = (await db.SortedSetPopAsync("SCHEDULED_MESSAGES")).Value;

                // once we have popped the message, we can release the lock
                await db.LockReleaseAsync("SCHEDULED_MESSAGES_LOCK", "1");

                try
                {
                    var distance = TimeSpan.FromMilliseconds(now - topMessage.Score);
                    _logger.LogInformation("Processing delayed message with delayed time {Distance}", distance);

                    var message = JsonSerializer.Deserialize<RedisStreamScheduledMessageType<object>>(topMessage!.Element!);

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
                    _logger.LogWarning("{message}", e.Message);
                }
                catch (Exception e)
                {
                    _logger.LogError(e, "{message}", e.Message);
                }

                return 1;
            }
        }

        // release lock before returning (maybe it already has been released)
        await db.LockReleaseAsync("SCHEDULED_MESSAGES_LOCK", "1");
        return 0;
    }
}