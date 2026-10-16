namespace TRobot.Domain;

public enum PollStatus { Draft, PublicationQueued, Closed, Cancelled }

public sealed class Poll : ILocalRecord
{
    private Poll() { }
    public Poll(Guid eventId, Guid id, string title, DateTime deadlineUtc)
    { EventId = Guard.Id(eventId, nameof(eventId)); Id = Guard.Id(id, nameof(id)); Set(title, deadlineUtc); }
    public Guid Id { get; private set; }
    public Guid EventId { get; private set; }
    public int Revision { get; private set; }
    public string Title { get; private set; } = null!;
    public DateTime DeadlineUtc { get; private set; }
    public PollStatus Status { get; private set; }
    public Guid? PublicationMessageId { get; private set; }
    public Guid? ResultMessageId { get; private set; }
    public Guid? DecisionOptionId { get; private set; }
    public DateTime? ClosedAtUtc { get; private set; }
    private void Set(string title, DateTime deadlineUtc)
    {
        var valid = Guard.Text(title, 200, nameof(title));
        if (deadlineUtc.Kind != DateTimeKind.Utc) throw new ArgumentException("La scadenza deve essere UTC.");
        Title = valid; DeadlineUtc = deadlineUtc;
    }
    public bool Edit(string title, DateTime deadlineUtc)
    {
        if (Status != PollStatus.Draft) throw new InvalidOperationException("Solo le bozze sono modificabili.");
        var candidate = new Poll(EventId, Id, title, deadlineUtc);
        if (Title == candidate.Title && DeadlineUtc == deadlineUtc) return false;
        Set(candidate.Title, deadlineUtc); Touch(); return true;
    }
    public void Publish(Guid messageId)
    {
        if (Status != PollStatus.Draft) throw new InvalidOperationException("Sondaggio non pubblicabile.");
        PublicationMessageId = Guard.Id(messageId, nameof(messageId)); Status = PollStatus.PublicationQueued; Touch();
    }
    public void Close(DateTime utc, Guid? decisionOptionId)
    {
        if (Status != PollStatus.PublicationQueued) throw new InvalidOperationException("Sondaggio non chiudibile.");
        if (utc.Kind != DateTimeKind.Utc || decisionOptionId == Guid.Empty) throw new ArgumentException("Chiusura non valida.");
        DecisionOptionId = decisionOptionId; ClosedAtUtc = utc; Status = PollStatus.Closed; Touch();
    }
    public void QueueResult(Guid messageId)
    {
        if (Status != PollStatus.Closed || ResultMessageId.HasValue) throw new InvalidOperationException("Risultato non accodabile.");
        ResultMessageId = Guard.Id(messageId, nameof(messageId)); Touch();
    }
    public void Cancel()
    {
        if (Status is not (PollStatus.Draft or PollStatus.PublicationQueued)) throw new InvalidOperationException("Sondaggio non annullabile.");
        Status = PollStatus.Cancelled; Touch();
    }
    public void Touch() => Revision = checked(Revision + 1);
}

public sealed class PollOption : ILocalRecord
{
    private PollOption() { }
    public PollOption(Guid eventId, Guid pollId, int position, string text)
    {
        EventId = Guard.Id(eventId, nameof(eventId)); PollId = Guard.Id(pollId, nameof(pollId));
        if (position is < 1 or > 10) throw new ArgumentException("Posizione opzione non valida.");
        Position = position; Text = Guard.Text(text, 150, nameof(text));
    }
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid EventId { get; private set; }
    public Guid PollId { get; private set; }
    public int Revision { get; private set; }
    public int Position { get; private set; }
    public string Text { get; private set; } = null!;
}

public sealed class PollVote : ILocalRecord
{
    private PollVote() { }
    public PollVote(Guid eventId, Guid pollId, Guid personId, Guid optionId)
    {
        EventId = Guard.Id(eventId, nameof(eventId)); PollId = Guard.Id(pollId, nameof(pollId));
        PersonId = Guard.Id(personId, nameof(personId)); OptionId = Guard.Id(optionId, nameof(optionId));
    }
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid EventId { get; private set; }
    public Guid PollId { get; private set; }
    public Guid PersonId { get; private set; }
    public Guid OptionId { get; private set; }
    public int Revision { get; private set; }
    public void Change(Guid optionId)
    {
        var valid = Guard.Id(optionId, nameof(optionId));
        if (valid == OptionId) return;
        OptionId = valid; Revision = checked(Revision + 1);
    }
}

public sealed class PollVoteReceipt : ILocalRecord
{
    private PollVoteReceipt() { }
    public PollVoteReceipt(Guid eventId, Guid pollId, Guid id, Guid actorId, Guid personId, Guid optionId, DateTime utc)
    {
        EventId = Guard.Id(eventId, nameof(eventId)); PollId = Guard.Id(pollId, nameof(pollId)); Id = Guard.Id(id, nameof(id));
        ActorId = Guard.Id(actorId, nameof(actorId)); PersonId = Guard.Id(personId, nameof(personId)); OptionId = Guard.Id(optionId, nameof(optionId));
        if (utc.Kind != DateTimeKind.Utc) throw new ArgumentException("La ricevuta deve essere UTC.");
        AtUtc = utc;
    }
    public Guid Id { get; private set; }
    public Guid EventId { get; private set; }
    public Guid PollId { get; private set; }
    public int Revision { get; private set; }
    public Guid ActorId { get; private set; }
    public Guid PersonId { get; private set; }
    public Guid OptionId { get; private set; }
    public DateTime AtUtc { get; private set; }
}
