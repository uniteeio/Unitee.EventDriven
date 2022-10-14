using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.DependencyInjection;
using ServiceBus.Abstraction;
using ServiceBus.Internal;

namespace ServiceBus.AzureServiceBus;

/// <summary>
/// Handler par défault pour les messages de Azure Service Bus.
/// La classe se charge de lire le message puis, d'appeler le bon consumer.
/// </summary>
/// <param name="ServiceBusReceivedMessage">Le message original (celui lu depuis la Azure Fonction).</param>
public class AzureServiceBusMessageHandler : IMessageHandler<ServiceBusReceivedMessage>
{
    readonly IEnumerable<IConsumer> _consumers;

    public AzureServiceBusMessageHandler(IServiceProvider provider)
    {
        _consumers = provider.GetServices<IConsumer>();
    }

    public static async Task<bool> TryInvoke<T>(IConsumer<T> consumer, ServiceBusReceivedMessage originalMessage)
    {
        var subject = ClassHelper.GetSubject<T>();
        if (subject == originalMessage.Subject)
        {
            var message = JsonSerializer.Deserialize<T>(originalMessage.Body);
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