using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using StackExchange.Redis;
using Unitee.EventDriven.Abstraction;
using Unitee.EventDriven.Attributes;
using Unitee.RedisStream;

namespace Unitee.EventDriven.RedisStream.Tests;

[Subject("TEST_EVENT")]
public record TestEvent(string ATestString);

public class UnitTest1
{
    public UnitTest1()
    {
    }

    [Fact]
    public async Task Test1()
    {
        IServiceCollection services = new ServiceCollection();
        var redis = await ConnectionMultiplexer.ConnectAsync("localhost:6379");

        services.AddLogging();
        services.AddSingleton<IConnectionMultiplexer>(redis);
        services.AddScoped<IRedisStreamPublisher, RedisStreamPublisher>();

        services.AddScoped(provider => new RedisStreamMessagesProcessor("Test", provider,
            provider.GetRequiredService<IConnectionMultiplexer>(),
            provider.GetRequiredService<ILogger<RedisStreamMessagesProcessor>>()));

        var consumerInstance = Mock.Of<IRedisStreamConsumer<TestEvent>>();

        services.AddSingleton<RedisStreamBackgroundReceiver>(x => new RedisStreamBackgroundReceiver(x, "Test"));
        services.AddSingleton<IConsumer>(consumerInstance);

        var provider = services.BuildServiceProvider();

        var publisher = provider.GetRequiredService<IRedisStreamPublisher>();

        await publisher.PublishAsync(new TestEvent("World"));

        var backgroundService = provider.GetService<RedisStreamBackgroundReceiver>();
        await backgroundService.StartAsync(CancellationToken.None);
        await Task.Delay(1000);
        await backgroundService.StopAsync(CancellationToken.None);

        var moc = Mock.Get(consumerInstance);
        moc.Verify(x=> x.ConsumeAsync(new TestEvent("World")), Times.Once);
    }
}