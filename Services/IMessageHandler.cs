namespace ServiceBus.Abstraction;

public interface IMessageHandler<T>
{
    public Task HandleAsync(T originalMessage);
}