using System.Text.Json;

namespace Unitee.EventDriven.ReadisStream.Configuration;

public class RedisStreamConfiguration
{
    public JsonSerializerOptions JsonSerializerOptions { get; set; } = new(JsonSerializerDefaults.General);
}
