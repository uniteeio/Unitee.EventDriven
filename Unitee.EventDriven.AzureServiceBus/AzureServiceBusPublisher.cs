using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;
using Unitee.EventDriven.Abstraction;
using Unitee.EventDriven.Exceptions;
using Unitee.EventDriven.Helpers;
using Unitee.EventDriven.Models;

namespace Unitee.EventDriven.AzureServiceBus;

public record Result(long Sequence, string MessageId);

public interface IAzureServiceBusPublisher : IPublisher<Result, long> { }


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

    private ServiceBusAdministrationClient GetManagementClient()
    {
        return new ServiceBusAdministrationClient(_connectionString);
    }

    private async Task<Result> InternalPublishAsync<T>(T message, string topic, ServiceBusMessage azMessage)
    {
        var subject = MessageHelper.GetSubject<T>();

        await using var client = GetServiceBusClient();
        var sender = client.CreateSender(topic);

        azMessage.Subject = subject;
        azMessage.Body = new(JsonSerializer.Serialize(message));
        azMessage.ContentType = "application/json";

        if (azMessage.MessageId is null)
        {
            azMessage.MessageId = Guid.NewGuid().ToString();
        }

        if (azMessage.ScheduledEnqueueTime == default)
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
        var msg = ServiceBusMessageFactory.Create(options);
        return await InternalPublishAsync(message, options.Topic ?? _defaultTopic, msg);
    }

    public async Task CancelAsync(long sequence, string? topic = null)
    {
        await using var client = GetServiceBusClient();
        var sender = client.CreateSender(topic ?? _defaultTopic);
        await sender.CancelScheduledMessageAsync(sequence);
    }

    private async Task CreateReplyQueue(string queueName)
    {
        var client = GetManagementClient();

        if (!await client.QueueExistsAsync(queueName))
        {
            await client.CreateQueueAsync(new CreateQueueOptions(queueName)
            {
                AutoDeleteOnIdle = TimeSpan.FromMinutes(10),
                DefaultMessageTimeToLive = TimeSpan.FromMinutes(5),
                RequiresSession = true,
            });
        }
    }

    public async Task<TResponse> RequestResponseAsync<TMessage, TResponse>(TMessage message, MessageOptions options, ReplyOptions? reply = null)
    {
        if (reply is null)
        {
            reply = new();
        }

        var sessionId = options.SessionId ?? Guid.NewGuid().ToString();

        if (options.SessionId is null)
        {
            options.SessionId = sessionId;
        }

        await CreateReplyQueue(reply.QueueName);

        var client = GetServiceBusClient();

        var msg = ServiceBusMessageFactory.Create(options);

        msg.ReplyToSessionId = sessionId;
        msg.ReplyTo = reply.QueueName;

        await InternalPublishAsync(message, options.Topic ?? _defaultTopic, msg);

        ServiceBusSessionReceiver receiver = await client.AcceptSessionAsync(reply.QueueName, sessionId, new ServiceBusSessionReceiverOptions()
        {
            ReceiveMode = ServiceBusReceiveMode.ReceiveAndDelete
        });

        ServiceBusReceivedMessage receivedMessage = await receiver.ReceiveMessageAsync(reply.Timeout);

        if (receivedMessage is null)
        {
            throw new TimeoutException();
        }

        var body = JsonSerializer.Deserialize<TResponse>(receivedMessage.Body);

        if (body is null)
        {
            throw new CannotParseException<ServiceBusReceivedMessage>(receivedMessage);
        }

        return body;
    }

    public Task<Result> PublishAsync<TMessage>(TMessage message, string subject)
    {
        throw new NotImplementedException();
    }
}
