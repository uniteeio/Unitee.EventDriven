using System.Text.Json;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using Unitee.EventDriven.Abstraction;

namespace Unitee.EventDriven.RedisStream;

/// Precised interfaces for RedisStream
public interface IRedisStreamConsumer<TMessage> : IConsumer<TMessage> { }
public interface IRedisStreamMessageContext : IMessageContext<bool> { };
public interface IRedisStreamConsumerWithContext<TMessage> : IConsumerWithContext<TMessage, IRedisStreamMessageContext> { }


///
/// This is created by the context factory when calling a IRedisStreamConsumerWithContext
///
public class RedisStreamMessageContext : IRedisStreamMessageContext
{
    private readonly Maybe<string> _replyTo;
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<IRedisStreamMessageContext> _logger;

    public Maybe<string> Locale { get; }

    public RedisStreamMessageContext(ILoggerFactory loggerFactory, IRedisStreamPublisher _, IConnectionMultiplexer redis, Maybe<string> replyTo, Maybe<string> locale)
    {
        _replyTo = replyTo;
        _redis = redis;
        _logger = _logger = loggerFactory.CreateLogger<IRedisStreamMessageContext>();
        Locale = locale;
    }

    public async Task<bool> ReplyAsync<TMessage>(TMessage message)
    {
        if (_replyTo.HasNoValue)
        {
            _logger.LogError("You cannot because reply to this message because the value: replyTo is not defined. Please be sure to publish the message with RequestReply");
            return false;
        }

        var redisChannel = new RedisChannel(_replyTo.GetValueOrThrow(), RedisChannel.PatternMode.Literal);

        // FIXME: Json parse option here
        await _redis.GetSubscriber().PublishAsync(redisChannel, JsonSerializer.Serialize(message));
        return true;
    }
}
