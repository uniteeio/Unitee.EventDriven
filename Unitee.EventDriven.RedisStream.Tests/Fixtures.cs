
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using Unitee.EventDriven.RedisStream;
using Unitee.RedisStream;

public class Fixtures: IDisposable
{
    public IServiceCollection Services;

    public Fixtures()
    {
        Services = new ServiceCollection();
        var redis = ConnectionMultiplexer.Connect("localhost:6379");

        Services.AddLogging();
        Services.AddSingleton<IConnectionMultiplexer>(redis);
        Services.AddScoped<IRedisStreamPublisher, RedisStreamPublisher>();

        Services.AddScoped(provider => new RedisStreamMessagesProcessor("Test", provider));

        Services.AddSingleton<RedisStreamBackgroundReceiver>(x => new RedisStreamBackgroundReceiver(x, "Test"));
    }

    public void Dispose()
    {
    }
}