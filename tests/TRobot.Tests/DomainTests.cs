using TRobot.Domain;

namespace TRobot.Tests;

public class DomainTests
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void PersonRequiresName(string name) =>
        Assert.Throws<ArgumentException>(() => new Person(name));

    [Fact]
    public void PersonAllowsCompanionWithoutWhatsApp()
    {
        var person = new Person(" Accompagnatore ");
        Assert.Equal("Accompagnatore", person.DisplayName);
        Assert.Null(person.WhatsAppSenderId);
    }

    [Fact]
    public void NamesHaveLengthLimit() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new Person(new string('a', 151)));

    [Fact]
    public void EventRejectsNonUtc() =>
        Assert.Throws<ArgumentException>(() => new Event("Evento", new DateTime(2026, 12, 31)));

    [Fact]
    public void EventRejectsUnknownTimeZone() =>
        Assert.Throws<TimeZoneNotFoundException>(() => new Event("Evento", DateTime.UtcNow, "Invalid/Zone"));

    [Fact]
    public void ParticipationStartsWithoutResponse()
    {
        var participation = new EventParticipant(Guid.NewGuid(), Guid.NewGuid());
        Assert.Equal(ParticipationStatus.NoResponse, participation.Status);
        Assert.Equal(DateTimeKind.Utc, participation.CreatedAtUtc.Kind);
    }

    [Fact]
    public void ParticipationRejectsEmptyReferences()
    {
        Assert.Throws<ArgumentException>(() => new EventParticipant(Guid.Empty, Guid.NewGuid()));
        Assert.Throws<ArgumentException>(() => new EventParticipant(Guid.NewGuid(), Guid.Empty));
    }

    [Fact]
    public void OrganizerRejectsInvalidRole() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new EventOrganizer(Guid.NewGuid(), Guid.NewGuid(), (OrganizerRole)99));
}
