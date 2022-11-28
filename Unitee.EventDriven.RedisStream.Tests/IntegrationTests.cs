using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using StackExchange.Redis;
using Unitee.EventDriven.Abstraction;
using Unitee.EventDriven.Attributes;
using Unitee.EventDriven.Models;

namespace Unitee.EventDriven.RedisStream.Tests;


public class BaseTests : IClassFixture<RedisFixtures>
{
    private readonly IConnectionMultiplexer _redis;

    public BaseTests(RedisFixtures redis)
    {
        _redis = redis.Redis;
    }

    IServiceCollection GetServices(string name)
    {
        var _services = new ServiceCollection();

        _services.AddLogging();
        _services.AddSingleton(_redis);
        _services.AddScoped<IRedisStreamPublisher, RedisStreamPublisher>();
        _services.AddScoped<RedisStreamMessageContextFactory>();
        _services.AddScoped(provider => new RedisStreamMessagesProcessor(name, provider));
        _services.AddSingleton<RedisStreamBackgroundReceiver>(x => new RedisStreamBackgroundReceiver(x));
        return _services;
    }

    [Fact]
    public async Task PendingMessages_ShouldBeConsumedAtStart()
    {
        var db = _redis.GetDatabase();
        try
        {
            db.StreamCreateConsumerGroup("TEST_EVENT_1", "Test1", StreamPosition.NewMessages);
        }
        catch (RedisException)
        {
        }

        var services = GetServices("Test1");
        var consumerInstance = new Mock<IRedisStreamConsumer<TestEvent1>>();
        services.AddScoped<IConsumer>(x => consumerInstance.Object);

        var provider = services.BuildServiceProvider();
        var publisher = provider.GetRequiredService<IRedisStreamPublisher>();

        await publisher.PublishAsync(new TestEvent1("World"));

        var backgroundService = provider.GetService<RedisStreamBackgroundReceiver>();
        await backgroundService.StartAsync(CancellationToken.None);
        await Task.Delay(500);
        await backgroundService.StopAsync(CancellationToken.None);
        db.KeyDelete("TEST_EVENT_1");

        consumerInstance.Verify(x => x.ConsumeAsync(new TestEvent1("World")), Times.Once);
    }

    [Fact]
    public async Task NewMessages_ShouldBeConsumed()
    {
        var db = _redis.GetDatabase();
        var services = GetServices("Test1");
        var consumerInstance = new Mock<IRedisStreamConsumer<TestEvent2>>();
        services.AddScoped<IConsumer>(x => consumerInstance.Object);

        var provider = services.BuildServiceProvider();
        var publisher = provider.GetRequiredService<IRedisStreamPublisher>();

        var backgroundService = provider.GetService<RedisStreamBackgroundReceiver>();
        await backgroundService.StartAsync(CancellationToken.None);
        await Task.Delay(500);
        await publisher.PublishAsync(new TestEvent2("World"));
        await Task.Delay(500);
        await backgroundService.StopAsync(CancellationToken.None);
        db.KeyDelete("TEST_EVENT_2");

        consumerInstance.Verify(x => x.ConsumeAsync(new TestEvent2("World")), Times.Once);
    }

    [Fact]
    public async Task MultipleConsumers_ShoudBeCaled()
    {
        var db = _redis.GetDatabase();
        var services = GetServices("Test2");

        var consumerInstance1 = new Mock<IRedisStreamConsumer<TestEvent3>>();
        var consumerInstance2 = new Mock<IRedisStreamConsumer<TestEvent3>>();
        var consumerInstance3 = new Mock<IRedisStreamConsumer<TestEvent3>>();

        services.AddScoped<IConsumer>(x => consumerInstance1.Object);
        services.AddScoped<IConsumer>(x => consumerInstance2.Object);
        services.AddScoped<IConsumer>(x => consumerInstance3.Object);

        var provider = services.BuildServiceProvider();
        var publisher = provider.GetRequiredService<IRedisStreamPublisher>();

        var backgroundService = provider.GetService<RedisStreamBackgroundReceiver>();
        await backgroundService.StartAsync(CancellationToken.None);
        await Task.Delay(500);
        await publisher.PublishAsync(new TestEvent3("World"));
        await Task.Delay(500);
        await backgroundService.StopAsync(CancellationToken.None);
        db.KeyDelete("TEST_EVENT_3");

        consumerInstance1.Verify(x => x.ConsumeAsync(new TestEvent3("World")), Times.Once);
        consumerInstance2.Verify(x => x.ConsumeAsync(new TestEvent3("World")), Times.Once);
        consumerInstance3.Verify(x => x.ConsumeAsync(new TestEvent3("World")), Times.Once);
    }

