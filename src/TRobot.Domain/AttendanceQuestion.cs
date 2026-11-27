namespace TRobot.Domain;

/// <summary>Domanda aggiuntiva dell'organizzatore posta dopo la conferma della presenza (es. "a piedi o con il gatto delle nevi").</summary>
public sealed class AttendanceQuestion : ILocalRecord
{
    public const int MaxOptions = 8;
    private AttendanceQuestion() { }
    public AttendanceQuestion(Guid eventId, string text, int position)
    {
        EventId = Guard.Id(eventId, nameof(eventId));
        Text = Guard.Text(text, 200, nameof(text));
        if (position < 0) throw new ArgumentOutOfRangeException(nameof(position));
        Position = position; IsActive = true;
    }
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid EventId { get; private set; }
    public string Text { get; private set; } = null!;
    public int Position { get; private set; }
    public bool IsActive { get; private set; }
    public int Revision { get; private set; }
    public bool SetActive(bool active)
    {
        if (IsActive == active) return false;
        IsActive = active; Revision++; return true;
    }
}

public sealed class AttendanceQuestionOption : ILocalRecord
{
    private AttendanceQuestionOption() { }
    public AttendanceQuestionOption(Guid eventId, Guid questionId, string text, int position)
    {
        EventId = Guard.Id(eventId, nameof(eventId)); QuestionId = Guard.Id(questionId, nameof(questionId));
        Text = Guard.Text(text, 60, nameof(text));
        if (position < 0) throw new ArgumentOutOfRangeException(nameof(position));
        Position = position;
    }
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid EventId { get; private set; }
    public Guid QuestionId { get; private set; }
    public string Text { get; private set; } = null!;
    public int Position { get; private set; }
    public int Revision { get; private set; }
}

/// <summary>Ultima risposta di una persona a una domanda; una sola riga per persona e domanda.</summary>
public sealed class AttendanceAnswer : ILocalRecord
{
    private AttendanceAnswer() { }
    public AttendanceAnswer(Guid eventId, Guid personId, Guid questionId, Guid optionId, Guid senderId, DateTime utc)
    {
        EventId = Guard.Id(eventId, nameof(eventId)); PersonId = Guard.Id(personId, nameof(personId));
        QuestionId = Guard.Id(questionId, nameof(questionId)); OptionId = Guard.Id(optionId, nameof(optionId));
        SenderId = Guard.Id(senderId, nameof(senderId));
        if (utc.Kind != DateTimeKind.Utc) throw new ArgumentException("La data deve essere UTC.", nameof(utc));
        AnsweredAtUtc = utc;
    }
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid EventId { get; private set; }
    public Guid PersonId { get; private set; }
    public Guid QuestionId { get; private set; }
    public Guid OptionId { get; private set; }
    /// <summary>Chi ha scritto la risposta: puo' essere il genitore per un bambino collegato.</summary>
    public Guid SenderId { get; private set; }
    public DateTime AnsweredAtUtc { get; private set; }
    public int Revision { get; private set; }
    public bool Change(Guid optionId, Guid senderId, DateTime utc)
    {
        if (OptionId == optionId && SenderId == senderId) return false;
        OptionId = Guard.Id(optionId, nameof(optionId)); SenderId = Guard.Id(senderId, nameof(senderId));
        AnsweredAtUtc = utc; Revision++; return true;
    }
}
