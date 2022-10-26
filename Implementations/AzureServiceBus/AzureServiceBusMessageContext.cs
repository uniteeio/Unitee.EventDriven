using Azure.Messaging.ServiceBus;
using ServiceBus.Abstraction;

namespace ServiceBus.AzureServiceBus;

public interface IAzureServiceBusMessageContext : IMessageContext<Result> { }

public class AzureServiceBusMessageContext : IAzureServiceBusMessageContext
{
    private readonly IAzureServiceBusPublisher _publisher;
    private readonly ServiceBusReceivedMessage _message;

    public AzureServiceBusMessageContext(IAzureServiceBusPublisher publisher, ServiceBusReceivedMessage originalMessage)
    {
        _publisher = publisher;
        _message = originalMessage;
    }

    public async Task<Result> AnswerAsync<TMessage>(TMessage message)
    {
        return await _publisher.PublishAsync(message, new Models.MessageOptions
        {
            Topic = _message.ReplyTo,
            SessionId = _message.SessionId,
        });
    }
}