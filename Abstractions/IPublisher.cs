using ServiceBus.Models;
namespace ServiceBus.Abstraction;

public interface IPublisher<TResult, TSequence>
{
    /// <summary>
    /// Publie un message sur le bus.
    /// </summary>
    public Task<TResult> PublishAsync<TMessage>(TMessage message);

    /// <summary>
    /// Publie un message sur le bus avec des options.
    /// </summary>
    public Task<TResult> PublishAsync<TMessage>(TMessage message, MessageOptions options);

    /// <summary>
    /// Publie un message et attend une réponse
    /// </summary>
    public Task<U> RequestResponseAsync<T, U>(T message, MessageOptions options, ReplyOptions? replyOptions = null);

    /// <summary>
    /// Annule un message
    /// </summary>
    public Task CancelAsync(TSequence sequence, string? topic = null);
}