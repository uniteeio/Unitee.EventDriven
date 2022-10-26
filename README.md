# Azure Service Bus

[https://github.com/uniteeio/Unitee.EventDriven](https://github.com/uniteeio/Unitee.EventDriven)

![logo](./Logo/Logo.png)

## Concepts

---

### Les messages

Sont des classes POCO, échangées depuis un service bus. Les messages ont un “sujet” qui sert de discriminant lors la consomation.

### Le publisher

Le publisher est une classe qui permet de publier un message à travers un bus d’événement.

### Les consumers

Les consumers sont des classes capables de consumer un type de message en particulier.

### Le handler

Le handler est la classe capable de rediriger un message vers le handler correct en fonction du sujet.

## Exemples et documentation

---

### Créer un type de message

Un message est une classe, ou un type enregistrement (conseillé). 

```csharp
// Préféré
public record OrderSubmitted(int Id);

// Ok
public class OrderSubmitted
{
    public int Id { get; set; }
}
```

Le sujet est dans ce cas déterminé par le nom de la classe (ici: OrderSubmitted).

Il est également possible définir notre propre sujet à l’aide de l’attribut “Subject”:

```csharp
[Subject("ORDER_SUBMITTED")]
public record OrderSubmitted(int Id);
```

### Publier un message

Pour publier un message, on se sert d’un “publisher”. Une implementation d’un publisher pour Azure Service Bus est définit dans le package. 

On l’ajoute à notre projet .NET Core:

```csharp
builder.services.addAzureServiceBus(
    "{connectionString}", // chaîne de connexion vers le namespace
    "{defaultTopicName}" // topic par défaut si aucun spécifié
);
```

On utilise ensuite l’injection de dépendance pour récupérer notre instance de publisher dans le controller:

```csharp
public class MonController: Controller
{
    private readonly IAzureServiceBusPublisher _publisher;

    MonController(IAzureServiceBusPublisher publisher)
    {
        _publisher = publisher;
    }

    [Subject("USER_CREATED")]
    public record UtilisateurCreeEvent(int Id, string FirstName, string LastName);

    public async Task<IActionResult> MonAction()
    {
        // envoie dans le topic par défaut
        await _publisher.PublishAsync(new UtilisateurCreeEvent(3, "John", "Doe"));

        // envoie dans un topic particulier
        var result = await _publisher.PublishAsync(new UtilisateurCreeEvent(3, new MessageOption()
        {
            Topic = "mon-topic",
            ScheduledEnqueueTime = DateTime.Now.AddMinutes(10)

            // d'autres options disponibles
        }));

        // Annule un message
        await _publisher.CancelAsync(result.Sequence);

        return Ok();
    }
}
```

### Consumer un message dans une Azure Function

Prérequis:

- Activer l’injection de dépendance pour la Azure Fonction ([https://learn.microsoft.com/fr-fr/azure/azure-functions/functions-dotnet-dependency-injection](https://learn.microsoft.com/fr-fr/azure/azure-functions/functions-dotnet-dependency-injection))
- Avoir installé le package NuGet Microsoft.Azure.WebJobs.Extensions.ServiceBus: ([https://www.nuget.org/packages/Microsoft.Azure.WebJobs.Extensions.ServiceBus](https://www.nuget.org/packages/Microsoft.Azure.WebJobs.Extensions.ServiceBus))

1. Ajouter un consumer pour un type de message

```csharp
public record OrderSubmitted(int Id); 

// ..

public class MonConsumer: IAzureServiceBusConsumer<OrderSubmitted>
{
    private readonly ILogger<MonConsumer> log;

    // Injection de dépendance disponible
    MonConsumer(ILogger<MonConsumer> log)
    {
        _log = log;
    }

    public async Task ConsumeAsync(OrderSubmitted message)
    {
        _log.LogInfo(message.Id);
    }
}
```

1. Enregistrer son consumer pour qu’il soit disponible pour l’injection de dépendance

```csharp
public class Startup : FunctionsStartup
{
    public override void Configure(IFunctionsHostBuilder builder)
    {
        builder.Services.AddScoped<IConsumer, MonConsumer>();  
    } 
}
```

1. Ajouter un handler, j’utilise celui fourni avec l’extension pour Azure Service Bus:

```csharp
builder.Services.AddScoped<
    IAzureServiceBusMessageHandler, 
    AzureServiceBusMessageHandler>();
```

1. Combiner le tout ensemble:

```csharp
// Mon Azure Fonction
public class ServiceBusTopicTrigger1
{
    private readonly ILogger<ServiceBusTopicTrigger1> _logger;
    private readonly IAzureServiceBusMessageHandler _handler;

    public ServiceBusTopicTrigger1(ILogger<ServiceBusTopicTrigger1> log, IAzureServiceBusMessageHandler handler)
    {
         _logger = log;
         _handler = handler;
    }

    [FunctionName("ServiceBusTopicTrigger1")]
    public async Task Run([ServiceBusTrigger("topic", "subscription", Connection = "SERVICEBUS")]ServiceBusReceivedMessage message)
    {
        var result = await _handler.HandleAsync(message);

        if (result is true)
        {
           _logger.LogInformation("Un handler à été appelé");
        }
        else
        {
            _logger.LogInformation("Aucun handler trouvé pour ce type de message");
        }
    }
}
```

### Requêtes et réponses

Il est possible d’envoyer un message en attendant une réponse, pour ça on utilise la fonction:

```csharp
public record MonTypeRequete(int Guid);
public record MonTypeReponse(string Status);

// ...

await _publisher.RequestResponseAsync<MonTypeRequete, MonTypeReponse>(
    new MonTypeRequete(Guid.NewGuid()),
    new MessageOptions(),
    new ReplyOptions()
    {
        Timeout = TimeSpan.FromSecond(10);
        QueueName = "reply"
    });
```

Le service utilise le pattern: Reply / Response en créant une queue de réponse temporaire et en partionant les messages à l’aide du champs: SessionId.

### Répondre à un message

Pour répondre depuis un consumer on peut implémenter l’interface:

```csharp
public class MonConsumer: IAzureServiceBusConsumerWithContext<OrderSubmitted>
{
    public async Task ConsumeAsync(OrderSubmitted message, IAzureServiceBusMessageContext ctx)
    {
        await ctx.AnswerAsync(new MonTypeReponse("Ok"));
    }
}
```

### Consumer un message depuis une Api ou un MVC

On utilise un IHostedService (service qui tourne en tâche de fond) pour récupérer les messages.
On utilise les mêmes conceptes que pour les Functions, c'est à dire, les handlers et les consumers.

Program.cs
```cs
// Ajout des consumers
builder.Services.AddScoped<IConsumer, ConsumerImpl1>();
builder.Services.AddScoped<IConsumer, ConsumerImpl2>();
builder.Services.AddScoped<IConsumer, ConsumerImpl3>();

// Ajout du handler
builder.Services.AddScoped<IAzureServiceBusMessageHandler, AzureServiceBusMessageHandler>();

// Ajout du BackgroundService

// Pour les queues
builder.Services.AddBackgroundReceiver("{connectionString}", "{queueName}");

// Pour les topics et subscriptions
builder.Services.AddBackgroundReceiver("{connectionString}", "{queueName}", "{subscriptionName}");
```

Il est possible d'ajouter plusieurs BackgroundReceiver pour gérer différents événements.
