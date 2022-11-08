namespace Unitee.EventDriven.RedisStream.Models;

public record RedisStreamScheduledMessageType<T>(Guid Id, T Body, string Subject);