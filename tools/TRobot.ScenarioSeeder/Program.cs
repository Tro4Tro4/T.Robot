using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TRobot.Application.Accounting;
using TRobot.Application.Attendance;
using TRobot.Application.Events;
using TRobot.Application.Games;
using TRobot.Application.Households;
using TRobot.Application.Local;
using TRobot.Application.Menu;
using TRobot.Application.Messaging;
using TRobot.Application.Polls;
using TRobot.Application.Tasks;
using TRobot.Domain;
using TRobot.Infrastructure.Persistence;

try
{
    if (args.Contains("--help"))
    {
        Console.WriteLine("T.Robot scenario: --database <existing SQLite path> [--apply] [--report-path <.local/path.json>]. Without --apply: read-only verification.");
        return;
    }
    string? Option(string name) => Array.IndexOf(args, name) is var index && index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    var database = Path.GetFullPath(Option("--database") ?? throw new ArgumentException("--database obbligatorio."));
    if (!File.Exists(database)) throw new ArgumentException("Database esistente obbligatorio; nessuna creazione o migrazione automatica.");
    var local = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, ".local"));
    if (!Directory.Exists(local)) throw new ArgumentException("Eseguire dalla radice repository con directory .local esistente.");
    var report = Path.GetFullPath(Option("--report-path") ?? Path.Combine(local, "capodanno-scenario-report.json"));
    if (!report.StartsWith(local + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || report == database)
        throw new ArgumentException("Report ammesso solo in .local, separato dal database.");
    var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(database.ToUpperInvariant())));
    // A file handle works across async continuations; named Mutex ownership is thread-affine.
    using var runLock = new FileStream(Path.Combine(local, "capodanno-seeder-" + hash[..16] + ".lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    {
        var apply = args.Contains("--apply");
        var journalPath = Path.Combine(local, "capodanno-seeder-" + hash[..16] + ".json");
        var completed = File.Exists(journalPath) && JsonSerializer.Deserialize<Journal>(await File.ReadAllTextAsync(journalPath))?.Complete == true;
        var connection = new SqliteConnectionStringBuilder { DataSource = database, Mode = apply && !completed ? SqliteOpenMode.ReadWrite : SqliteOpenMode.ReadOnly, ForeignKeys = true };
        await using var db = new TRobotDbContext(new DbContextOptionsBuilder<TRobotDbContext>().UseSqlite(connection.ToString()).Options);
        var scenario = new Scenario(db, journalPath);
        await scenario.Run(apply, report);
    }
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Scenario non completato: {ex.GetType().Name}: {ex.Message}");
    Console.Error.WriteLine(ex.StackTrace);
    Environment.ExitCode = 1;
}

