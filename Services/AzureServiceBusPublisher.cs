using System.Text.Json;
using Azure.Messaging.ServiceBus;
using ServiceBus.Abstraction;
using ServiceBus.Attributes;
using ServiceBus.Internal;
using ServiceBus.Models;

namespace ServiceBus.AzureServiceBus;

public record Result(long Sequence, string MessageId);

public interface IAzureServiceBusPublisher : IPublisher<Result, long> { }


/// <summary>
/// Implémentation de <see cref="IPublisher"/> pour Azure Service Bus.
/// </summary>
public class AzureServiceBusPublisher : IAzureServiceBusPublisher
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

    private async Task<Result> InternalPublishAsync<T>(T message, string topic, ServiceBusMessage azMessage)
    {
        var subject = ClassHelper.GetSubject<T>();

        await using var client = GetServiceBusClient();
        var sender = client.CreateSender(topic);

        azMessage.Subject = subject;
        azMessage.Body = new(JsonSerializer.Serialize(message));
        azMessage.ContentType = "application/json";

        if (azMessage.MessageId is null)
        {
            azMessage.MessageId = Guid.NewGuid().ToString();
        }

        if (azMessage.ScheduledEnqueueTime == null)
        {
            await sender.SendMessageAsync(azMessage);
            return new Result(0, azMessage.MessageId);
        }
        else
        {
            return new Result(
                await sender.ScheduleMessageAsync(azMessage, azMessage.ScheduledEnqueueTime), azMessage.MessageId);
        }
    }

    public async Task<Result> PublishAsync<T>(T message)
    {
        return await InternalPublishAsync(message, _defaultTopic, new());
    }

    public async Task<Result> PublishAsync<T>(T message, MessageOptions options)
    {
        var msg = new ServiceBusMessage();

        if (options.ScheduledEnqueueTime is not null)
        {
            msg.ScheduledEnqueueTime = options.ScheduledEnqueueTime.Value;
        }

        if (options.MessageId is not null)
        {
            msg.MessageId = options.MessageId;
        }

        return await InternalPublishAsync(message, options.Topic ?? _defaultTopic, msg);
    }

    public async Task CancelAsync(long sequence, string? topic = null)
    {
        await using var client = GetServiceBusClient();
        var sender = client.CreateSender(topic ?? _defaultTopic);
        await sender.CancelScheduledMessageAsync(sequence);
    }
}