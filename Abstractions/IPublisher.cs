using ServiceBus.Models;
namespace ServiceBus.Abstraction;

public interface IPublisher<SequenceType, IdType>
{
    /// <summary>
    /// Publie un message sur le bus.
    /// </summary>
    public Task<(SequenceType, IdType)> PublishAsync<T>(T message);

    /// <summary>
    /// Publie un message sur le bus avec des options.
    /// </summary>
    public Task<(SequenceType, IdType)> PublishAsync<T>(T message, MessageOptions options);

    public Task CancelAsync(SequenceType sequence, string? topic = null);
}