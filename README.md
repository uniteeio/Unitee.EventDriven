# Unitee.EventDriven

[https://github.com/uniteeio/Unitee.EventDriven](https://github.com/uniteeio/Unitee.EventDriven)

# Summary

Unitee.EventDriven is library to deal with Event Driven Programming (EDP) in a distributed environment.

![Build](https://img.shields.io/github/actions/workflow/status/uniteeio/Unitee.EventDriven/publish.yml?style=flat-square)
![Nuget](https://img.shields.io/nuget/v/Unitee.EventDriven.RedisStream?style=flat-square)
```
dotnet add package Unitee.EventDriven.RedisStream
```

For now, we mainly focus on Redis as an event store because:
  - Easy to deploy or find free (cheap) clusters
  - Easy to visualize with a gui tool
  - A tool you may already familiar with (for caching for example)
  - Builtin system for pub/sub and storing streams
  - Good .NET integration

# Features
  - Publishing distributed messages
  - Subscribe to distributed messages
  - Request/Reply pattern
  - Scheduling messages
  - Treat pending messages at start
  - Recurring task (cron)
  - Localized messages

# How to use

1) Use the package `StackExchang.Redis` to make the `IConnectionMultiplexer` available in the DI container.

```csharp
var multiplexer = ConnectionMultiplexer.Connect(builder.Configuration["Redis:ConnectionString"]);
builder.Services.AddSingleton<IConnectionMultiplexer>(multiplexer);
```

2) Create an event as a POCO object.

```csharp
[Subject("USER_REGISTERED")]
public record UserRegistered(int UserId, string Email);
```

If the subject is ommited, the name of the object is used instead (here, `UserRegistered`)

# Guide

## Publish an event

### Setup

```csahrp
builder.Services.AddRedisStreamPublisher();
```

### Publish

Use the `IRedisStreamPublisher` to actually publish the event:

```csharp
[ApiController]
public class UserController : ControllerBase
{

    private readonly IRedisStreamPublisher _publisher;
    private readonly IUserService _userService;

    public UserController(IRedisStreamPublisher publisher, IUserService userService)
    {
        _publisher = publisher;
        _userService = userService;
    }

    public async Task<IActionResult> Register(string email)
    {
        var userId = _userService.CreateUserInBdd();

        await _publisher.PublishAsync(new UserRegistered(userId, email));

        return Ok();
    }

    // Request a reply
    public async Task<IActionResult> ForgotPassword(string email)
    {
        try
        {
            var response = await _publisher.RequestResponseAsync(new PasswordForgotten(email));
            return Ok();
        }
        catch (TimeoutException)
        {
            return NotFound();
        }
    }

    // Schedule
    public async Task<IActionResult> Register(string email)
    {
        await _publisher.PublishAsync(new UserRegistered30MinutesAgo(email), new()
        {
            ScheduledEnqueueTime = DateTime.UtcNow.AddMinutes(30);
        });

        return Ok();
    }
}

```

## Consume an event

### Setup

You need to register a `RedisStreamBackgorundReceiver`:

```csharp
services.AddRedisStreamBackgroundReceiver("ConsumerService");
```

Implementation detail: The name is used to create consumer groups. A message is delivered to all the consumer groups. (one to many communication).

### Consume

You also need to create a class that implements: `IRedisStreamConsumer<TEvent>`

```csharp
public class UserRegisteredConsumer : IRedisStreamConsumer<UserRegistered>
{
    public async Task ConsumeAsync(UserRegistered message)
    {
        await _email.Send(message.Email);
    }
}
```

Then, register your consumer:

```csharp
services.AddTransient<IConsumer, UserRegisteredConsumer>();
```

All consumers should be added using `AddTransient`. So, they all have their own scope since they are executed concurrently.

If you want to your consumer to be able to reply or access metadata of the message, then, implement `IRedisStreamConsumerWithContext<TRequest, TResponse>` instead.

```csharp
public class UserRegisteredConsumer : IRedisStreamConsumeWithContext<UserRegistered, MyResponse> // Use object or anything if you didn't plan to respond to the message.
{
    public async Task ConsumeAsync(UserRegistered message, IRedisStreamMessageContext context)
    {
       _logger.LogInformation(context.Locale);

        await _email.Send(message.Email);

        await context.ReplyAsync(new MyResponse());
    }
}
```

### Dead letter queue

If a consumer throw, then the message and the exception are published to a special queue named: dead letter queue.
The default name is `DEAD_LETTER` but you can configured it by providing a second parameter to `AddRedisStreamBackgroundReceiver`. You can easily imagine a script able to pull the messages from the dead letter queue and send them again.


### Horizontal scaling

Inside a consumer group, you can have multiple consumers. Each consumer group receives a single copy of the message.
You can name the consumer with the third parameter of `AddRedisStreamBackgroundReceiver`. You should use an unique name PER INSTANCE

### Thread safety and concurrency

When multiple consumer are subscribed to the same event, or when, there is multiple event pending, they are executed concurrently.
This mean that you should not rely of the order they are inserted.

To avoid any concurrency issues, **consumers should be registered as Transient**. So, if you use Entity Framework, register it as Transient too:

```cs
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    SqlServerDbContextOptionsExtensions.UseSqlServer(options, dbConn), ServiceLifetime.Transient);
```

### Reccuring tasks

You can add a Redis Hash in a special named key: `Cron:Schedule:{Name of your cron}`. This hash should have as a fields (in the order bellow):
  - `CronExpression` A cron expression that can be parsed with Cronos (https://github.com/HangfireIO/Cronos)
  - `EventName` The name of the event we want to trigger when the cron expression is hit

Every time the cron expression is hit, an event with the name `EventName` is published.

### Configure json serialization / deserialization

You can configure the redis stream publisher and receiver by calling:

```cs
builder.Services.AddRedisStreamOptions(options =>
{
    options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
});
```
