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
        var res = await db.StreamAddAsync(subject, "Body", JsonSerializer.Serialize(message, _config.JsonSerializerOptions), maxLength: 100);
        var redisChannel = new RedisChannel(subject, RedisChannel.PatternMode.Literal);
        await db.PublishAsync(redisChannel, "");
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
            var res = await db.StreamAddAsync(subject, new NameValueEntry[] {
                new("Body", JsonSerializer.Serialize(message, _config.JsonSerializerOptions)),
                new("ReplyTo", $"{subject}_{options.SessionId}") }, maxLength: 100);

            var redisChannel = new RedisChannel(subject, RedisChannel.PatternMode.Literal);
            await db.PublishAsync(redisChannel, "1");
            return res;
        }

        if (options.ScheduledEnqueueTime is null)
        {
            return await PublishAsync(message);
        }

        var member = new RedisStreamScheduledMessageType<TMessage>(Guid.NewGuid(), message, subject);

        var json = JsonSerializer.Serialize(member, _config.JsonSerializerOptions);
        var scheduledTime = options.ScheduledEnqueueTime.Value.ToUnixTimeMilliseconds();

        await db.SortedSetAddAsync("SCHEDULED_MESSAGES", json, scheduledTime);

        return json;
    }


    public async Task<U> RequestResponseAsync<T, U>(T message, MessageOptions? options = null, ReplyOptions? replyOptions = null)
    {
        var sessionId = options?.SessionId ?? Guid.NewGuid().ToString();
        var subject = MessageHelper.GetSubject<T>();
        var uniqueName = $"{subject}_{sessionId}";
        var db = _redis.GetDatabase();

        var promise = new TaskCompletionSource<U>();

        var redisChannel = new RedisChannel(uniqueName, RedisChannel.PatternMode.Literal);

        _redis.GetSubscriber().Subscribe(redisChannel, (channel, message) =>
        {
            var response = JsonSerializer.Deserialize<U>(message!, _config.JsonSerializerOptions);
            promise.TrySetResult(response!);
        });

        await PublishAsync(message, new MessageOptions
        {
            SessionId = sessionId
        });

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

