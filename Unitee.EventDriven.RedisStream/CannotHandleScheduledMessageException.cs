namespace Unitee.EventDriven.RedisStream
{
    using System;
    using System.Runtime.Serialization;

    [Serializable]
    public class CannotHandleScheduledMessageException : Exception
    {
        public CannotHandleScheduledMessageException()
        {
        }

        public CannotHandleScheduledMessageException(string? message) : base(message)
        {
        }

        public CannotHandleScheduledMessageException(string? message, Exception? innerException) : base(message, innerException)
        {
        }

        protected CannotHandleScheduledMessageException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }
}