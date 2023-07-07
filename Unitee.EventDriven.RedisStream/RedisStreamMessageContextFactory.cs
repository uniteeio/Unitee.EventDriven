using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Unitee.EventDriven.RedisStream;

/// Injected automatically by the DI container
public class RedisStreamMessageContextFactory
{
    private readonly IRedisStreamPublisher _publisher;
    private readonly IConnectionMultiplexer _redis;
    private readonly ILoggerFactory _loggerFactory;

    public RedisStreamMessageContextFactory(IRedisStreamPublisher publisher, IConnectionMultiplexer redis, ILoggerFactory loggerFactory)
    {
        _publisher = publisher;
        _redis = redis;
        _loggerFactory = loggerFactory;
    }

    /// TODO: Wrap reply to and locale in a record or tuple or something since it will probably grow
    public IRedisStreamMessageContext Create(Maybe<string> replyTo, Maybe<string> locale)
    {
        return new RedisStreamMessageContext(
            _loggerFactory,
            _publisher,
            _redis,
            replyTo,
            locale
       );
    }
}
