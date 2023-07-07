using Unitee.EventDriven.DependencyInjection;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using StackExchange.Redis;
using Unitee.EventDriven.Abstraction;
using Unitee.EventDriven.Models;
using System.Text.Json;

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
        _services.AddScoped(provider => new RedisStreamMessagesProcessor(name, "Default", "DEAD_LETTER", provider));
        _services.AddSingleton(x => new RedisStreamBackgroundReceiver(x));
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
        services.AddTransient<IConsumer>(x => consumerInstance.Object);

        var provider = services.BuildServiceProvider();
        var publisher = provider.GetRequiredService<IRedisStreamPublisher>();

        await publisher.PublishAsync(new TestEvent1("World"));

        var backgroundService = provider.GetService<RedisStreamBackgroundReceiver>();
        await backgroundService.StartAsync(CancellationToken.None);
        await Task.Delay(100);
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
        services.AddTransient<IConsumer>(x => consumerInstance.Object);

        var provider = services.BuildServiceProvider();
        var publisher = provider.GetRequiredService<IRedisStreamPublisher>();

        var backgroundService = provider.GetService<RedisStreamBackgroundReceiver>();
        await backgroundService.StartAsync(CancellationToken.None);
        await Task.Delay(100);
        await publisher.PublishAsync(new TestEvent2("World"));
        await Task.Delay(100);
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

        services.AddTransient<IConsumer>(x => consumerInstance1.Object);
        services.AddTransient<IConsumer>(x => consumerInstance2.Object);
        services.AddTransient<IConsumer>(x => consumerInstance3.Object);

        var provider = services.BuildServiceProvider();
        var publisher = provider.GetRequiredService<IRedisStreamPublisher>();

        var backgroundService = provider.GetService<RedisStreamBackgroundReceiver>();
        await backgroundService.StartAsync(CancellationToken.None);
        await Task.Delay(100);
        await publisher.PublishAsync(new TestEvent3("World"));
        await Task.Delay(100);
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

        services.AddTransient<IConsumer, ResponseRequestFixtureConsumer>();
        var provider = services.BuildServiceProvider();
        var publisher = provider.GetRequiredService<IRedisStreamPublisher>();

        var backgroundService = provider.GetService<RedisStreamBackgroundReceiver>();
        await backgroundService.StartAsync(CancellationToken.None);
        await Task.Delay(100);
        var resp = await publisher.RequestResponseAsync<TestEvent4, string>(new TestEvent4("World"), new()
        {
            SessionId = Guid.NewGuid().ToString()
        });
        await Task.Delay(100);
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
        services.AddTransient<IConsumer>(x => consumerInstance1.Object);

        var provider = services.BuildServiceProvider();
        var publisher = provider.GetRequiredService<IRedisStreamPublisher>();

        var backgroundService = provider.GetService<RedisStreamBackgroundReceiver>();
        await backgroundService.StartAsync(CancellationToken.None);
        await Task.Delay(100);
        await publisher.PublishAsync(new TestEvent5("World"), new MessageOptions()
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
        services.AddTransient<IConsumer>(x => consumerInstance1.Object);

        services.AddSingleton(x => new RedisStreamBackgroundReceiver(x));

        var provider = services.BuildServiceProvider();
        var publisher = provider.GetRequiredService<IRedisStreamPublisher>();

        var backgroundService = provider.GetService<RedisStreamBackgroundReceiver>();
        await backgroundService.StartAsync(CancellationToken.None);
        await Task.Delay(100);
        await publisher.PublishAsync(new TestEvent6("World"), new MessageOptions()
        {
            ScheduledEnqueueTime = DateTime.UtcNow.AddSeconds(-5)
        });
        await Task.Delay(3500);
        await backgroundService.StopAsync(CancellationToken.None);
        db.KeyDelete("TEST_EVENT_6");
        consumerInstance1.Verify(x => x.ConsumeAsync(new TestEvent6("World")), Times.Once);
    }

    [Fact]
    public async Task FireAndForget_OldMessagesShouldNotBlockAtStart()
    {
        var db = _redis.GetDatabase();

         try
        {
            db.StreamCreateConsumerGroup("TEST_EVENT_7", "DefaultConsumer", StreamPosition.NewMessages);
        }
        catch (RedisException)
        {
        }

        var services = GetServices("DefaultConsumer");

        var slowConsumerInstance = new Mock<IRedisStreamConsumer<TestEvent7>>();

        slowConsumerInstance.Setup(x => x.ConsumeAsync(new TestEvent7("World")))
            .Callback(async () =>
            {
                await Task.Delay(1000);
            })
            .Returns(Task.CompletedTask);

        services.AddTransient<IConsumer>(x => slowConsumerInstance.Object);

        var provider = services.BuildServiceProvider();
        var publisher = provider.GetRequiredService<IRedisStreamPublisher>();
        var backgroundService = provider.GetService<RedisStreamBackgroundReceiver>();

        for (int i = 0; i < 50; i++)
        {
            await publisher.PublishAsync(new TestEvent7("World"));
        }

        await backgroundService.StartAsync(CancellationToken.None);
        await Task.Delay(3000);

        await backgroundService.StopAsync(CancellationToken.None);
        db.KeyDelete("TEST_EVENT_7");

        slowConsumerInstance.Verify(x => x.ConsumeAsync(new TestEvent7("World")), Times.Exactly(50));
    }

    [Fact]
    public async Task DeadLetter_MessageNotConsumedShoudBePushToDeadLetter()
    {
        var db = _redis.GetDatabase();

        var services = GetServices(Guid.NewGuid().ToString());

        var throwingConsumer = new Mock<IRedisStreamConsumer<TestEvent8>>();

        throwingConsumer.Setup(x => x.ConsumeAsync(new TestEvent8("World")))
            .Throws(new Exception("Hello World"));

        var deadLetterConsumer = new Mock<IRedisStreamConsumer<DeadLetter>>();

        services.AddTransient<IConsumer>(x => throwingConsumer.Object);
        services.AddTransient<IConsumer>(x => deadLetterConsumer.Object);

        var provider = services.BuildServiceProvider();
        var publisher = provider.GetRequiredService<IRedisStreamPublisher>();
        var backgroundService = provider.GetService<RedisStreamBackgroundReceiver>();
        await backgroundService.StartAsync(CancellationToken.None);

        await Task.Delay(100);
        await publisher.PublishAsync(new TestEvent8("World"));
        await Task.Delay(100);

        await backgroundService.StopAsync(CancellationToken.None);

        db.KeyDelete("TEST_EVENT_8");
        db.KeyDelete("DEAD_LETTER");

        deadLetterConsumer.Verify(x => x.ConsumeAsync(It.IsAny<DeadLetter>()), Times.Once);
    }

    [Fact]
    public async Task Concurrency_TwoConsumerShouoldBeExecutedConcurrently()
    {
        var db = _redis.GetDatabase();
        var services = GetServices(Guid.NewGuid().ToString());

        var slowConsumerInstance1 = new Mock<IRedisStreamConsumer<TestEvent9>>();
        var slowConsumerInstance2 = new Mock<IRedisStreamConsumer<TestEvent9>>();


        var consumer1 = slowConsumerInstance1.Setup(x => x.ConsumeAsync(new TestEvent9("World")))
            .Callback(async () =>
            {
                await Task.Delay(1000);
            })
            .Returns(Task.CompletedTask);

        var consumer2 = slowConsumerInstance1.Setup(x => x.ConsumeAsync(new TestEvent9("World")))
            .Callback(async () =>
            {
                await Task.Delay(1000);
            })
            .Returns(Task.CompletedTask);


        services.AddTransient<IConsumer>(x => slowConsumerInstance1.Object);
        services.AddTransient<IConsumer>(x => slowConsumerInstance2.Object);

        var provider = services.BuildServiceProvider();
        var publisher = provider.GetRequiredService<IRedisStreamPublisher>();
        var backgroundService = provider.GetService<RedisStreamBackgroundReceiver>();
        await backgroundService.StartAsync(CancellationToken.None);

        await Task.Delay(100);
        await publisher.PublishAsync(new TestEvent9("World"));
        await Task.Delay(1000 + 100);

        await backgroundService.StopAsync(CancellationToken.None);

        slowConsumerInstance1.Verify(x => x.ConsumeAsync(It.IsAny<TestEvent9>()), Times.Once);
        slowConsumerInstance2.Verify(x => x.ConsumeAsync(It.IsAny<TestEvent9>()), Times.Once);

        db.KeyDelete("TEST_EVENT_9");
    }


    [Fact]
    public async Task RequestReply_ShouldOnlySendOneEventInTheStream()
    {
        var db = _redis.GetDatabase();
        db.KeyDelete("TEST_EVENT_10");

        var services = GetServices(Guid.NewGuid().ToString());

        var provider = services.BuildServiceProvider();
        var publisher = provider.GetRequiredService<IRedisStreamPublisher>();

        try
        {
            await publisher.RequestResponseAsync<TestEvent10, bool>(new TestEvent10("World"), new());
        } catch (TimeoutException)
        {
        }

        db.StreamLength("TEST_EVENT_10").Should().Be(1);
        db.KeyDelete("TEST_EVENT_10");
    }


    [Fact]
    public async Task Json_CustomOptionsShouldByApplied()
    {
        var db = _redis.GetDatabase();
        db.KeyDelete("TEST_EVENT_11");

        var services = GetServices(Guid.NewGuid().ToString());
        services.AddRedisStreamOptions(options =>
        {
            options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        });

        var provider = services.BuildServiceProvider();
        var publisher = provider.GetRequiredService<IRedisStreamPublisher>();

        await publisher.PublishAsync(new TestEvent11("World"));

        var stream = db.StreamRead("TEST_EVENT_11", "0-0").First();
        stream.Values.First().Value.ToString().Should().Be("{\"aTestString\":\"World\"}");
    }

    [Fact]
    public async Task Json_WithCustomOptionsShouldWorkAsNormal()
    {
        var db = _redis.GetDatabase();
        db.KeyDelete("TEST_EVENT_12");

        var services = GetServices(Guid.NewGuid().ToString());

        var provider = services.BuildServiceProvider();
        var publisher = provider.GetRequiredService<IRedisStreamPublisher>();

        await publisher.PublishAsync(new TestEvent12("World"));

        var stream = db.StreamRead("TEST_EVENT_12", "0-0").First();
        stream.Values.First().Value.ToString().Should().Be("{\"ATestString\":\"World\"}");
    }

    [Fact]
    public async Task Json_ShouldBeConsumedEvenWithCustomJsonOptions()
    {
        var db = _redis.GetDatabase();
        var services = GetServices(Guid.NewGuid().ToString());
        var consumerInstance = new Mock<IRedisStreamConsumer<TestEvent13>>();
        services.AddTransient<IConsumer>(x => consumerInstance.Object);

        var provider = services.BuildServiceProvider();
        var publisher = provider.GetRequiredService<IRedisStreamPublisher>();

        var backgroundService = provider.GetService<RedisStreamBackgroundReceiver>();
        await backgroundService.StartAsync(CancellationToken.None);
        await Task.Delay(100);
        await publisher.PublishAsync(new TestEvent13("World"));
        await Task.Delay(100);
        await backgroundService.StopAsync(CancellationToken.None);
        db.KeyDelete("TEST_EVENT_13");

        consumerInstance.Verify(x => x.ConsumeAsync(new TestEvent13("World")), Times.Once);
    }


    [Fact]
    public async Task TTL_MessageWithExpiredAtShouldBeProcessed()
    {
        var db = _redis.GetDatabase();

         try
        {
            db.StreamCreateConsumerGroup("TEST_EVENT_14", "DefaultConsumer", StreamPosition.NewMessages);
        }
        catch (RedisException)
        {
        }

        var services = GetServices("DefaultConsumer");
        var consumerInstance = new Mock<IRedisStreamConsumer<TestEvent14>>();
        services.AddTransient<IConsumer>(x => consumerInstance.Object);

        var provider = services.BuildServiceProvider();
        var publisher = provider.GetRequiredService<IRedisStreamPublisher>();
        var backgroundService = provider.GetService<RedisStreamBackgroundReceiver>();

        await publisher.PublishAsync(new TestEvent14("World"), new MessageOptions() { ExpireAt = DateTimeOffset.UtcNow.AddSeconds(5) });
        await backgroundService.StartAsync(CancellationToken.None);
        await Task.Delay(1000);
        await backgroundService.StopAsync(CancellationToken.None);
        db.KeyDelete("TEST_EVENT_14");

        consumerInstance.Verify(x => x.ConsumeAsync(new TestEvent14("World")), Times.Once);
    }

    [Fact]
    public async Task TTL_MessageWithAExpiredAtExpiredShouldNotBeProcessed()
    {
        var db = _redis.GetDatabase();

         try
        {
            db.StreamCreateConsumerGroup("TEST_EVENT_15", "DefaultConsumer", StreamPosition.NewMessages);
        }
        catch (RedisException)
        {
        }
        var services = GetServices("DefaultConsumer");
        var consumerInstance = new Mock<IRedisStreamConsumer<TestEvent15>>();
        services.AddTransient<IConsumer>(x => consumerInstance.Object);

        var provider = services.BuildServiceProvider();
        var publisher = provider.GetRequiredService<IRedisStreamPublisher>();
        var backgroundService = provider.GetService<RedisStreamBackgroundReceiver>();

        await publisher.PublishAsync(new TestEvent15("World"), new MessageOptions() { ExpireAt = DateTimeOffset.UtcNow.AddSeconds(1) });
        await Task.Delay(2000);
        await backgroundService.StartAsync(CancellationToken.None);
        await Task.Delay(1000);
        await backgroundService.StopAsync(CancellationToken.None);
        db.KeyDelete("TEST_EVENT_15");

        consumerInstance.Verify(x => x.ConsumeAsync(new TestEvent15("World")), Times.Never());
    }

    [Fact]
    public async Task Local_IfALocaleHasBeenSpecifiedItShouldBeAvailable()
    {
        var db = _redis.GetDatabase();

        var services = GetServices(Guid.NewGuid().ToString());
        var consumerInstance = new Mock<IRedisStreamConsumerWithContext<TestEvent16>>();
        services.AddTransient<IConsumer>(x => consumerInstance.Object);

        var provider = services.BuildServiceProvider();
        var publisher = provider.GetRequiredService<IRedisStreamPublisher>();
        var backgroundService = provider.GetService<RedisStreamBackgroundReceiver>();

        await backgroundService.StartAsync(CancellationToken.None);
        await Task.Delay(100);
        await publisher.PublishAsync(new TestEvent16("World"), new MessageOptions() { Locale = "en-US" });
        await Task.Delay(500);
        await backgroundService.StopAsync(CancellationToken.None);

        db.KeyDelete("TEST_EVENT_16");

        consumerInstance.Verify(x => x.ConsumeAsync(new TestEvent16("World"), It.Is<RedisStreamMessageContext>(x => x.Locale == "en-US")), Times.Once());
    }

    [Fact]
    public async Task Local_AMessageWithoutReplyCantBeReplyed()
    {
        var db = _redis.GetDatabase();

        var services = GetServices(Guid.NewGuid().ToString());
        var consumerInstance = new Mock<IRedisStreamConsumerWithContext<TestEvent17>>();
        services.AddTransient<IConsumer>(x => consumerInstance.Object);

        var provider = services.BuildServiceProvider();
        var publisher = provider.GetRequiredService<IRedisStreamPublisher>();
        var backgroundService = provider.GetService<RedisStreamBackgroundReceiver>();

        await backgroundService.StartAsync(CancellationToken.None);
        await Task.Delay(100);
        await publisher.PublishAsync(new TestEvent17("World"), new MessageOptions() { Locale = "en-US" });
        await Task.Delay(500);
        await backgroundService.StopAsync(CancellationToken.None);

        db.KeyDelete("TEST_EVENT_17");

        consumerInstance.Verify(x => x.ConsumeAsync(new TestEvent17("World"), It.Is<RedisStreamMessageContext>(x => x.Locale == "en-US")), Times.Once());
    }




}
