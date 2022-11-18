using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

public partial class Program
{
}

public class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
     protected override void ConfigureWebHost(IWebHostBuilder builder)
     {
     }
}