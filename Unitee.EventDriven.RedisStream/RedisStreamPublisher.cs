using System.Text.Json;
using Unitee.EventDriven.Abstraction;
using Unitee.EventDriven.Helpers;
using Unitee.EventDriven.Models;
using StackExchange.Redis;

namespace Unitee.RedisStream;


public interface IRedisStreamPublisher : IPublisher<RedisValue, string> { }

public class RedisStreamPublisher : IRedisStreamPublisher
{
    private readonly IConnectionMultiplexer _redis;

    public RedisStreamPublisher(IConnectionMultiplexer redis)
    {
        _redis = redis;
    }

    public Task CancelAsync(string sequence, string? topic = null)
    {
        throw new NotImplementedException();
    }

    public async Task<RedisValue> PublishAsync<TMessage>(TMessage message)
    {
        var db = _redis.GetDatabase();
        var subject = MessageHelper.GetSubject<TMessage>();
        return await db.StreamAddAsync(subject, "Body", JsonSerializer.Serialize(message));
    }

    public Task<RedisValue> PublishAsync<TMessage>(TMessage message, MessageOptions options)
    {
        throw new NotImplementedException();
    }

    public Task<U> RequestResponseAsync<T, U>(T message, MessageOptions options, ReplyOptions? replyOptions = null)
    {
        throw new NotImplementedException();
    }
}

