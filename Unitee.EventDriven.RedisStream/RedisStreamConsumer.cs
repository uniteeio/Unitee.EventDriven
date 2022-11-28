using System.Text.Json;
using StackExchange.Redis;
using Unitee.EventDriven.Abstraction;

namespace Unitee.EventDriven.RedisStream;

public interface IRedisStreamConsumer<TMessage> : IConsumer<TMessage> { }

public interface IRedisStreamMessageContext : IMessageContext<bool> { };

public interface IRedisStreamConsumerWithContext<TMessage> : IConsumerWithContext<TMessage, IRedisStreamMessageContext> { }

public class RedisStreamMessageContext : IRedisStreamMessageContext
{
    private readonly IRedisStreamPublisher _publisher;
    private readonly string _replyTo;
    private readonly IConnectionMultiplexer _redis;

    public RedisStreamMessageContext(IRedisStreamPublisher publisher, IConnectionMultiplexer redis, string replyTo)
    {
        _publisher = publisher;
        _replyTo = replyTo;
        _redis = redis;
    }

    public async Task<bool> ReplyAsync<TMessage>(TMessage message)
    {
        await _redis.GetSubscriber().PublishAsync(_replyTo, JsonSerializer.Serialize(message));
        return true;
    }
}