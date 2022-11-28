namespace Unitee.EventDriven.RedisStream.Tests;

public class ResponseRequestFixtureConsumer : IRedisStreamConsumerWithContext<TestEvent4>
{
    public async Task ConsumeAsync(TestEvent4 message, IRedisStreamMessageContext context)
    {
        await context.ReplyAsync("Received");
    }
}
