namespace Unitee.EventDriven.Abstraction;

public interface IMessageHandler<T>
{
    /// <summary>
    /// Appelle le bon consumer enregistré pour le message de type T
    /// </summary>
    /// <returns>
    /// True si un consumer a été trouvé et appelé, false sinon.
    /// </returns>
    public Task<bool> HandleAsync(T originalMessage);
}