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

public class BaseTests
{
    private readonly IServiceCollection _services;

    public BaseTests()
    {
        _services = new ServiceCollection();
        var redis = ConnectionMultiplexer.Connect("localhost:6379");

        _services.AddLogging();
        _services.AddSingleton<IConnectionMultiplexer>(redis);
        _services.AddScoped<IRedisStreamPublisher, RedisStreamPublisher>();

        _services.AddScoped(provider => new RedisStreamMessagesProcessor("Test", provider,
                    provider.GetRequiredService<IConnectionMultiplexer>(),
                    provider.GetRequiredService<ILogger<RedisStreamMessagesProcessor>>()));

        _services.AddScoped<RedisStreamBackgroundReceiver>(x => new RedisStreamBackgroundReceiver(x, "Test"));
    }

    [Fact]
    public async Task PendingMessages_ShouldBeConsumedAtStart()
    {
        var consumerInstance = Mock.Of<IRedisStreamConsumer<TestEvent>>();
        _services.AddScoped<IConsumer>(x => consumerInstance);

        var provider = _services.BuildServiceProvider();
        var publisher = provider.GetRequiredService<IRedisStreamPublisher>();

        await publisher.PublishAsync(new TestEvent("World"));

        var backgroundService = provider.GetService<RedisStreamBackgroundReceiver>();
        await backgroundService.StartAsync(CancellationToken.None);
        await Task.Delay(500);
        await backgroundService.StopAsync(CancellationToken.None);

        var moc = Mock.Get(consumerInstance);
        moc.Verify(x=> x.ConsumeAsync(new TestEvent("World")), Times.Once);
    }

    [Fact]
    public async Task NewMessages_ShouldBeConsumed()
    {
        var consumerInstance = Mock.Of<IRedisStreamConsumer<TestEvent>>();
        _services.AddScoped<IConsumer>(x => consumerInstance);

        var provider = _services.BuildServiceProvider();
        var publisher = provider.GetRequiredService<IRedisStreamPublisher>();

        var backgroundService = provider.GetService<RedisStreamBackgroundReceiver>();
        await backgroundService.StartAsync(CancellationToken.None);
        await Task.Delay(100);
        await publisher.PublishAsync(new TestEvent("World"));
        await Task.Delay(400);
        await backgroundService.StopAsync(CancellationToken.None);

        var moc = Mock.Get(consumerInstance);
        moc.Verify(x=> x.ConsumeAsync(new TestEvent("World")), Times.Once);
    }

        [Fact]
    public async Task MultipleConsumers_ShoudBeCaled()
    {
        var consumerInstance1 = Mock.Of<IRedisStreamConsumer<TestEvent>>();
        var consumerInstance2 = Mock.Of<IRedisStreamConsumer<TestEvent>>();
        var consumerInstance3 = Mock.Of<IRedisStreamConsumer<TestEvent>>();

        _services.AddScoped<IConsumer>(x => consumerInstance1);
        _services.AddScoped<IConsumer>(x => consumerInstance2);
        _services.AddScoped<IConsumer>(x => consumerInstance3);

        var provider = _services.BuildServiceProvider();
        var publisher = provider.GetRequiredService<IRedisStreamPublisher>();

        var backgroundService = provider.GetService<RedisStreamBackgroundReceiver>();
        await backgroundService.StartAsync(CancellationToken.None);
        await Task.Delay(100);
        await publisher.PublishAsync(new TestEvent("World"));
        await Task.Delay(400);
        await backgroundService.StopAsync(CancellationToken.None);

        var moc1 = Mock.Get(consumerInstance1);
        var moc2 = Mock.Get(consumerInstance2);
        var moc3 = Mock.Get(consumerInstance3);

        moc1.Verify(x => x.ConsumeAsync(new TestEvent("World")), Times.Once);
        moc2.Verify(x => x.ConsumeAsync(new TestEvent("World")), Times.Once);
        moc3.Verify(x => x.ConsumeAsync(new TestEvent("World")), Times.Once);
    }


}