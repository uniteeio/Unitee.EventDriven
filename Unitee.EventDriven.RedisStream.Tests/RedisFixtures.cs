using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace Unitee.EventDriven.RedisStream.Tests;

public class RedisFixtures : IDisposable
{
    public IServiceCollection Services;
    public IConnectionMultiplexer Redis;

    public RedisFixtures()
    {
        ThreadPool.GetMaxThreads(out var workerThreads, out var completionPortThreads);
        ThreadPool.SetMinThreads(workerThreads, completionPortThreads);
        Redis = ConnectionMultiplexer.Connect("localhost:6379");
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }
}