using System.Text.Json;
using Azure.Messaging.ServiceBus;
using ServiceBus.Abstraction;
using ServiceBus.Attributes;
using ServiceBus.Internal;
using ServiceBus.Models;

namespace ServiceBus.AzureServiceBus;

/// <summary>
/// Implémentation de <see cref="IPublisher"/> pour Azure Service Bus.
/// </summary>
public class AzureServiceBusPublisher : IPublisher
{
    private readonly string _connectionString;
    private readonly string _defaultTopic;

    public AzureServiceBusPublisher(string connectionString, string defaultTopic)
    {
        _connectionString = connectionString;
        _defaultTopic = defaultTopic;
    }

    private ServiceBusClient GetServiceBusClient()
    {
        return new ServiceBusClient(_connectionString);
    }

    private async Task InternalPublishAsync<T>(T message, string topic, ServiceBusMessage azMessage)
    {
        var subject = ClassHelper.GetSubject<T>();

        await using var client = GetServiceBusClient();
        var sender = client.CreateSender(topic);

        azMessage.Subject = subject;
        azMessage.Body = new(JsonSerializer.Serialize(message));
        azMessage.ContentType = "application/json";

        await sender.SendMessageAsync(azMessage);
    }

    public async Task PublishAsync<T>(T message)
    {
        await InternalPublishAsync(message, _defaultTopic, new());
    }

    public async Task PublishAsync<T>(T message, MessageOptions options)
    {
        var msg = new ServiceBusMessage();

        if (options.ScheduledEnqueueTime is not null)
        {
            msg.ScheduledEnqueueTime = options.ScheduledEnqueueTime.Value;
        }

        await InternalPublishAsync(message, options.Topic ?? _defaultTopic, msg);
    }
}