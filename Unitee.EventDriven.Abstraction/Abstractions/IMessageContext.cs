namespace Unitee.EventDriven.Abstraction;

public interface IMessageContext<TReturn>
{
    public Task<TReturn> AnswerAsync<TMessage>(TMessage message);
}