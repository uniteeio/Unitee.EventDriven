using System.Text.Json;
using StackExchange.Redis;
using Unitee.EventDriven.Abstraction;

namespace Unitee.EventDriven.RedisStream;

public interface IRedisStreamConsumer<TMessage> : IConsumer<TMessage> { }

public interface IRedisStreamMessageContext : IMessageContext<bool> { };

public interface IRedisStreamConsumerWithContext<TMessage> : IConsumerWithContext<TMessage, IRedisStreamMessageContext> { }

public class RedisStreamMessageContext : IRedisStreamMessageContext
{
    private readonly string _replyTo;
    private readonly IConnectionMultiplexer _redis;

    public RedisStreamMessageContext(IRedisStreamPublisher _, IConnectionMultiplexer redis, string replyTo)
    {
        _replyTo = replyTo;
        _redis = redis;
    }

    public async Task<bool> ReplyAsync<TMessage>(TMessage message)
    {
        var redisChannel = new RedisChannel(_replyTo, RedisChannel.PatternMode.Literal);
        await _redis.GetSubscriber().PublishAsync(redisChannel, JsonSerializer.Serialize(message));
        return true;
    }
}
