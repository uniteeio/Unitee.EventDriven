using ServiceBus.Abstraction;

namespace ServiceBus.AzureServiceBus;

public interface IAzureServiceBusConsumerWithContext<TMessage> : IConsumerWithContext<TMessage, IAzureServiceBusMessageContext> { }

public interface IAzureServiceBusConsumer<TMessage> : IConsumer<TMessage> { }