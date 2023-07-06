using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;
using Unitee.EventDriven.Abstraction;
using Unitee.EventDriven.Helpers;
using Unitee.EventDriven.Models;
using Unitee.EventDriven.ReadisStream.Configuration;
using Unitee.EventDriven.RedisStream.Models;

namespace Unitee.EventDriven.RedisStream;

public interface IRedisStreamPublisher : IPublisher<RedisValue, RedisValue> { }

public class RedisStreamPublisher : IRedisStreamPublisher
{
    private readonly IConnectionMultiplexer _redis;
    private readonly RedisStreamConfiguration _config;

    public RedisStreamPublisher(IConnectionMultiplexer redis, IServiceProvider services)
    {
        _redis = redis;
        _config = services.GetService<RedisStreamConfiguration>() ?? new RedisStreamConfiguration();
    }

    public async Task CancelAsync(RedisValue member, string? topic = null)
    {
        if (topic is not null)
        {
            throw new NotSupportedException("RedisStream does not support topic");
        }

        var db = _redis.GetDatabase();
        await db.SortedSetRemoveAsync("SCHEDULED_MESSAGES", member);
    }


    private async Task<RedisValue> InternalPublishAsync<TMessage>(TMessage message, string subject, MessageOptions options)
    {
        var payload = new List<NameValueEntry>();

        payload.Add(new("Body", JsonSerializer.Serialize(message, _config.JsonSerializerOptions)));

        if (options.SessionId is not null)
        {
            payload.Add(new("ReplyTo", $"{subject}_{options.SessionId}"));
        }

        if (options.ExpireAt is not null)
        {
            payload.Add(new("ExpireAt", options.ExpireAt.Value.ToUnixTimeMilliseconds().ToString()));
        }

        if (options.Locale is not null)
        {
            payload.Add(new("Locale", options.Locale));
        }

        var db = _redis.GetDatabase();
        var redisChannel = new RedisChannel(subject, RedisChannel.PatternMode.Literal);

        var res = await db.StreamAddAsync(subject, payload.ToArray(), maxLength: 100);

        await db.PublishAsync(redisChannel, "1");

        return res;
    }

    ///
    /// <summary>
    /// Scheduling consists of put the message in a sorted set with the scheduled time as score
    /// The value part consists of:
    ///   - a Guid to identify the message and then, cancel it
    ///   - the message body (serialized with the configured serializer)
    ///   - the subject
    ///
    /// The value part is serialized as Json with the default serializer
    ///
    /// </summary>
    ///
    /// <returns>
    ///   To remove a element from a set in redis, we need its value, here, the json payload.
    ///   We return it as an opaque RedisValue
    /// </returns>
    ///
    private async Task<RedisValue> InternalSchedule<TMessage>(TMessage body, string subject, MessageOptions options)
    {
            var serializedBody = JsonSerializer.Serialize(body, _config.JsonSerializerOptions);
            var value = new RedisStreamScheduledMessageType<TMessage>(Guid.NewGuid(), serializedBody, subject);
            var json = JsonSerializer.Serialize(value);

            var scheduledTime = options.ScheduledEnqueueTime!.Value!.ToUnixTimeMilliseconds();
            var db = _redis.GetDatabase();
            await db.SortedSetAddAsync("SCHEDULED_MESSAGES", json, scheduledTime);

            return json;
    }

    public async Task<RedisValue> PublishAsync<TMessage>(TMessage message)
    {
        var subject = MessageHelper.GetSubject<TMessage>();

        if (subject is null)
        {
            throw new ApplicationException("Cannot publish message without subject");
        }

        return await InternalPublishAsync(message, subject, new MessageOptions());
    }

    public async Task<RedisValue> PublishAsync<TMessage>(TMessage message, string subject)
    {
        return await InternalPublishAsync(message, subject, new MessageOptions());
    }

    public async Task<RedisValue> PublishAsync<TMessage>(TMessage message, MessageOptions options)
    {
        var subject = MessageHelper.GetSubject<TMessage>();

        if (subject is null)
        {
            throw new ApplicationException("Cannot publish message without subject");
        }

        if (options.ScheduledEnqueueTime is not null)
        {
            return await InternalSchedule(message, subject, options);
        }

        return await InternalPublishAsync(message, subject, options);
    }

    public async Task<U> RequestResponseAsync<T, U>(T body, MessageOptions? options = null, ReplyOptions? replyOptions = null)
    {
        var sessionId = options?.SessionId ?? Guid.NewGuid().ToString();
        var subject = MessageHelper.GetSubject<T>();
        var uniqueName = $"{subject}_{sessionId}";
        var db = _redis.GetDatabase();

        if (subject is null)
        {
            throw new ApplicationException("Cannot publish message without subject");
        }

        var promise = new TaskCompletionSource<U>();

        var redisChannel = new RedisChannel(uniqueName, RedisChannel.PatternMode.Literal);

        _redis.GetSubscriber().Subscribe(redisChannel, (channel, message) =>
        {
            var response = JsonSerializer.Deserialize<U>(message!, _config.JsonSerializerOptions);
            promise.TrySetResult(response!);
        });

        await InternalPublishAsync(body, subject, (options ?? new MessageOptions()) with { SessionId = sessionId });

        var completed = await Task.WhenAny(promise.Task, Task.Delay(replyOptions?.Timeout ?? TimeSpan.FromSeconds(5)));

        _redis.GetSubscriber().Unsubscribe(redisChannel);

        if (completed != promise.Task)
        {
            throw new TimeoutException();
        }

        var response = await promise.Task;
        return response;
    }
}

