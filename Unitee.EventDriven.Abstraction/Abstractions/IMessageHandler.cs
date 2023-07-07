namespace Unitee.EventDriven.Abstraction;


///
/// <summary>
/// Used for Azure Service Bus messages only
/// </summary>
public interface IMessageHandler<T>
{
    /// <summary>
    /// Route to the right consumer.
    /// </summary>
    /// <returns>
    /// True if the message was handled, false otherwise.
    /// </returns>
    public Task<bool> HandleAsync(T originalMessage);
}
