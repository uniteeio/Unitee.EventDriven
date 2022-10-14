using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.DependencyInjection;
using ServiceBus.Abstraction;

namespace ServiceBus.AzureServiceBus;

public class AzureServiceBusMessageHandler : IMessageHandler<ServiceBusReceivedMessage>
{
    readonly IEnumerable<IConsumer> _consumers;

    public AzureServiceBusMessageHandler(IServiceProvider provider)
    {
        _consumers = provider.GetServices<IConsumer>();
    }

    public static async Task<bool> TryInvoke<T>(IConsumer<T> consumer, ServiceBusReceivedMessage originalMessage)
    {
        var subject = AzureServiceBusPublisher.GetSubject<T>();
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

    public async Task HandleAsync(ServiceBusReceivedMessage originalMessage)
    {
        foreach (var consumer in _consumers)
        {
            // https://stackoverflow.com/questions/53508354/get-open-ended-generic-service-in-microsofts-dependency-injection)
            var result = await TryInvoke((dynamic)consumer, originalMessage);
            if (result is true)
            {
                break;
            }
        }
    }
}