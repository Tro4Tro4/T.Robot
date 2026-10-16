namespace TRobot.Domain;

public enum QuizStatus { Draft, PublicationQueued, Closed, Cancelled }

public sealed class GameMembership : ILocalRecord
{
    private GameMembership() { }
    public GameMembership(Guid eventId, Guid personId)
    { EventId = Guard.Id(eventId, nameof(eventId)); PersonId = Guard.Id(personId, nameof(personId)); }
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid EventId { get; private set; }
    public Guid PersonId { get; private set; }
    public int Revision { get; private set; }
    public bool IsActive { get; private set; } = true;
    public bool SetActive(bool active)
    { if (IsActive == active) return false; IsActive = active; Revision = checked(Revision + 1); return true; }
}

public sealed class Quiz : ILocalRecord
{
    private Quiz() { }
    public Quiz(Guid eventId, Guid id, string question, DateTime deadlineUtc, int correctPosition)
    {
        EventId = Guard.Id(eventId, nameof(eventId)); Id = Guard.Id(id, nameof(id));
        Question = Guard.Text(question, 200, nameof(question));
        if (deadlineUtc.Kind != DateTimeKind.Utc || correctPosition is < 1 or > 10) throw new ArgumentException("Quiz non valido.");
        DeadlineUtc = deadlineUtc; CorrectPosition = correctPosition;
    }
    public Guid Id { get; private set; }
    public Guid EventId { get; private set; }
    public int Revision { get; private set; }
    public string Question { get; private set; } = null!;
    public DateTime DeadlineUtc { get; private set; }
    public int CorrectPosition { get; private set; }
    public QuizStatus Status { get; private set; }
    public Guid? PublicationMessageId { get; private set; }
    public DateTime? ClosedAtUtc { get; private set; }
    public void Publish(Guid messageId)
    {
        if (Status != QuizStatus.Draft) throw new InvalidOperationException("Quiz non pubblicabile.");
        PublicationMessageId = Guard.Id(messageId, nameof(messageId)); Status = QuizStatus.PublicationQueued; Touch();
    }
    public void Close(DateTime utc)
    {
        if (Status != QuizStatus.PublicationQueued) throw new InvalidOperationException("Quiz non chiudibile.");
        if (utc.Kind != DateTimeKind.Utc) throw new ArgumentException("Chiusura UTC obbligatoria.");
        ClosedAtUtc = utc; Status = QuizStatus.Closed; Touch();
    }
    public void Cancel()
    {
        if (Status is not (QuizStatus.Draft or QuizStatus.PublicationQueued)) throw new InvalidOperationException("Quiz non annullabile.");
        Status = QuizStatus.Cancelled; Touch();
    }
    public void Touch() => Revision = checked(Revision + 1);
}

public sealed class QuizOption : ILocalRecord
{
    private QuizOption() { }
    public QuizOption(Guid eventId, Guid quizId, int position, string text)
    {
        EventId = Guard.Id(eventId, nameof(eventId)); QuizId = Guard.Id(quizId, nameof(quizId));
        if (position is < 1 or > 10) throw new ArgumentException("Posizione non valida.");
        Position = position; Text = Guard.Text(text, 150, nameof(text));
    }
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid EventId { get; private set; }
    public Guid QuizId { get; private set; }
    public int Revision { get; private set; }
    public int Position { get; private set; }
    public string Text { get; private set; } = null!;
}

// Immutable answer also acts as the historical operation receipt.
public sealed class QuizAnswer : ILocalRecord
{
    private QuizAnswer() { }
    public QuizAnswer(Guid eventId, Guid quizId, Guid operationId, Guid actorId, Guid personId, Guid optionId, DateTime utc)
    {
        EventId = Guard.Id(eventId, nameof(eventId)); QuizId = Guard.Id(quizId, nameof(quizId)); Id = Guard.Id(operationId, nameof(operationId));
        ActorId = Guard.Id(actorId, nameof(actorId)); PersonId = Guard.Id(personId, nameof(personId)); OptionId = Guard.Id(optionId, nameof(optionId));
        if (utc.Kind != DateTimeKind.Utc) throw new ArgumentException("Risposta UTC obbligatoria.");
        AtUtc = utc;
    }
    public Guid Id { get; private set; }
    public Guid EventId { get; private set; }
    public Guid QuizId { get; private set; }
    public int Revision { get; private set; }
    public Guid ActorId { get; private set; }
    public Guid PersonId { get; private set; }
    public Guid OptionId { get; private set; }
    public DateTime AtUtc { get; private set; }
}
