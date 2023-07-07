namespace Unitee.EventDriven.Abstraction;

/// <summary>
/// Used to mark a class as a consumer.
/// Should never be implemented directly without passing through IConsummer<T> / IConsumerWithContext<T> or IConsumerWithMetadata<T, TMetadata>
/// </summary>
public interface IConsumer { }

public interface IConsumerWithContext<TMessage, TMessageContext> : IConsumer
{
    public Task ConsumeAsync(TMessage message, TMessageContext context);
}

public interface IConsumer<T> : IConsumer
{
    public Task ConsumeAsync(T message);
}

