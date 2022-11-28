
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using Unitee.EventDriven.RedisStream;

public class RedisFixtures : IDisposable
{
    public IServiceCollection Services;
    public IConnectionMultiplexer Redis;

    public RedisFixtures()
    {
        Redis = ConnectionMultiplexer.Connect("localhost:6379");
    }

    public void Dispose()
    {
    }
}