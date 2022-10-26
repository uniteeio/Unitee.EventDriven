using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Unitee.EventDriven.Abstraction;
using Unitee.EventDriven.Helpers;
using FreeRedis;

namespace Unitee.EventDriven.DependencyInjection;

public class RedisStreamBackgroundReceiver<TConsumer> : BackgroundService where TConsumer : class, IConsumer
{
    private readonly RedisClient _freeRedisClient;
    private readonly ILogger<RedisStreamBackgroundReceiver<TConsumer>> _logger;
    private readonly string _serviceName;
    private readonly IServiceProvider _services;

    public RedisStreamBackgroundReceiver(IServiceProvider services, string serviceName)
    {
        _services = services;
        _freeRedisClient = services.GetRequiredService<RedisClient>();
        _logger = services.GetRequiredService<ILogger<RedisStreamBackgroundReceiver<TConsumer>>>();
        _serviceName = serviceName;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _ = Task.Run(async () =>
        {
            var TMessage = typeof(TConsumer).GetInterface("IConsumer`1")?.GenericTypeArguments[0];

            ArgumentNullException.ThrowIfNull(TMessage);

            var subject = MessageHelper.GetSubject<TConsumer>();

            try
            {
                await _freeRedisClient.XGroupCreateAsync(subject, _serviceName, MkStream: true);
            }
            catch (RedisServerException)
            {
            }

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var result = await _freeRedisClient.XReadGroupAsync(_serviceName, "Default", 1, 3000, false, subject, ">").ConfigureAwait(false);


                    if (result.Length is 0)
                    {
                        continue;
                    }

                    var message = result.First();
                    var body = (string?)message.entries.Where(x => x.fieldValues[0] is "Body").Select(x => x.fieldValues[1]).FirstOrDefault();

                    using var scope = _services.CreateScope();
                    var consumer = scope.ServiceProvider.GetRequiredService<TConsumer>() as IConsumer;

                    if (body is not null)
                    {
                        var deserialized = JsonSerializer.Deserialize(body, TMessage);

                        try
                        {
                            Task? t = consumer.GetType().GetMethod("ConsumeAsync")?.Invoke(consumer, new[] { deserialized }) as Task;

                            if (t is not null)
                            {
                                await t;
                                _freeRedisClient.XAck(subject, "Default", message.entries[0].id);
                            }
                        }
                        catch (Exception e)
                        {
                            _logger.LogError(e, "An exception occured in the consumer {}", consumer.GetType().Name);
                        }
                    }
                }
                catch (RedisServerException)
                {
                    await _freeRedisClient.XGroupCreateAsync(subject, _serviceName, MkStream: true);
                }
            }
        }, stoppingToken);
    }
}