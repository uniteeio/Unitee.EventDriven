# Azure Service Bus

[https://github.com/uniteeio/service-bus](https://github.com/uniteeio/service-bus)

## Conceptes

---

### Les messages

Sont des classes POCO, échangées depuis un service bus. Les messages ont un “sujet” qui sert de discriminant lors la consomation.

### Le publisher

Le publisher est une classe qui permet de publier un message à travers un bus d’événement.

### Les consumers

Les consumers sont des classes capapbles de consumer un type de message en particulier.

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

Pour publier un message, on se sert d’un “publisher”. Une implementation d’un publisher pour Azure Service Bus est définit dans le package. On l’ajoute à notre projet .NET Core:

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
    private readonly IPublisher _publisher;

    MonController(IPublisher publisher)
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
        await _publisher.PublishAsync(new UtilisateurCreeEvent(3, new MessageOption()
        {
            Topic = "mon-topic"

            // d'autres options disponibles
        }));

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

public class MonConsumer: IConsumer<OrderSubmitted>
{
   private readonly ILogger<MonConsumer> log;

   /// Injection de dépendance disponible
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
        // parce que IConsumer<T> implémente IConsumer, c'est possible de
        //faire comme ça
        builder.Services.AddScoped<IConsumer, MonConsumer>();
    }
}
```

1. Ajouter un handler pour le type de message en entrée “ServiceBusReceivedMessage” (j’utilise celui fourni avec l’extension pour Azure Service Bus)

```csharp
builder.Services.AddScoped<
	IMessageHandler<ServiceBusReceivedMessage>,
	AzureServiceBusMessageHandler>();
```

1. Combiner le tout ensemble:

```csharp
// Mon Azure Fonction
public class ServiceBusTopicTrigger1
{
    private readonly ILogger<ServiceBusTopicTrigger1> _logger;
    private readonly IMessageHandler<ServiceBusReceivedMessage> _handler;

    public ServiceBusTopicTrigger1(ILogger<ServiceBusTopicTrigger1> log, IMessageHandler<ServiceBusReceivedMessage> handler)
    {
         _logger = log;
         _handler = handler;
    }

    [FunctionName("ServiceBusTopicTrigger1")]
     public async Task Run([ServiceBusTrigger("topic", "subscription", Connection = "sbhousebase_SERVICEBUS")]ServiceBusReceivedMessage message)
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
