using Unitee.EventDriven.Attributes;

namespace Unitee.EventDriven.Helpers;

public static class MessageHelper
{

    /// <summary>
    /// Get the subject of a message
    /// </summary>
    public static string GetSubject(Type t)
    {
        var maybeSubjectAttribute = (SubjectAttribute?)Attribute.GetCustomAttribute(t, typeof(SubjectAttribute));

        var maybeSubject = maybeSubjectAttribute?.Subject;

        if (maybeSubject is null)
        {
            // returns the name of the class if there is no attributes defined
            return t.Name;
        }

        return maybeSubject;
    }

    /// <summary>
    /// Get the subject of a message
    /// </summary>
    public static string GetSubject<T>()
    {
        return GetSubject(typeof(T));
    }
}