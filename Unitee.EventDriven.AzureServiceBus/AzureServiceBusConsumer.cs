using Unitee.EventDriven.Abstraction;

namespace Unitee.EventDriven.AzureServiceBus;

public interface IAzureServiceBusConsumerWithContext<TMessage> : IConsumerWithContext<TMessage, IAzureServiceBusMessageContext> { }

public interface IAzureServiceBusConsumer<TMessage> : IConsumer<TMessage> { }