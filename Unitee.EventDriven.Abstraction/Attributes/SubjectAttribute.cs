namespace Unitee.EventDriven.Attributes;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public class SubjectAttribute : Attribute
{
    public SubjectAttribute(string subject)
    {
        Subject = subject;
    }

    public string Subject { get; init; }
}