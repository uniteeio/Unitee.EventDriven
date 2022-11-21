
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using Unitee.EventDriven.RedisStream;
using Unitee.RedisStream;

public class RedisFixctures : IDisposable
{
    public IServiceCollection Services;
    public IConnectionMultiplexer Redis;

    public RedisFixctures()
    {
        Redis = ConnectionMultiplexer.Connect("localhost:6379");
    }

    public void Dispose()
    {
    }
}