using System.Text.Json;
using Azure.Messaging.ServiceBus;
using ServiceBus.Abstraction;
using ServiceBus.Attributes;
using ServiceBus.Exceptions;

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

    public async Task PublishAsync<T>(T message) where T : class
    {
        var maybeSubjectAttribute = (SubjectAttribute?)Attribute.GetCustomAttribute(typeof(T), typeof(SubjectAttribute));
        var maybeSubject = maybeSubjectAttribute?.Subject;

        if (maybeSubject is null)
        {
            throw new SubjectMissingException();
        }

        await using var client = GetServiceBusClient();
        var sender = client.CreateSender(_defaultTopic);

        var serviceBusMessage = new ServiceBusMessage
        {
            Body = new(JsonSerializer.Serialize(message)),
            Subject = maybeSubject,
        };

        await sender.SendMessageAsync(serviceBusMessage);
    }

    public Task PublishAsync<T>(T message, string topic) where T: class
    {
        throw new NotImplementedException();
    }
}