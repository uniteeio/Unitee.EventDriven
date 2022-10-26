namespace Unitee.EventDriven.Models;

public record ReplyOptions
{
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(30);
    public string QueueName { get; init; } = "reply";
}