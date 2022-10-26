using Unitee.EventDriven.Abstraction;

namespace Unitee.EventDriven.RedisStream;

public interface IRedisStreamConsumer<TMessage> : IConsumer<TMessage> { }