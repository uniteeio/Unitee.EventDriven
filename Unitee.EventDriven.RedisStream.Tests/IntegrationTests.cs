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

public class UnitTest1: IClassFixture<Fixtures>
{
    private readonly IServiceCollection _services;

    public UnitTest1(Fixtures f)
    {
        _services = f.Services;
    }

    [Fact]
    public async Task Test1()
    {
        var consumerInstance = Mock.Of<IRedisStreamConsumer<TestEvent>>();
        _services.AddSingleton<IConsumer>(consumerInstance);

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
}