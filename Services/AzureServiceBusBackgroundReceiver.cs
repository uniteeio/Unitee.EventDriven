using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ServiceBus.AzureServiceBus;

public class AzureServiceBusBackgroundReceiver : BackgroundService
{
    public IServiceProvider Services { get; }
    private readonly string _connectionString;
    private readonly string _queueOrTopic;
    private readonly string? _maybeSubscription;

    public AzureServiceBusBackgroundReceiver(IServiceProvider services, string connectionString, string queueOrTopic, string? maybeSubscription = null)
    {
        Services = services;
        _connectionString = connectionString;
        _maybeSubscription = maybeSubscription;
        _queueOrTopic = queueOrTopic;
    }

    protected async override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await using var client = new ServiceBusClient(_connectionString);
        ServiceBusReceiver receiver = _maybeSubscription is null
            ? client.CreateReceiver(_queueOrTopic)
            : client.CreateReceiver(_queueOrTopic, _maybeSubscription);

        while (!stoppingToken.IsCancellationRequested)
        {
            using var scope = Services.CreateScope();
            var handler = scope.ServiceProvider.GetRequiredService<IAzureServiceBusMessageHandler>();
            var receivedMessage = await receiver.ReceiveMessageAsync(null, stoppingToken);
            if (receivedMessage is null)
                continue;

            try
            {
                await handler.HandleAsync(receivedMessage);
            }
            catch (Exception e)
            {
                // we don't want to stop a background service when there is an exception in a consumer.
                var logger = scope.ServiceProvider.GetRequiredService<ILogger<AzureServiceBusBackgroundReceiver>>();
                logger.LogError(e, "An exception occured in the consumer");
            }
        }
    }
}