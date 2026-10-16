namespace TRobot.Domain;

public sealed class Person
{
    private Person() { }

    public Person(string displayName, string? whatsAppSenderId = null)
    {
        DisplayName = Guard.Text(displayName, 150, nameof(displayName));
        WhatsAppSenderId = whatsAppSenderId is null ? null
            : Guard.Text(whatsAppSenderId, 200, nameof(whatsAppSenderId));
    }

    public Guid Id { get; private set; } = Guid.NewGuid();
    public string DisplayName { get; private set; } = null!;
    public string? WhatsAppSenderId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; } = DateTime.UtcNow;
}
