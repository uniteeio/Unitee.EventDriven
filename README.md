# Service bus

Service est une abstraction légère autour des services bus (et surtout Azure Service Bus, dans l'utilisateur "topic/subscription") pour faciliter leur utilisation.

# Configuration

Ajouter le service dans le `Program.cs`

```cs
builder.services.addAzureServiceBus(
    "{connectionString}", // chaîne de connexion vers le namespace
    "{defaultTopicName}" // topic par défaut si aucun spécifié
);
```

# Création d'un message

Un message est une simple classe POCO (ou record).

```cs
// Conseillé
public record OrderConfirmed(int OrderId);

// Ok
public class OrderConfirmed
{
    public int OrderId { get; set; }
}
```

Le nom du message est, par défaut, le nom de classe (ici: OrderConfirmed), mais peut être configuré via l'attribut `Subject`

```cs
[Subject("ORDER_CONFIRMED")]
public record OrderConfirmed(int OrderId);
```

Les messages sont formatés avec le sérializeur par défaut: `System.Text.Json`. Tous les attributs de sérialisation sont utilisés.

# Publication d'un message

```cs
public class MaClasse: Controller
{
    private readonly IPublisher _publisher;

    MaClasse(IPublisher publisher)
    {
        _publisher = publisher;
    }

    public record MonMessage(int Id, string FirstName, string LastName);

    public async Task<IActionResult> MonAction()
    {
        // envoie dans le topic par défaut
        await _publisher.PublishAsync(new MonMessage(3, "John", "Doe"));

        // envoie dans un topic particulier
        await _publisher.PublishAsync(new MonMessage(3, new MessageOption()
        {
            Topic = "mon-topic
            // d'autres options disponibles
        }));

        return Ok();
    }
}
```