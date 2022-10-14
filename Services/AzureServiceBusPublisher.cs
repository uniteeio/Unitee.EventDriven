using System.Text.Json;
using Azure.Messaging.ServiceBus;
using ServiceBus.Abstraction;
using ServiceBus.Attributes;
using ServiceBus.Models;

namespace ServiceBus.AzureServiceBus;

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

    public static string GetSubject<T>()
    {
        var maybeSubjectAttribute = (SubjectAttribute?)Attribute.GetCustomAttribute(typeof(T), typeof(SubjectAttribute));

        var maybeSubject = maybeSubjectAttribute?.Subject;

        if (maybeSubject is null)
        {
            // returns the name of the class if there is no attributes defined
            return typeof(T).Name;
        }

        return maybeSubject;
    }

    private async Task InternalPublishAsync<T>(T message, string topic, ServiceBusMessage azMessage) where T : class
    {
        var subject = GetSubject<T>();

        await using var client = GetServiceBusClient();
        var sender = client.CreateSender(topic);

        azMessage.Subject = subject;
        azMessage.Body = new(JsonSerializer.Serialize(message));
        azMessage.ContentType = "application/json";

        await sender.SendMessageAsync(azMessage);
    }

    public async Task PublishAsync<T>(T message) where T : class
    {
        await InternalPublishAsync(message, _defaultTopic, new());
    }

    public async Task PublishAsync<T>(T message, MessageOptions options) where T : class
    {
        var msg = new ServiceBusMessage();

        if (options.ScheduledEnqueueTime is not null)
        {
            msg.ScheduledEnqueueTime = options.ScheduledEnqueueTime.Value;
        }

        await InternalPublishAsync(message, options.Topic ?? _defaultTopic, msg);
    }
}