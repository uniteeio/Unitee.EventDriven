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
    public BaseTests()
    {

    }

    IServiceCollection GetServices()
    {
        var _services = new ServiceCollection();
        var redis = ConnectionMultiplexer.Connect("localhost:6379");

        _services.AddLogging();
        _services.AddSingleton<IConnectionMultiplexer>(redis);
        _services.AddScoped<IRedisStreamPublisher, RedisStreamPublisher>();

        _services.AddScoped(provider => new RedisStreamMessagesProcessor("Test", provider));

        _services.AddSingleton<RedisStreamBackgroundReceiver>(x => new RedisStreamBackgroundReceiver(x, "Test"));
        return _services;
    }

    public class Consumer : IRedisStreamConsumer<TestEvent>
    {
        public Task ConsumeAsync(TestEvent message)
        {
            Console.WriteLine(message.ATestString);
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task PendingMessages_ShouldBeConsumedAtStart()
    {
        var services = GetServices();
        var consumerInstance = new Mock<IRedisStreamConsumer<TestEvent>>();
        services.AddScoped<IConsumer>(x => consumerInstance.Object);

        var provider = services.BuildServiceProvider();
        var publisher = provider.GetRequiredService<IRedisStreamPublisher>();

        await publisher.PublishAsync(new TestEvent("World"));

        var backgroundService = provider.GetService<RedisStreamBackgroundReceiver>();
        await backgroundService.StartAsync(CancellationToken.None);
        await Task.Delay(500);
        await backgroundService.StopAsync(CancellationToken.None);

        consumerInstance.Verify(x=> x.ConsumeAsync(new TestEvent("World")), Times.Once);
    }

    [Fact]
    public async Task NewMessages_ShouldBeConsumed()
    {
        var services = GetServices();
        var consumerInstance = new Mock<IRedisStreamConsumer<TestEvent>>();
        services.AddScoped<IConsumer>(x => consumerInstance.Object);

        var provider = services.BuildServiceProvider();
        var publisher = provider.GetRequiredService<IRedisStreamPublisher>();

        var backgroundService = provider.GetService<RedisStreamBackgroundReceiver>();
        await backgroundService.StartAsync(CancellationToken.None);
        await Task.Delay(3000);
        await publisher.PublishAsync(new TestEvent("World"));
        await Task.Delay(500);
        await backgroundService.StopAsync(CancellationToken.None);

        consumerInstance.Verify(x=> x.ConsumeAsync(new TestEvent("World")), Times.Once);
    }

    [Fact]
    public async Task MultipleConsumers_ShoudBeCaled()
    {
        var services = GetServices();

        var consumerInstance1 = new Mock<IRedisStreamConsumer<TestEvent>>();
        var consumerInstance2 = new Mock<IRedisStreamConsumer<TestEvent>>();
        var consumerInstance3 = new Mock<IRedisStreamConsumer<TestEvent>>();

        services.AddScoped<IConsumer>(x => consumerInstance1.Object);
        services.AddScoped<IConsumer>(x => consumerInstance2.Object);
        services.AddScoped<IConsumer>(x => consumerInstance3.Object);

        var provider = services.BuildServiceProvider();
        var publisher = provider.GetRequiredService<IRedisStreamPublisher>();

        var backgroundService = provider.GetService<RedisStreamBackgroundReceiver>();
        await backgroundService.StartAsync(CancellationToken.None);
        await Task.Delay(3000);
        await publisher.PublishAsync(new TestEvent("World"));
        await Task.Delay(500);
        await backgroundService.StopAsync(CancellationToken.None);

        consumerInstance1.Verify(x => x.ConsumeAsync(new TestEvent("World")), Times.Once);
        consumerInstance2.Verify(x => x.ConsumeAsync(new TestEvent("World")), Times.Once);
        consumerInstance3.Verify(x => x.ConsumeAsync(new TestEvent("World")), Times.Once);
    }
}