    [Fact]
    public async Task ResponseRequest_ShouldReply()
    {
        var db = _redis.GetDatabase();
        var services = GetServices(Guid.NewGuid().ToString());

        services.AddScoped<IConsumer, ResponseRequestFixtureConsumer>();
        var provider = services.BuildServiceProvider();
        var publisher = provider.GetRequiredService<IRedisStreamPublisher>();

        var backgroundService = provider.GetService<RedisStreamBackgroundReceiver>();
        await backgroundService.StartAsync(CancellationToken.None);
        await Task.Delay(500);
        var resp = await publisher.RequestResponseAsync<TestEvent4, string>(new TestEvent4("World"), new()
        {
            SessionId = Guid.NewGuid().ToString()
        });
        await Task.Delay(500);
        await backgroundService.StopAsync(CancellationToken.None);
        db.KeyDelete("TEST_EVENT_4");

        Assert.Equal("Received", resp);
    }

    [Fact]
    public async Task Scheduled_ShouldConsumeAMessageSentInThePast()
    {
        var db = _redis.GetDatabase();
        var services = GetServices(Guid.NewGuid().ToString());

        var consumerInstance1 = new Mock<IRedisStreamConsumer<TestEvent5>>();
        services.AddScoped<IConsumer>(x => consumerInstance1.Object);

        var provider = services.BuildServiceProvider();
        var publisher = provider.GetRequiredService<IRedisStreamPublisher>();

        var backgroundService = provider.GetService<RedisStreamBackgroundReceiver>();
        await backgroundService.StartAsync(CancellationToken.None);
        await Task.Delay(500);
        await publisher.PublishAsync<TestEvent5>(new TestEvent5("World"), new MessageOptions()
        {
            ScheduledEnqueueTime = DateTime.UtcNow.AddSeconds(-5)
        });
        await Task.Delay(3500);
        await backgroundService.StopAsync(CancellationToken.None);
        db.KeyDelete("TEST_EVENT_5");
        consumerInstance1.Verify(x => x.ConsumeAsync(new TestEvent5("World")), Times.Once);
    }

    [Fact]
    public async Task Concurrency_ScheduledMessageShouldNotBeSentMultipleTime()
    {
        var db = _redis.GetDatabase();
        var services = GetServices(Guid.NewGuid().ToString());

        var consumerInstance1 = new Mock<IRedisStreamConsumer<TestEvent6>>();
        services.AddScoped<IConsumer>(x => consumerInstance1.Object);

        services.AddSingleton<RedisStreamBackgroundReceiver>(x => new RedisStreamBackgroundReceiver(x));

        var provider = services.BuildServiceProvider();
        var publisher = provider.GetRequiredService<IRedisStreamPublisher>();

        var backgroundService = provider.GetService<RedisStreamBackgroundReceiver>();
        await backgroundService.StartAsync(CancellationToken.None);
        await Task.Delay(500);
        await publisher.PublishAsync(new TestEvent6("World"), new MessageOptions()
        {
            ScheduledEnqueueTime = DateTime.UtcNow.AddSeconds(-5)
        });
        await Task.Delay(3500);
        await backgroundService.StopAsync(CancellationToken.None);
        db.KeyDelete("TEST_EVENT_6");
        consumerInstance1.Verify(x => x.ConsumeAsync(new TestEvent6("World")), Times.Once);
    }
}