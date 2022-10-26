namespace Unitee.EventDriven.Abstraction;

/// <summary>
/// Juste pour pouvoir récupérer la liste de tous les consumers depuis l'injection de dépendances.
/// Ne doit jamais être implémenté directement sans passer par IConsummer<T>.
/// </summary>
public interface IConsumer { }

public interface IConsumerWithContext<TMessage, TMessageContext> : IConsumer
{
    public Task ConsumeAsync(TMessage message, TMessageContext context);
}

public interface IConsumer<T> : IConsumer
{
    public Task ConsumeAsync(T message);
}
