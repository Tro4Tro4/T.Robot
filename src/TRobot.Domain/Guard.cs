namespace TRobot.Domain;

internal static class Guard
{
    public static string Text(string value, int maxLength, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, name);
        value = value.Trim();
        if (value.Length > maxLength) throw new ArgumentOutOfRangeException(name);
        return value;
    }

    public static Guid Id(Guid value, string name) => value != Guid.Empty
        ? value : throw new ArgumentException("L'identificativo è obbligatorio.", name);
}
