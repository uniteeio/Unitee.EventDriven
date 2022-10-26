using ServiceBus.Attributes;

namespace ServiceBus.Helpers;

public static class MessageHelper
{

    /// <summary>
    /// Get the subject of a message
    /// </summary>
    public static string GetSubject<T>()
    {
        var maybeSubjectAttribute = (SubjectAttribute?)Attribute.GetCustomAttribute(typeof(T), typeof(SubjectAttribute));

        var maybeSubject = maybeSubjectAttribute?.Subject;

        if (maybeSubject is null)
        {
            // returns the name of the class if there is no attributes defined
            return typeof(T).Name;
        }

        return maybeSubject;
    }
}