sealed class Scenario
{
    const string Marker = "[TRobot.ScenarioSeeder:capodanno-2027:v1]";
    const string Name = "SIMULAZIONE — Capodanno al Casale, 20 persone";
    readonly TRobotDbContext db;
    readonly string journalPath;
    readonly EventManagementService events;
    readonly OrganizerAttendanceService attendance;
    readonly HouseholdService households;
    readonly MenuService menu;
    readonly TaskService tasks;
    readonly AccountingService accounting;
    readonly PollService polls;
    readonly GameService games;
    readonly MessageService messages;
    Guid actor, eventId;
    Journal journal = new();
    readonly List<Guid> people = [];
    readonly List<Guid> adults = [];
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    public Scenario(TRobotDbContext context, string path)
    {
        db = context; journalPath = path;
        // Fixed creation clock keeps synthetic publication timestamps repeatable, outside quiet hours.
        TimeProvider clock = new ScenarioClock();
        var store = new LocalStore(db, clock);
        var operations = new LocalOperations(store, clock);
        events = new(new EventManagementStore(db), clock);
        attendance = new(new AttendanceStore(db), clock);
        households = new(store, operations); menu = new(store, operations); tasks = new(store, operations);
        accounting = new(store, operations, clock); polls = new(store, operations, clock);
        games = new(store, operations, clock); messages = new(store, operations, clock);
    }
    public async Task Run(bool apply, string report)
    {
        actor = await db.Users.AsNoTracking().Where(x => x.IsEnabled && x.IsSystemAdministrator).OrderBy(x => x.PersonId).Select(x => x.PersonId).FirstOrDefaultAsync();
        Require(actor != Guid.Empty, "Amministratore esistente abilitato obbligatorio.");
        var existing = await db.Events.AsNoTracking().Where(x => x.Program != null && x.Program.Contains(Marker)).ToListAsync();
        Require(existing.Count <= 1, "Marker duplicato: intervento manuale necessario.");
        if (File.Exists(journalPath)) journal = JsonSerializer.Deserialize<Journal>(await File.ReadAllTextAsync(journalPath)) ?? throw new InvalidOperationException("Journal non valido.");
        eventId = existing.SingleOrDefault()?.Id ?? Guid.Empty;
        Require(journal.EventId == Guid.Empty || journal.EventId == eventId, "Journal e database non corrispondono.");
        if (eventId == Guid.Empty)
        {
            Require(apply, "Scenario assente. Usare --apply per crearlo.");
            Require(!journal.Complete, "Journal completo ma evento assente: rifiuto ricreazione.");
            eventId = await events.CreateAsync(actor, new(Name, Local(12, 30, 15), "Europe/Rome", "Casale delle Querce — località inventata",
                Marker + "\n30/12: arrivo 15:00, camere, cena 20:00.\n31/12: colazione, passeggiata, pranzo, cenone 20:30 e brindisi.\n01/01: colazione, pranzo, giochi e cena.\n02/01: colazione, pulizie e partenza 11:00.\n4 giorni, 3 notti, 20 persone; 3 colazioni, 2 pranzi e 3 cene: 160 coperti. Dati interamente fittizi."));
            journal.EventId = eventId; SaveJournal();
        }
        await events.GetAsync(actor, eventId); // Existing administrator must also be authorized for this event.
        if (apply && !journal.Complete)
        {
            Require(journal.EventId == eventId, "Evento presente senza journal gestito: rifiuto di sovrascrivere dati.");
            await Populate();
        }
        var result = await Verify();
        if (apply && !journal.Complete) { journal.Complete = true; SaveJournal(); }
        Directory.CreateDirectory(Path.GetDirectoryName(report)!);
        await File.WriteAllTextAsync(report, JsonSerializer.Serialize(result, Json));
        Console.WriteLine($"VERIFIED scenario EventId={eventId}; 20 partecipanti, 3600 EUR, pagamenti confermati 800 EUR. Report: {report}");
    }
    void SaveJournal()
    {
        var temporary = journalPath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(journal, Json));
        File.Move(temporary, journalPath, true);
    }
    async Task Step(string key, Func<Task> operation)
    {
        if (journal.Done.Contains(key)) return;
        await operation(); journal.Done.Add(key); SaveJournal();
    }
    Guid Id(string key) => new(SHA256.HashData(Encoding.UTF8.GetBytes(eventId + "/" + key)).AsSpan(0, 16));
    static DateTime Local(int month, int day, int hour = 12) => new(month == 1 ? 2027 : 2026, month, day, hour, 0, 0, DateTimeKind.Unspecified);
    async Task<T?> Row<T>(Guid id) where T : class, ILocalRecord => await db.Set<T>().AsNoTracking().SingleOrDefaultAsync(x => x.EventId == eventId && x.Id == id);
    async Task Populate()
    {
        string[][] names = [["Luca Rinaldi", "Marta Rinaldi", "Sofia Rinaldi"], ["Paolo Bellini", "Giulia Bellini", "Tommaso Bellini"], ["Davide Conti", "Elena Conti", "Alice Conti"], ["Marco Serra", "Chiara Serra", "Pietro Serra"], ["Andrea Moretti", "Laura Moretti"], ["Stefano Galli", "Francesca Galli"], ["Matteo Costa", "Sara Costa"], ["Alessandro Greco", "Valentina Greco"]];
        for (var h = 0; h < names.Length; h++)
        {
            var hh = Id("household/" + h);
            await Step("household/" + h, async () => { await households.CreateAsync(actor, eventId, "Nucleo " + names[h][0].Split(' ')[1], hh); });
            for (var p = 0; p < names[h].Length; p++)
            {
                var key = $"person/{h}/{p}";
                var person = await events.AddParticipantAsync(actor, eventId, Id(key), names[h][p], p < 2);
                people.Add(person); if (p < 2) adults.Add(person);
                await Step(key + "/confirmed", async () => {
                    var presence = await attendance.GetAsync(actor, eventId, person);
                    if (presence.Status != ParticipationStatus.Confirmed)
                        await attendance.SetAsync(actor, new(eventId, person, Id(key + "/attendance"), ParticipationStatus.Confirmed, presence.Version, true));
                });
                await Step(key + "/member", async () => {
                    if (await Row<HouseholdMember>(Id(key + "/member")) is null)
                    {
                        var current = (await households.ListAsync(actor, eventId)).Households.Single(x => x.Id == hh);
                        await households.AddAsync(actor, eventId, hh, new(person, p < 2 ? HouseholdMemberKind.Adult : HouseholdMemberKind.Child, null, current.Revision, 0, true, Id(key + "/member")));
                    }
                });
            }
        }
        await Step("accounting-settings", async () => { var x = await accounting.ListAsync(actor, eventId); await accounting.ConfigureAsync(actor, eventId, .5m, x.Settings.Revision, true); });
        await SeedMenuAndTasks();
        (string text, decimal amount, int owner)[] expenses = [("Alloggio tre notti",1800,0),("Cena 30 dicembre",260,2),("Cenone 31 dicembre",480,1),("Colazioni tre mattine",156,2),("Pranzi 31 dicembre e 1 gennaio",220,3),("Cena 1 gennaio",180,3),("Analcolici",96,3),("Vino e birra",200,1),("Spumante",72,1),("Materiali e pulizia",86,2),("Giochi e premi simbolici",50,3),("Legna extra da approvare",120,3),("Scontrino duplicato respinto",45,2)];
        for (var i = 0; i < expenses.Length; i++)
        {
            var n = i; var e = expenses[i]; var id = Id("expense/" + i);
            await Step("expense/" + i, async () => {
                await accounting.DeclareExpenseAsync(actor, eventId, new(id, adults[e.owner * 2], e.text, e.amount, true));
                var row = (await Row<Expense>(id))!;
                var target = n < 11 ? ExpenseStatus.Approved : n == 12 ? ExpenseStatus.Rejected : row.Status;
                if (row.Status != target) await accounting.ExpenseTransitionAsync(actor, eventId, id, row.Revision, target, true);
            });
        }
        for (var i = 0; i < 7; i++)
        {
            var n = i; var id = Id("payment/" + i);
            await Step("payment/" + i, async () => {
                await accounting.DeclarePaymentAsync(actor, eventId, new(id, adults[8 + n], adults[(n % 4) * 2], n < 4 ? 200 : n < 6 ? 100 : 50, true));
                var row = (await Row<Payment>(id))!;
                var target = n < 4 ? PaymentStatus.Confirmed : n == 6 ? PaymentStatus.Cancelled : row.Status;
                if (row.Status != target) await accounting.PaymentTransitionAsync(actor, eventId, id, row.Revision, target, true);
            });
        }
        await Step("settings", async () => { var s = (await messages.ListAsync(actor, eventId)).Settings; await messages.ConfigureAsync(actor, eventId, new(false,23,9,3,5,s.Revision)); });
        for (var i = 0; i < 3; i++) await SeedPoll(i);
        for (var i = 0; i < 8; i++)
        {
            var p = adults[i]; await Step("game-member/" + i, async () => { if (!(await games.ListAsync(actor,eventId)).People.Any(x => x.Person.Id == p && x.Membership?.IsActive == true)) await games.MembershipAsync(actor,eventId,p,0,true,true); });
        }
        for (var i = 0; i < 2; i++) await SeedQuiz(i);
        string[] texts = ["Benvenuti al Casale! Arrivo il 30 dicembre dalle 15:00: sistemazione e cena alle 20:00. Scenario interamente simulato.", "Bozza: promemoria raccolta materiali per pulizie e riconsegna chiavi.", "Promemoria simulato: cenone il 31 dicembre alle 20:30; preparazione tavoli dalle 19:30.", "Promemoria simulato: il 2 gennaio colazione alle 09:00, riordino e partenza entro le 11:00."];
        for (var i = 0; i < texts.Length; i++)
        {
            var n = i; var id = Id("message/" + i);
            await Step("message/" + i, async () => {
                if (await Row<OutboundMessage>(id) is null) await messages.SaveDraftAsync(actor,eventId,null,new(id,texts[n],MessageKind.Operational,n < 2 ? null : Local(n == 2 ? 12 : 1,n == 2 ? 31 : 2,n == 2 ? 18 : 9),0));
                var row = (await Row<OutboundMessage>(id))!;
                if (n != 1 && row.Status == OutboundMessageStatus.Draft) await messages.TransitionAsync(actor,eventId,id,row.Revision,true,false);
                if (n == 0) await messages.TickAsync(actor,eventId,true);
            });
        }
    }
    async Task SeedMenuAndTasks()
    {
        (string name, decimal q, string unit, string note)[] food = [
            ("30/12 cena — lasagne al forno",20,"porzioni","Preparare in due teglie"),("30/12 cena — torta salata vegetariana",8,"porzioni","Alternativa vegetariana condivisa"),("30/12 cena — insalata mista",2,"kg","Condire al momento"),
            ("31/12 colazione — latte e bevande vegetali",5,"litri","Colazione per 20"),("31/12 colazione — pane e confetture",20,"porzioni","Preparazione dalle 08:30"),("31/12 pranzo — pasta al pomodoro",2,"kg","Peso pasta secca"),("31/12 pranzo — verdure grigliate",3,"kg","Vassoio condiviso"),
            ("31/12 cenone — antipasti misti",20,"porzioni","Servire alle 20:30"),("31/12 cenone — risotto ai funghi",2,"kg","Peso riso secco; brodo vegetale"),("31/12 cenone — arrosto al forno",4,"kg","Affettare prima del servizio"),("31/12 cenone — polpettone di lenticchie",8,"porzioni","Alternativa vegetariana"),("31/12 cenone — patate arrosto",5,"kg","Due infornate"),("31/12 cenone — lenticchie di mezzanotte",2,"kg","Peso cotto"),("31/12 cenone — pandoro e panettone",4,"pezzi","Scelta dolce ancora al voto"),
            ("01/01 colazione — yogurt e frutta",20,"porzioni","Colazione flessibile"),("01/01 colazione — biscotti",2,"kg","Cestini condivisi"),("01/01 pranzo — pasta al forno",20,"porzioni","Preparazione dalle 11:30"),("01/01 pranzo — insalata di legumi",8,"porzioni","Alternativa vegetariana"),("01/01 cena — pizza in teglia",8,"teglie","Impasto nel pomeriggio"),("01/01 cena — verdure al forno",3,"kg","Accompagnamento"),("02/01 colazione — pane e miele",20,"porzioni","Ultima colazione ore 09:00"),("02/01 colazione — frutta fresca",3,"kg","Consumare prima della partenza"),
            ("Acqua per quattro giorni",60,"litri","Scorta comune"),("Succhi e bibite",16,"litri","Scorta comune"),("Vino e birra",24,"bottiglie","Solo adulti"),("Spumante per brindisi",6,"bottiglie","Brindisi 31 dicembre"),("Caffè e tè",1,"kit","Tre mattine"),("Tovaglioli e tovaglie",1,"kit","Per gli otto pasti"),("Sacchi raccolta differenziata",30,"pezzi","Seguire indicazioni del casale"),("Contenitori per avanzi",12,"pezzi","Etichettare data e contenuto")];
        for (var i = 0; i < food.Length; i++)
        {
            var n = i; var f = food[i]; var free = i % 7 == 0;
            await Step("menu/" + i, async () => { await menu.SaveAsync(actor,eventId,new(Id("menu/"+n),f.name,n < 22 ? MenuCategory.Food : n < 27 ? MenuCategory.Drink : MenuCategory.Other,f.q,f.unit,free ? null : adults[n%adults.Count],free ? MenuAvailability.Planned : n%3==0 ? MenuAvailability.Ready : MenuAvailability.Assigned,f.note,null,false),true); });
        }
        string[] titles = ["Confermare prenotazione e saldo casale","Organizzare auto e orari di arrivo","Assegnare camere ai nuclei","Compilare lista spesa condivisa","Acquistare dispensa e colazioni","Ritirare bevande","Portare giochi da tavolo","Controllare dotazione cucina","Preparare lasagne del 30 dicembre","Apparecchiare cena di arrivo","Organizzare passeggiata del 31","Preparare antipasti del cenone","Preparare risotto e secondi","Allestire tavola cenone","Preparare brindisi di mezzanotte","Organizzare giochi del 1 gennaio","Preparare impasto pizza","Riordinare cucina dopo cena","Raccolta differenziata finale","Pulire camere e riconsegnare chiavi"];
        for (var i = 0; i < titles.Length; i++)
        {
            var n = i; var id = Id("task/"+i);
            var input = new TaskEdit(titles[i],"Incarico dello scenario simulato; coordinarsi con il nucleo e segnare l'avanzamento.",i % 4 == 0 ? TaskPriority.High : TaskPriority.Normal,OrganizationTaskStatus.Todo,i % 6 == 0 ? null : adults[i%16],i<8 ? Local(12,29,18) : i<10 ? Local(12,30,19) : i<15 ? Local(12,31,18) : i<18 ? Local(1,1,18) : Local(1,2,10),0,false,id);
            await Step("task/"+i, async () => {
                if (await Row<OrganizationTask>(id) is null) await tasks.CreateAsync(actor,eventId,input);
                var row = (await Row<OrganizationTask>(id))!;
                var target = n < 4 ? OrganizationTaskStatus.Completed : n < 8 ? OrganizationTaskStatus.InProgress : OrganizationTaskStatus.Todo;
                if (row.Status != target) await tasks.UpdateAsync(actor,eventId,id,input with { Status=target, Revision=row.Revision });
            });
        }
    }
    async Task SeedPoll(int n)
    {
        var id = Id("poll/"+n);
        string[] titles = ["Dolce del cenone", "Attività del primo gennaio", "Orario del cenone"];
        string[][] options = [["Pandoro con crema", "Panettone", "Macedonia"], ["Passeggiata al casale", "Torneo giochi da tavolo", "Film e relax"], ["20:00", "20:30", "21:00"]];
        await Step("poll/"+n, async () => { if (await Row<Poll>(id) is null) await polls.SaveAsync(actor,eventId,null,new(id,titles[n],Local(12,29,18),options[n],0)); });
        await Step("poll-publish/"+n, async () => { var p = await polls.ReadAsync(actor,eventId,id); if (p.Publication is null) await polls.PublishAsync(actor,eventId,id,p.Poll.Revision,true); await messages.TickAsync(actor,eventId,true); });
        for (var v = 0; v < 12; v++)
        {
            var ballot = v; await Step($"poll-vote/{n}/{v}", async () => { var page = await polls.ReadAsync(actor,eventId,id); await polls.VoteAsync(actor,eventId,id,new(Id($"poll-vote/{n}/{ballot}"),adults[ballot],page.Counts[n==2 && ballot < 9 ? 1 : ballot%3].Option.Id,page.Poll.Revision,true)); });
        }
        if (n == 2) await Step("poll-close", async () => {
            var p=await polls.ReadAsync(actor,eventId,id);
            if (p.Poll.Status != PollStatus.Closed) await polls.CloseAsync(actor,eventId,id,p.Poll.Revision,true,p.Counts[1].Option.Id);
            p=await polls.ReadAsync(actor,eventId,id); if (p.Result is null) await polls.QueueResultAsync(actor,eventId,id,p.Poll.Revision,true);
            await messages.TickAsync(actor,eventId,true);
        });
    }
    async Task SeedQuiz(int n)
    {
        var id=Id("quiz/"+n);
        await Step("quiz/"+n,async()=> { if(await Row<Quiz>(id) is null) await games.CreateAsync(actor,eventId,new(id,n==0 ? "Quiz di riscaldamento: quanti secondi dura un minuto?" : "Quiz aperto: quale pianeta è noto come pianeta rosso?",Local(12,31,22),n==0 ? ["30","60","100"] : ["Venere","Marte","Giove"],2)); });
        await Step("quiz-publish/"+n,async()=> { var q=await games.ReadAsync(actor,eventId,id); if(q.Publication is null) await games.PublishAsync(actor,eventId,id,q.Quiz.Revision,true); await messages.TickAsync(actor,eventId,true); });
        for(var i=0;i<(n==0?8:3);i++)
        {
            var p=i; await Step($"quiz-answer/{n}/{i}",async()=> { var q=await games.ReadAsync(actor,eventId,id); await games.AnswerAsync(actor,eventId,id,new(Id($"quiz-answer/{n}/{p}"),adults[p],q.Options[p%3==0?0:1].Id,q.Quiz.Revision,true)); });
        }
        if(n==0) await Step("quiz-close",async()=> {var q=await games.ReadAsync(actor,eventId,id); if(q.Quiz.Status!=QuizStatus.Closed) await games.CloseAsync(actor,eventId,id,q.Quiz.Revision,true);});
    }
    async Task<object> Verify()
    {
        var ledger=await accounting.ListAsync(actor,eventId);
        var hh=await households.ListAsync(actor,eventId);
        var m=await menu.ReadAsync(actor,eventId); var t=await tasks.ListAsync(actor,eventId);
        var p=await polls.ListAsync(actor,eventId); var g=await games.ListAsync(actor,eventId); var msg=await messages.ListAsync(actor,eventId);
        Require(ledger.Participants.Count==20 && ledger.Participants.All(x=>x.Status==ParticipationStatus.Confirmed),"Presenze non coerenti.");
        Require(hh.Households.Count==8 && hh.Members.Count(x=>x.IsActive && x.Kind==HouseholdMemberKind.Child)==4 && hh.Members.Count(x=>x.IsActive)==20,"Nuclei non coerenti.");
        Require(ledger.ApprovedTotal==3600m && ledger.Expenses.Count(x=>x.Status==ExpenseStatus.Approved)==11 && ledger.Expenses.Count==13,"Spese non coerenti.");
        Require(ledger.Balances.Count(x=>x.Share==200)==16 && ledger.Balances.Count(x=>x.Share==100)==4 && ledger.Balances.Sum(x=>x.Share)==3600 && ledger.ReconciliationResidual==0 && ledger.Unallocated==0,"Quote o riconciliazione non coerenti.");
        Require(ledger.Payments.Count==7 && ledger.Payments.Where(x=>x.Status==PaymentStatus.Confirmed).Sum(x=>x.Amount)==800,"Pagamenti non coerenti.");
        Require(ledger.Balances.Where(x=>x.Advanced>0).Select(x=>x.Advanced).Order().SequenceEqual(new decimal[]{502,546,752,1800}),"Anticipi non coerenti.");
        Require(m.Items.Count==30 && t.Tasks.Count==20 && m.Items.Any(x=>x.ResponsiblePersonId==null) && t.Tasks.Any(x=>x.Status==OrganizationTaskStatus.Completed) && t.Tasks.Any(x=>x.Status==OrganizationTaskStatus.InProgress),"Menu o attività incompleti.");
        Require(p.Polls.Count==3 && p.Polls.Count(x=>x.Status==PollStatus.Closed)==1 && g.Quizzes.Count==2 && g.Quizzes.Count(x=>x.Status==QuizStatus.Closed)==1 && g.People.Count(x=>x.Membership?.IsActive==true)==8 && g.Scores.Any(x=>x.Points>0),"Sondaggi o giochi incompleti.");
        foreach(var poll in p.Polls) {var page=await polls.ReadAsync(actor,eventId,poll.Id); Require(page.Votes.Count==12 && page.Publication?.Status==OutboundMessageStatus.Sent,"Voti/pubblicazione mancanti."); if(poll.Status==PollStatus.Closed) Require(page.Counts.Single(x=>x.Option.Id==poll.DecisionOptionId).Option.Text=="20:30","Decisione errata.");}
        Require(!msg.Settings.AutomaticEnabled && msg.Messages.Any(x=>x.Status==OutboundMessageStatus.Draft) && msg.Messages.Any(x=>x.Status==OutboundMessageStatus.Queued) && msg.Messages.Any(x=>x.Status==OutboundMessageStatus.Sent),"Coda o scheduler non coerenti.");
        Require(!await db.Set<WhatsAppConnection>().AsNoTracking().AnyAsync(x=>x.EventId==eventId),"Connessione WhatsApp inattesa.");
        return new { EventId=eventId, EventName=Name, Scenario=Marker, ScenarioAsOfUtc=new ScenarioClock().GetUtcNow(), Participants=20, Adults=16, Children=4, Households=8, Nights=3, MealCovers=160, MenuItems=m.Items.Count, Tasks=t.Tasks.Count, ApprovedExpenses=11, ApprovedTotal=ledger.ApprovedTotal, ConfirmedPayments=800, ledger.ReconciliationResidual, ledger.Unallocated, Balances=ledger.Balances, Polls=p.Polls.Count, Quizzes=g.Quizzes.Count, GameMembers=8, Messages=msg.Messages.GroupBy(x=>x.Status).ToDictionary(x=>x.Key.ToString(),x=>x.Count()), AutomaticScheduler=false, WhatsAppConnections=0, VerifiedAtUtc=DateTime.UtcNow };
    }
    static void Require(bool condition,string message) {if(!condition) throw new InvalidOperationException(message);}
}
sealed class Journal
{
    public Guid EventId {get;set;}
    public bool Complete {get;set;}
    public HashSet<string> Done {get;set;}=[];
}
sealed class ScenarioClock : TimeProvider
{
    public override DateTimeOffset GetUtcNow()=>new(2026,10,17,10,0,0,TimeSpan.Zero);
}
