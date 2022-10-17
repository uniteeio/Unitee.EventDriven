using Azure.Messaging.ServiceBus;

namespace ServiceBus.Exceptions;

public class CannotParseException<TMessage> : Exception
{
    public TMessage ServiceBusMessage { get; }

    public CannotParseException(TMessage m) : base()
    {
        ServiceBusMessage = m;
    }
}