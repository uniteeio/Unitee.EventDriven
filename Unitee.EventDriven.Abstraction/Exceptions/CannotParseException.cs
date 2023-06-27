namespace Unitee.EventDriven.Exceptions;

public class CannotParseException<TMessage> : Exception
{
    public TMessage ServiceBusMessage { get; }

    public CannotParseException(TMessage m) : base()
    {
        ServiceBusMessage = m;
    }
}
