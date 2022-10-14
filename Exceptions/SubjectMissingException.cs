
namespace ServiceBus.Exceptions;

public class SubjectMissingException : Exception
{
    public SubjectMissingException() : base("A subject is required to publish a message. Please decorate your message type with the [Subject()] attribute.")
    {
    }
}