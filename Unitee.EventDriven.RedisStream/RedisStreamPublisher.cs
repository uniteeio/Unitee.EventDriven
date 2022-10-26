using System.Text.Json;
using FreeRedis;
using Unitee.EventDriven.Abstraction;
using Unitee.EventDriven.Helpers;
using Unitee.EventDriven.Models;

namespace Unitee.RedisStream;


public interface IRedisStreamPublisher : IPublisher<string, string> { }

public class RedisStreamPublisher : IRedisStreamPublisher
{
    private readonly RedisClient _redis;

    public RedisStreamPublisher(RedisClient redis)
    {
        _redis = redis;
    }

    public Task CancelAsync(string sequence, string? topic = null)
    {
        throw new NotImplementedException();
    }

    public async Task<string> PublishAsync<TMessage>(TMessage message)
    {
        var subject = MessageHelper.GetSubject<TMessage>();
        return await _redis.XAddAsync(subject, "Body", message);
    }

    public Task<string> PublishAsync<TMessage>(TMessage message, MessageOptions options)
    {
        throw new NotImplementedException();
    }

    public Task<U> RequestResponseAsync<T, U>(T message, MessageOptions options, ReplyOptions? replyOptions = null)
    {
        throw new NotImplementedException();
    }
}

