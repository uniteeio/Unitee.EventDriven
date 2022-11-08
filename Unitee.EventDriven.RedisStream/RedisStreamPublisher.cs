using System.Text.Json;
using StackExchange.Redis;
using Unitee.EventDriven.Abstraction;
using Unitee.EventDriven.Helpers;
using Unitee.EventDriven.Models;
using Unitee.EventDriven.RedisStream.Models;

namespace Unitee.RedisStream;


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
        var db = _redis.GetDatabase();
        var res =await db.StreamAddAsync(subject, "Body", JsonSerializer.Serialize(message), maxLength: 100);
        await db.PublishAsync(subject, "");
        return res;
    }

    public async Task<RedisValue> PublishAsync<TMessage>(TMessage message, MessageOptions options)
    {
        if (options.ScheduledEnqueueTime is null)
        {
            return await PublishAsync(message);
        }

        var db = _redis.GetDatabase();

        var member = new RedisStreamScheduledMessageType<TMessage>(Guid.NewGuid(), message, MessageHelper.GetSubject<TMessage>());

        var json = JsonSerializer.Serialize(member);
        var scheduledTime = options.ScheduledEnqueueTime.Value.ToUnixTimeMilliseconds();

        await db.SortedSetAddAsync("SCHEDULED_MESSAGES", json, scheduledTime);

        return json;
    }

    public Task<U> RequestResponseAsync<T, U>(T message, MessageOptions options, ReplyOptions? replyOptions = null)
    {
        throw new NotImplementedException();
    }
}

