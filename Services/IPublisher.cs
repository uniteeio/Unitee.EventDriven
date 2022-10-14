namespace ServiceBus.Abstraction;

public interface IPublisher
{
    public Task PublishAsync<T>(T message) where T : class;
    public Task PublishAsync<T>(T message, string topic) where T : class;
}