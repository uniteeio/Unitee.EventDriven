namespace ServiceBus.Abstraction;

/// <summary>
/// Juste pour pouvoir récupérer la liste de tous les consumers depuis l'injection de dépendances.
/// Ne doit jamais être implémenté directement sans passer par IConsummer<T>.
/// </summary>
public interface IConsumer { }

public interface IConsumer<in T>: IConsumer
{
    public Task ConsumeAsync(T message);
}