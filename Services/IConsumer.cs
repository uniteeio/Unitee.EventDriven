namespace ServiceBus.Abstraction;

public interface IConsumer
{
    public void Consume<IBaseMessage>(IBaseMessage message);
    public Task ConsumeAsync<IBaseMessage>(IBaseMessage message);
}