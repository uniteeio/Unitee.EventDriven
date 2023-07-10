using CSharpFunctionalExtensions;

namespace Unitee.EventDriven.Abstraction;

public interface IMessageContext<TReturn>
{
    public Task<TReturn> ReplyAsync<TMessage>(TMessage message);
    public Maybe<string> Locale { get; }
}
