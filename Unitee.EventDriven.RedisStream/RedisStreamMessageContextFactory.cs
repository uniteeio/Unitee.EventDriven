using StackExchange.Redis;

namespace Unitee.EventDriven.RedisStream;

public class RedisStreamMessageContextFactory
{
    private readonly IRedisStreamPublisher _publisher;
    private readonly IConnectionMultiplexer _redis;

    public RedisStreamMessageContextFactory(IRedisStreamPublisher publisher, IConnectionMultiplexer redis)
    {
        _publisher = publisher;
        _redis = redis;
    }

    public IRedisStreamMessageContext Create(string replyTo)
    {
        return new RedisStreamMessageContext(_publisher, _redis, replyTo);
    }
}