using System.Text.Json;
using StackExchange.Redis;
using Unitee.EventDriven.Abstraction;
using Unitee.EventDriven.Helpers;
using Unitee.EventDriven.Models;
using Unitee.EventDriven.RedisStream.Models;

namespace Unitee.EventDriven.RedisStream;

public interface IRedisStreamPublisher : IPublisher<RedisValue, RedisValue> { }

public class RedisStreamPublisher : IRedisStreamPublisher
{
    private readonly IConnectionMultiplexer _redis;

    public RedisStreamPublisher(IConnectionMultiplexer redis)
    {
        _redis = redis;
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

    public async Task<RedisValue> PublishAsync<TMessage>(TMessage message)
    {
        var subject = MessageHelper.GetSubject<TMessage>();

        if (subject is null)
        {
            throw new ApplicationException("Cannot publish message without subject");
        }

        return await PublishAsync(message, subject);
    }

    public async Task<RedisValue> PublishAsync<TMessage>(TMessage message, string subject)
    {
        var db = _redis.GetDatabase();
        var res = await db.StreamAddAsync(subject, "Body", JsonSerializer.Serialize(message), maxLength: 100);
        await db.PublishAsync(subject, "");
        return res;
    }

    public async Task<RedisValue> PublishAsync<TMessage>(TMessage message, MessageOptions options)
    {
        var db = _redis.GetDatabase();
        var subject = MessageHelper.GetSubject<TMessage>();
        if (subject is null)
        {
            throw new ApplicationException("Cannot publish message without subject");
        }

        if (options.SessionId is not null)
        {
            var res = await db.StreamAddAsync(subject, new NameValueEntry[] { new("Body", JsonSerializer.Serialize(message)), new("ReplyTo", $"{subject}_{options.SessionId}") }, maxLength: 100);
            await db.PublishAsync(subject, "");
            return res;
        }

        if (options.ScheduledEnqueueTime is null)
        {
            return await PublishAsync(message);
        }

        var member = new RedisStreamScheduledMessageType<TMessage>(Guid.NewGuid(), message, subject);

        var json = JsonSerializer.Serialize(member);
        var scheduledTime = options.ScheduledEnqueueTime.Value.ToUnixTimeMilliseconds();

        await db.SortedSetAddAsync("SCHEDULED_MESSAGES", json, scheduledTime);

        return json;
    }


    public async Task<U> RequestResponseAsync<T, U>(T message, MessageOptions options, ReplyOptions? replyOptions = null)
    {
        var sessionId = options.SessionId ?? Guid.NewGuid().ToString();
        var subject = MessageHelper.GetSubject<T>();
        var uniqueName = $"{subject}_{sessionId}";
        var db = _redis.GetDatabase();

        var promise = new TaskCompletionSource<U>();

        _redis.GetSubscriber().Subscribe(uniqueName, (channel, message) =>
        {
            var response = JsonSerializer.Deserialize<U>(message!);
            promise.TrySetResult(response!);
        });

        await PublishAsync(message, new MessageOptions
        {
            SessionId = sessionId
        });

        var completed = await Task.WhenAny(promise.Task, Task.Delay(replyOptions?.Timeout ?? TimeSpan.FromSeconds(5)));

        _redis.GetSubscriber().Unsubscribe(uniqueName);

        if (completed != promise.Task)
        {
            throw new TimeoutException();
        }

        var response = await promise.Task;
        return response;
    }
}

