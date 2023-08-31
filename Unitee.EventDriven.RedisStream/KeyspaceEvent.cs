using Unitee.EventDriven.Attributes;

namespace Unitee.EventDriven.RedisStream.Events;

[Subject("KEYSPACE_EVENTS")]
public record KeySpaceEvent(string Command, string Key);

