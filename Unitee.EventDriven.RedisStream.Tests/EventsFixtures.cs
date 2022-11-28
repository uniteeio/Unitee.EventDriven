using Unitee.EventDriven.Attributes;

namespace Unitee.EventDriven.RedisStream.Tests;

[Subject("TEST_EVENT_1")]
public record TestEvent1(string ATestString);

[Subject("TEST_EVENT_2")]
public record TestEvent2(string ATestString);

[Subject("TEST_EVENT_3")]
public record TestEvent3(string ATestString);

[Subject("TEST_EVENT_4")]
public record TestEvent4(string ATestString);
