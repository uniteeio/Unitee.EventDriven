using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Unitee.EventDriven.Abstraction;
using Unitee.EventDriven.Helpers;
using StackExchange.Redis;
using Unitee.EventDriven.RedisStream;
using Unitee.EventDriven.RedisStream.Models;
using Unitee.EventDriven.Attributes;
using Unitee.RedisStream;

namespace Unitee.EventDriven.RedisStream;

public class RedisStreamBackgroundReceiver : BackgroundService
{
    private readonly ILogger<RedisStreamBackgroundReceiver> _logger;
    private readonly string _serviceName;
    private readonly IConnectionMultiplexer _redis;
    private readonly IServiceProvider _services;

    public RedisStreamBackgroundReceiver(IServiceProvider services, string serviceName)
    {
        _services = services;
        _logger = services.GetRequiredService<ILogger<RedisStreamBackgroundReceiver>>();
        _serviceName = serviceName;
        _redis = services.GetRequiredService<IConnectionMultiplexer>();
    }


    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();

        using var scope = _services.CreateScope();

        var processor = scope.ServiceProvider.GetRequiredService<RedisStreamMessagesProcessor>();
        processor.RegisterConsumers();

        // Scheduled messages
        while (!stoppingToken.IsCancellationRequested)
        {
            await processor.ReadAndPublishScheduledMessagesAsync();
            await Task.Delay(3000, stoppingToken);
        }
    }
}