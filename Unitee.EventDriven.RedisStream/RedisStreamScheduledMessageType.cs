namespace Unitee.EventDriven.RedisStream.Models;

public record RedisStreamScheduledMessageType<T>(Guid Id, string Body, string Subject);
