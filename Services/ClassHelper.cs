using ServiceBus.Attributes;

namespace ServiceBus.Internal;

internal static class ClassHelper
{

    /// <summary>
    /// Récupère le sujet d'un message.
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