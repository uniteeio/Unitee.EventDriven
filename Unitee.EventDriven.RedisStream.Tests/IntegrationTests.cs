namespace Unitee.EventDriven.RedisStream.Tests;

public class UnitTest1 : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public UnitTest1(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Test1()
    {
        var client = _factory.Server.CreateClient();
        var resp = await client.SendAsync(new HttpRequestMessage(HttpMethod.Get, "/"));
        Console.WriteLine(resp.StatusCode);
    }
}