using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Unitee.EventDriven.RedisStream.Tests;

public class Startup
{}

public class TestFixture : IDisposable
{
    private TestServer Server;

    public void Dispose()
    {
        Console.WriteLine("Stop");
        GC.SuppressFinalize(this);
    }

    protected virtual void InitializeServices(IServiceCollection services)
    {
    }

    public TestFixture()
    {
        var builder = WebApplication.CreateBuilder();
        var host = builder.Build();
    }
}