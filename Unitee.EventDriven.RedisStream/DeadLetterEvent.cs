namespace Unitee.EventDriven.RedisStream.Events;

public record DeadLetterEvent(string OriginalSubject, object OriginalPayload, string Reason);