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
[Subject("TEST_EVENT_5")]
public record TestEvent5(string ATestString);
[Subject("TEST_EVENT_6")]
public record TestEvent6(string ATestString);
[Subject("TEST_EVENT_7")]
public record TestEvent7(string ATestString);
[Subject("TEST_EVENT_8")]
public record TestEvent8(string ATestString);
[Subject("DEAD_LETTER")]
public record DeadLetter(string OriginalSubject, object OriginalPayload, string Reason);

