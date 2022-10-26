using ServiceBus.Abstraction;

namespace ServiceBus.RedisStream;

public interface IRedisStreamConsumer<TMessage> : IConsumer<TMessage> { }