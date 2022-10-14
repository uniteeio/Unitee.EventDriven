using ServiceBus.Models;

namespace ServiceBus.Abstraction;

public interface IPublisher
{
    /// <summary>
    /// Publie un message sur le bus.
    /// </summary>
    public Task PublishAsync<T>(T message);

    /// <summary>
    /// Publie un message sur le bus avec des options.
    /// </summary>
    public Task PublishAsync<T>(T message, MessageOptions options);
}