using Cronos;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using StackExchange.Redis;

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
        processor.RegisterKeySpaceEventHandler();

        var errorCount = 0;

        // Scheduled messages
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await processor.ExecuteCrons();
                await processor.ReadAndPublishScheduledMessagesAsync();
                errorCount = 0;
            } catch (Exception e) when (e is RedisException || e is RedisTimeoutException)
            {
                errorCount++;
                if (errorCount > 10)
                {
                    throw;
                }
            }
            catch (CronFormatException)
            {
            }

            await Task.Delay(3000, stoppingToken);
        }
    }
}
