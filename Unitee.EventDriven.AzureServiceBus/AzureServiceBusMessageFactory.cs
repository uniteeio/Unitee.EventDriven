using Azure.Messaging.ServiceBus;
using Unitee.EventDriven.Models;

namespace Unitee.EventDriven.AzureServiceBus;

public static class ServiceBusMessageFactory
{
    public static ServiceBusMessage Create(MessageOptions options)
    {
        var msg = new ServiceBusMessage();

        if (options.ScheduledEnqueueTime is not null)
        {
            msg.ScheduledEnqueueTime = options.ScheduledEnqueueTime.Value;
        }

        if (options.MessageId is not null)
        {
            msg.MessageId = options.MessageId;
        }

        if (options.SessionId is not null)
        {
            msg.SessionId = options.SessionId;
        }

        return msg;
    }
}

