using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Unitee.EventDriven.Abstraction;
using Unitee.EventDriven.Helpers;
using StackExchange.Redis;
using Unitee.EventDriven.RedisStream.Models;
using Unitee.EventDriven.Attributes;

namespace Unitee.EventDriven.RedisStream;

public class RedisStreamBackgroundReceiver : BackgroundService
{
    private readonly IServiceProvider _services;
    public RedisStreamBackgroundReceiver(IServiceProvider services)
    {
        _services = services;
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