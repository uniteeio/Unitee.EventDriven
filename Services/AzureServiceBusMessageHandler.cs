using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.DependencyInjection;
using ServiceBus.Abstraction;
using ServiceBus.Helpers;

namespace ServiceBus.AzureServiceBus;

public interface IAzureServiceBusMessageHandler : IMessageHandler<ServiceBusReceivedMessage> { }

/// <summary>
/// Handler par défault pour les messages de Azure Service Bus.
/// La classe se charge de lire le message puis, d'appeler le bon consumer.
/// </summary>
/// <param name="ServiceBusReceivedMessage">Le message original (celui lu depuis la Azure Fonction).</param>
public class AzureServiceBusMessageHandler : IAzureServiceBusMessageHandler
{
    readonly IEnumerable<IConsumer> _consumers;
    private readonly IAzureServiceBusPublisher _publisher;

    public AzureServiceBusMessageHandler(IServiceProvider provider, IAzureServiceBusPublisher publisher)
    {
        _consumers = provider.GetServices<IConsumer>();
        _publisher = publisher;
    }

    public async Task<bool> TryInvoke<TMessage>(IAzureServiceBusConsumerWithContext<TMessage> consumer, ServiceBusReceivedMessage originalMessage)
    {
        var subject = MessageHelper.GetSubject<TMessage>();
        if (subject == originalMessage.Subject)
        {
            var message = JsonSerializer.Deserialize<TMessage>(originalMessage.Body);
            if (message is not null)
            {
                var messageCtx = new AzureServiceBusMessageContext(_publisher, originalMessage);
                await consumer.ConsumeAsync(message, messageCtx);
                return true;
            }
        }

        return false;
    }


    public async Task<bool> TryInvoke<TMessage>(IAzureServiceBusConsumer<TMessage> consumer, ServiceBusReceivedMessage originalMessage)
    {
        var subject = MessageHelper.GetSubject<TMessage>();
        if (subject == originalMessage.Subject)
        {
            var message = JsonSerializer.Deserialize<TMessage>(originalMessage.Body);
            if (message is not null)
            {
                await consumer.ConsumeAsync(message);
                return true;
            }
        }

        return false;
    }


    public async Task<bool> HandleAsync(ServiceBusReceivedMessage originalMessage)
    {
        foreach (var consumer in _consumers)
        {
            // https://stackoverflow.com/questions/53508354/get-open-ended-generic-service-in-microsofts-dependency-injection)
            var result = await TryInvoke((dynamic)consumer, originalMessage);
            if (result is true)
            {
                return true;
            }
        }

        return false;
    }
}