namespace Unitee.EventDriven.Models;

public record MessageOptions()
{
    public string? Topic { get; init; }
    public DateTimeOffset? ScheduledEnqueueTime { get; init; }
    public string? MessageId { get; init; }
    public string? SessionId { get; set; }
}