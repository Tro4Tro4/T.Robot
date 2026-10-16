using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using TRobot.Application.Accounting;
using TRobot.Application.Local;
using TRobot.Domain;
using TRobot.Infrastructure.Persistence;
using LocalWebFactory = TRobot.Tests.WebFlowTests.LocalWebFactory;

namespace TRobot.Tests;

public sealed class AccountingTests
{
    private static readonly DateTime Now = new(2026, 12, 30, 11, 0, 0, DateTimeKind.Utc);
    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => new(Now); }
    private static AccountingService Service(IServiceProvider provider)
    { var store = provider.GetRequiredService<ILocalStore>(); var clock = new Clock(); return new(store, new LocalOperations(store, clock), clock); }
    private static ExpenseDeclaration ExpenseInput(Guid person, decimal amount = 100m) => new(Guid.NewGuid(), person, "Spesa comune", amount, true);
    private static PaymentDeclaration PaymentInput(Guid from, Guid to, decimal amount = 50m) => new(Guid.NewGuid(), from, to, amount, true);
    private static async Task<(Guid First, Guid Second)> ConfirmTwo(IServiceProvider provider, Guid eventId)
    {
        var db = provider.GetRequiredService<TRobotDbContext>();
        var first = await db.EventParticipants.SingleAsync(x => x.EventId == eventId);
        first.ChangeStatus(ParticipationStatus.Confirmed, Now);
        var second = new Person("Secondo partecipante"); var participation = new EventParticipant(eventId, second.Id);
        participation.ChangeStatus(ParticipationStatus.Confirmed, Now); db.AddRange(second, participation); await db.SaveChangesAsync();
        return (first.PersonId, second.Id);
    }

    [Theory]
    [InlineData(0)] [InlineData(-1)] [InlineData(1000001)] [InlineData(0.001)]
    public void InvalidMoneyNeverCreatesMovement(decimal amount)
    { Assert.Throws<ArgumentException>(() => new Expense(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Spesa", amount, Now)); }

    [Fact]
    public void DomainRetainsHistoryAndRejectsTransitionsWithoutMutation()
    {
        var ev = Guid.NewGuid(); var actor = Guid.NewGuid(); var person = Guid.NewGuid();
        var expense = new Expense(ev, Guid.NewGuid(), person, actor, " Spesa ", 1.01m, Now);
        Assert.Equal("Spesa", expense.Description);
        Assert.Throws<ArgumentException>(() => expense.Transition(ExpenseStatus.Approved, false, Now));
        Assert.Throws<ArgumentException>(() => expense.Transition((ExpenseStatus)99, true, Now));
        Assert.Throws<ArgumentException>(() => expense.Transition(ExpenseStatus.Approved, true, DateTime.SpecifyKind(Now, DateTimeKind.Unspecified)));
        Assert.Equal(0, expense.Revision); Assert.Equal(ExpenseStatus.Pending, expense.Status);
        Assert.True(expense.Transition(ExpenseStatus.Approved, true, Now)); Assert.False(expense.Transition(ExpenseStatus.Approved, true, Now));
        Assert.Throws<InvalidOperationException>(() => expense.Transition(ExpenseStatus.Rejected, true, Now));
        Assert.True(expense.Transition(ExpenseStatus.Cancelled, true, Now));
        Assert.Throws<InvalidOperationException>(() => expense.Transition(ExpenseStatus.Approved, true, Now));
        Assert.Equal(1.01m, expense.Amount); Assert.Equal(2, expense.Revision);
        Assert.Throws<ArgumentException>(() => new Payment(ev, Guid.NewGuid(), person, person, actor, 1m, Now));
        var payment = new Payment(ev, Guid.NewGuid(), person, Guid.NewGuid(), actor, 1m, Now);
        Assert.True(payment.Transition(PaymentStatus.Confirmed, true, Now)); Assert.True(payment.Transition(PaymentStatus.Cancelled, true, Now));
        Assert.Throws<InvalidOperationException>(() => payment.Transition(PaymentStatus.Confirmed, true, Now));
        var settings = new AccountingSettings(ev); Assert.Equal(0.5m, settings.ChildWeight);
        Assert.Throws<ArgumentException>(() => settings.Update(10.001m)); Assert.Equal(0.5m, settings.ChildWeight);
        Assert.Throws<ArgumentException>(() => settings.Update(0.0001m)); Assert.Equal(0, settings.Revision);
    }

    [Fact]
    public void LargestRemaindersPreserveCentsAndTieBreakByStablePersonId()
    {
        var a = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var b = Guid.Parse("00000000-0000-0000-0000-000000000002");
        var c = Guid.Parse("00000000-0000-0000-0000-000000000003");
        var people = new[] { new WeightedPerson(c, 1), new WeightedPerson(a, 1), new WeightedPerson(b, 1) };
        var third = ShareCalculator.Allocate(1m, people);
        Assert.Equal(0.34m, third.Shares[a]); Assert.Equal(0.33m, third.Shares[b]); Assert.Equal(0.33m, third.Shares[c]);
        Assert.Equal(1m, third.Shares.Values.Sum()); Assert.Equal(0m, third.Unallocated);
        var penny = ShareCalculator.Allocate(0.01m, people.Reverse().ToArray());
        Assert.Equal(0.01m, penny.Shares[a]); Assert.Equal(0m, penny.Shares[b]);
        var weighted = ShareCalculator.Allocate(100m, [new(a, 1m), new(b, 0.5m), new(c, 0m)]);
        Assert.Equal(66.67m, weighted.Shares[a]); Assert.Equal(33.33m, weighted.Shares[b]); Assert.Equal(0m, weighted.Shares[c]);
        var huge = ShareCalculator.Allocate(1000000000000.01m, [new(a, 10m), new(b, 0.001m)]);
        Assert.Equal(1000000000000.01m, huge.Shares.Values.Sum());
    }

    [Fact]
    public void NoPositiveWeightsExposesUnallocatedBudgetWithoutDivisionByZero()
    {
        var person = Guid.NewGuid(); var result = ShareCalculator.Allocate(42.01m, [new(person, 0)]);
        Assert.Equal(42.01m, result.Unallocated); Assert.Equal(0m, result.Shares[person]);
        Assert.Equal(42.01m, ShareCalculator.Allocate(42.01m, []).Unallocated);
        Assert.Throws<ArgumentException>(() => ShareCalculator.Allocate(1m, [new(person, 1), new(person, 1)]));
        Assert.Throws<ArgumentException>(() => ShareCalculator.Allocate(0.001m, []));
    }

    [Fact]
    public async Task ApprovalAndConfirmedReimbursementReconcileWithoutCountingPaymentsAsExpenses()
    {
        await using var factory = new LocalWebFactory(); using var scope = factory.Services.CreateScope();
        var (first, second) = await ConfirmTwo(scope.ServiceProvider, factory.EventId); var service = Service(scope.ServiceProvider);
        var input = ExpenseInput(first); var expense = await service.DeclareExpenseAsync(factory.OrganizerPersonId, factory.EventId, input);
        var page = await service.ListAsync(factory.OrganizerPersonId, factory.EventId); Assert.Equal(0m, page.ApprovedTotal);
        await Assert.ThrowsAsync<LocalValidationException>(() => service.ExpenseTransitionAsync(factory.OrganizerPersonId, factory.EventId, expense, 0, ExpenseStatus.Approved, false));
        await service.ExpenseTransitionAsync(factory.OrganizerPersonId, factory.EventId, expense, 0, ExpenseStatus.Approved, true);
        Assert.Equal(expense, await service.DeclareExpenseAsync(factory.OrganizerPersonId, factory.EventId, input with { Confirmed = false }));
        await Assert.ThrowsAsync<LocalConflictException>(() => service.DeclareExpenseAsync(factory.OrganizerPersonId, factory.EventId, input with { Amount = 99m }));
        page = await service.ListAsync(factory.OrganizerPersonId, factory.EventId);
        Assert.Equal(-50m, page.Balances.Single(x => x.PersonId == first).Balance); Assert.Equal(50m, page.Balances.Single(x => x.PersonId == second).Balance);
        var payInput = PaymentInput(second, first); var payment = await service.DeclarePaymentAsync(factory.OrganizerPersonId, factory.EventId, payInput);
        Assert.Equal(50m, (await service.ListAsync(factory.OrganizerPersonId, factory.EventId)).Balances.Single(x => x.PersonId == second).Balance);
        await service.PaymentTransitionAsync(factory.OrganizerPersonId, factory.EventId, payment, 0, PaymentStatus.Confirmed, true);
        page = await service.ListAsync(factory.OrganizerPersonId, factory.EventId);
        Assert.All(page.Balances, x => Assert.Equal(0m, x.Balance)); Assert.Equal(100m, page.ApprovedTotal); Assert.Equal(0m, page.ReconciliationResidual);
        Assert.Equal(payment, await service.DeclarePaymentAsync(factory.OrganizerPersonId, factory.EventId, payInput));
        await Assert.ThrowsAsync<LocalConflictException>(() => service.DeclarePaymentAsync(factory.OrganizerPersonId, factory.EventId, payInput with { Amount = 49m }));
        await Assert.ThrowsAsync<LocalConflictException>(() => service.PaymentTransitionAsync(factory.OrganizerPersonId, factory.EventId, payment, 0, PaymentStatus.Cancelled, true));
        Assert.False(await service.PaymentTransitionAsync(factory.OrganizerPersonId, factory.EventId, payment, 1, PaymentStatus.Confirmed, true));
        await service.PaymentTransitionAsync(factory.OrganizerPersonId, factory.EventId, payment, 1, PaymentStatus.Cancelled, true);
        await service.ExpenseTransitionAsync(factory.OrganizerPersonId, factory.EventId, expense, 1, ExpenseStatus.Cancelled, true);
        page = await service.ListAsync(factory.OrganizerPersonId, factory.EventId);
        Assert.All(page.Balances, x => Assert.Equal(0m, x.Balance)); Assert.Equal(0m, page.ApprovedTotal);
        Assert.Equal(ExpenseStatus.Cancelled, Assert.Single(page.Expenses).Status); Assert.Equal(PaymentStatus.Cancelled, Assert.Single(page.Payments).Status);
        var db = scope.ServiceProvider.GetRequiredService<TRobotDbContext>();
        Assert.Equal(6, await db.Set<LocalAudit>().CountAsync()); Assert.Empty(await db.Set<OutboundMessage>().ToListAsync());
    }

    [Fact]
    public async Task ChildWeightsOverridesAndAttendanceAreDynamicWhileAbsentAdvancesRemainVisible()
    {
        await using var factory = new LocalWebFactory(); using var scope = factory.Services.CreateScope();
        var (adult, child) = await ConfirmTwo(scope.ServiceProvider, factory.EventId); var db = scope.ServiceProvider.GetRequiredService<TRobotDbContext>();
        var household = new Household(factory.EventId, "Nucleo", Guid.NewGuid());
        var membership = new HouseholdMember(factory.EventId, household.Id, child, HouseholdMemberKind.Child, null, Guid.NewGuid());
        db.AddRange(household, membership); await db.SaveChangesAsync(); var service = Service(scope.ServiceProvider);
        var expense = await service.DeclareExpenseAsync(factory.OrganizerPersonId, factory.EventId, ExpenseInput(adult));
        await service.ExpenseTransitionAsync(factory.OrganizerPersonId, factory.EventId, expense, 0, ExpenseStatus.Approved, true);
        var page = await service.ListAsync(factory.OrganizerPersonId, factory.EventId);
        Assert.Equal(66.67m, page.Balances.Single(x => x.PersonId == adult).Share); Assert.Equal(33.33m, page.Balances.Single(x => x.PersonId == child).Share);
        await service.ConfigureAsync(factory.OrganizerPersonId, factory.EventId, 0m, 0, true);
        page = await service.ListAsync(factory.OrganizerPersonId, factory.EventId); Assert.Equal(0m, page.Balances.Single(x => x.PersonId == child).Share);
        var overrideId = Guid.NewGuid(); await service.OverrideAsync(factory.OrganizerPersonId, factory.EventId, child, overrideId, 1m, 0, true);
        Assert.False(await service.OverrideAsync(factory.OrganizerPersonId, factory.EventId, child, overrideId, 1m, 0, true));
        page = await service.ListAsync(factory.OrganizerPersonId, factory.EventId); Assert.All(page.Balances, x => Assert.Equal(50m, x.Share));
        await service.OverrideAsync(factory.OrganizerPersonId, factory.EventId, child, overrideId, null, 0, true);
        await Assert.ThrowsAsync<LocalConflictException>(() => service.OverrideAsync(factory.OrganizerPersonId, factory.EventId, child, overrideId, 1m, 0, true));
        var member = await db.Set<HouseholdMember>().SingleAsync(); member.Update(HouseholdMemberKind.Child, null, false);
        var attendance = await db.EventParticipants.SingleAsync(x => x.PersonId == adult); attendance.ChangeStatus(ParticipationStatus.Absent, Now.AddMinutes(1)); await db.SaveChangesAsync();
        page = await service.ListAsync(factory.OrganizerPersonId, factory.EventId);
        Assert.Equal(-100m, page.Balances.Single(x => x.PersonId == adult).Balance);
        Assert.Equal(100m, page.Balances.Single(x => x.PersonId == child).Share); Assert.Equal(0m, page.ReconciliationResidual);
        var childAttendance = await db.EventParticipants.SingleAsync(x => x.PersonId == child); childAttendance.ChangeStatus(ParticipationStatus.Absent, Now.AddMinutes(2)); await db.SaveChangesAsync();
        page = await service.ListAsync(factory.OrganizerPersonId, factory.EventId);
        Assert.Equal(100m, page.Unallocated); Assert.Equal(0m, page.ReconciliationResidual); Assert.Equal(2, page.Balances.Count);
    }

    [Fact]
    public async Task RejectedExpensesAndZeroWeightsNeverAllocateBudgetArbitrarily()
    {
        await using var factory = new LocalWebFactory(); using var scope = factory.Services.CreateScope();
        var (a, b) = await ConfirmTwo(scope.ServiceProvider, factory.EventId); var service = Service(scope.ServiceProvider);
        var rejected = await service.DeclareExpenseAsync(factory.OrganizerPersonId, factory.EventId, ExpenseInput(a, 10m));
        await service.ExpenseTransitionAsync(factory.OrganizerPersonId, factory.EventId, rejected, 0, ExpenseStatus.Rejected, true);
        var expense = await service.DeclareExpenseAsync(factory.OrganizerPersonId, factory.EventId, ExpenseInput(a, 42.01m));
        await service.ExpenseTransitionAsync(factory.OrganizerPersonId, factory.EventId, expense, 0, ExpenseStatus.Approved, true);
        foreach (var person in new[] { a, b }) await service.OverrideAsync(factory.OrganizerPersonId, factory.EventId, person, Guid.NewGuid(), 0m, 0, true);
        var page = await service.ListAsync(factory.OrganizerPersonId, factory.EventId);
        Assert.Equal(42.01m, page.ApprovedTotal); Assert.Equal(42.01m, page.Unallocated); Assert.All(page.Balances, x => Assert.Equal(0m, x.Share));
        Assert.Equal(0m, page.ReconciliationResidual);
    }

    [Fact]
    public async Task AccessCrossEventReferencesExplicitConfirmationAndRevisionProtectWrites()
    {
        await using var factory = new LocalWebFactory(); using var scope = factory.Services.CreateScope();
        var (a, b) = await ConfirmTwo(scope.ServiceProvider, factory.EventId); var service = Service(scope.ServiceProvider);
        await Assert.ThrowsAsync<LocalAccessException>(() => service.ListAsync(Guid.NewGuid(), factory.EventId));
        await Assert.ThrowsAsync<LocalAccessException>(() => service.DeclareExpenseAsync(factory.OrganizerPersonId, Guid.NewGuid(), ExpenseInput(a)));
        await Assert.ThrowsAsync<LocalValidationException>(() => service.DeclareExpenseAsync(factory.OrganizerPersonId, factory.EventId, ExpenseInput(factory.OrganizerPersonId)));
        await Assert.ThrowsAsync<LocalValidationException>(() => service.DeclareExpenseAsync(factory.OrganizerPersonId, factory.EventId, ExpenseInput(a) with { Confirmed = false }));
        await Assert.ThrowsAsync<LocalValidationException>(() => service.DeclarePaymentAsync(factory.OrganizerPersonId, factory.EventId, PaymentInput(a, a)));
        await Assert.ThrowsAsync<LocalValidationException>(() => service.DeclarePaymentAsync(factory.OrganizerPersonId, factory.EventId, PaymentInput(a, Guid.NewGuid())));
        await Assert.ThrowsAsync<LocalValidationException>(() => service.ConfigureAsync(factory.OrganizerPersonId, factory.EventId, 1m, 0, false));
        await Assert.ThrowsAsync<LocalValidationException>(() => service.OverrideAsync(factory.OrganizerPersonId, factory.EventId, b, Guid.NewGuid(), 1m, 0, false));
        await service.ConfigureAsync(factory.OrganizerPersonId, factory.EventId, 1m, 0, true);
        await Assert.ThrowsAsync<LocalConflictException>(() => service.ConfigureAsync(factory.OrganizerPersonId, factory.EventId, 0m, 0, true));
        var weightId = Guid.NewGuid(); await service.OverrideAsync(factory.OrganizerPersonId, factory.EventId, a, weightId, 0m, 0, true);
        await Assert.ThrowsAsync<LocalConflictException>(() => service.OverrideAsync(factory.OrganizerPersonId, factory.EventId, a, Guid.NewGuid(), 1m, 0, true));
        var db = scope.ServiceProvider.GetRequiredService<TRobotDbContext>();
        var user = await db.Users.SingleAsync(x => x.PersonId == factory.OrganizerPersonId); user.IsEnabled = false; await db.SaveChangesAsync();
        await Assert.ThrowsAsync<LocalAccessException>(() => service.DeclareExpenseAsync(factory.OrganizerPersonId, factory.EventId, ExpenseInput(a)));
        await Assert.ThrowsAsync<LocalAccessException>(() => service.ListAsync(factory.OrganizerPersonId, factory.EventId));
    }

    [Fact]
    public async Task FailedAuditRollsBackBothLedgerAndSettings()
    {
        var interceptor = new FailAudit(); await using var factory = new LocalWebFactory { SaveInterceptor = interceptor };
        using var scope = factory.Services.CreateScope(); var (a, _) = await ConfirmTwo(scope.ServiceProvider, factory.EventId); var service = Service(scope.ServiceProvider);
        interceptor.Armed = true;
        await Assert.ThrowsAsync<LocalValidationException>(() => service.DeclareExpenseAsync(factory.OrganizerPersonId, factory.EventId, ExpenseInput(a)));
        await Assert.ThrowsAsync<LocalValidationException>(() => service.ConfigureAsync(factory.OrganizerPersonId, factory.EventId, 1m, 0, true));
        interceptor.Armed = false;
        var page = await service.ListAsync(factory.OrganizerPersonId, factory.EventId); Assert.Empty(page.Expenses); Assert.Equal(0.5m, page.Settings.ChildWeight);
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<TRobotDbContext>().Set<LocalAudit>().ToListAsync());
    }

    [Fact]
    public async Task HttpDeclarationApprovalAndReimbursementReachZeroBalancesWithCsrfAndEncodedDescription()
    {
        await using var factory = new LocalWebFactory();
        Guid first, second;
        using (var scope = factory.Services.CreateScope()) (first, second) = await ConfirmTwo(scope.ServiceProvider, factory.EventId);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/Accounting?eventId=" + factory.EventId)).StatusCode);
        var login = Token(await client.GetStringAsync("/Access/Login"));
        Assert.Equal(HttpStatusCode.Redirect, (await client.PostAsync("/Access/Login", Form(("UserName", LocalWebFactory.UserName),
            ("Password", LocalWebFactory.Password), ("__RequestVerificationToken", login)))).StatusCode);
        var page = await client.GetStringAsync("/Accounting?eventId=" + factory.EventId); var token = Token(page);
        Assert.Contains("Saldo positivo: da versare; saldo negativo: credito", page);
        Assert.DoesNotContain("<option value=\"" + factory.OrganizerPersonId, page);
        var expenseOperation = Operation(page, "DeclareExpense");
        var expenseData = new[] { ("EventId", factory.EventId.ToString()), ("OperationId", expenseOperation.ToString()),
            ("AdvancedByPersonId", first.ToString()), ("Description", "<script>anticipo()</script>"), ("Amount", "100,00") };
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/Accounting/DeclareExpense", Form(expenseData))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/Accounting/DeclareExpense", Form(expenseData.Append(("__RequestVerificationToken", token)).ToArray()))).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await client.PostAsync("/Accounting/DeclareExpense", Form(expenseData.Concat(new[] { ("Confirmed", "true"), ("__RequestVerificationToken", token) }).ToArray()))).StatusCode);
        var pending = await client.GetStringAsync("/Accounting?eventId=" + factory.EventId);
        Assert.Contains("&lt;script&gt;anticipo()&lt;/script&gt;", pending); Assert.DoesNotContain("<script>anticipo()</script>", pending);
        Assert.Contains("id=\"approved-total\">0.00", pending);
        var transition = new[] { ("EventId", factory.EventId.ToString()), ("Id", expenseOperation.ToString()), ("Revision", "0"), ("Target", "Approved"), ("__RequestVerificationToken", token) };
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/Accounting/ExpenseTransition", Form(transition))).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await client.PostAsync("/Accounting/ExpenseTransition", Form(transition.Append(("Confirmed", "true")).ToArray()))).StatusCode);
        // Replaying the declaration after approval does not create another expense or undo approval.
        Assert.Equal(HttpStatusCode.Redirect, (await client.PostAsync("/Accounting/DeclareExpense", Form(expenseData.Append(("__RequestVerificationToken", token)).ToArray()))).StatusCode);
        var approved = await client.GetStringAsync("/Accounting?eventId=" + factory.EventId);
        Assert.Contains("id=\"approved-total\">100.00", approved);
        var paymentOperation = Operation(approved, "DeclarePayment");
        Assert.Equal(HttpStatusCode.Redirect, (await client.PostAsync("/Accounting/DeclarePayment", Form(("EventId", factory.EventId.ToString()),
            ("OperationId", paymentOperation.ToString()), ("FromPersonId", second.ToString()), ("ToPersonId", first.ToString()), ("Amount", "50.00"),
            ("Confirmed", "true"), ("__RequestVerificationToken", token)))).StatusCode);
        var declared = await client.GetStringAsync("/Accounting?eventId=" + factory.EventId); Assert.Contains("class=\"accounting-balance\">50.00", declared);
        var payTransition = new[] { ("EventId", factory.EventId.ToString()), ("Id", paymentOperation.ToString()), ("Revision", "0"), ("Target", "Confirmed"), ("__RequestVerificationToken", token) };
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/Accounting/PaymentTransition", Form(payTransition))).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await client.PostAsync("/Accounting/PaymentTransition", Form(payTransition.Append(("Confirmed", "true")).ToArray()))).StatusCode);
        var settled = await client.GetStringAsync("/Accounting?eventId=" + factory.EventId);
        Assert.Equal(2, Regex.Matches(settled, "class=\"accounting-balance\">0.00").Count);
        Assert.Contains("id=\"reconciliation-residual\">0.00", settled);
        using var check = factory.Services.CreateScope(); var db = check.ServiceProvider.GetRequiredService<TRobotDbContext>();
        Assert.Equal(ExpenseStatus.Approved, (await db.Set<Expense>().SingleAsync()).Status);
        Assert.Equal(PaymentStatus.Confirmed, (await db.Set<Payment>().SingleAsync()).Status);
        Assert.Equal(4, await db.Set<LocalAudit>().CountAsync()); Assert.Empty(await db.Set<OutboundMessage>().ToListAsync());
    }

    [Fact]
    public async Task HttpWeightsAcceptDecimalCommaAndRequireRevisionWhileInvalidAmountsAreRejected()
    {
        await using var factory = new LocalWebFactory(); Guid first;
        using (var scope = factory.Services.CreateScope()) (first, _) = await ConfirmTwo(scope.ServiceProvider, factory.EventId);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var login = Token(await client.GetStringAsync("/Access/Login"));
        await client.PostAsync("/Access/Login", Form(("UserName", LocalWebFactory.UserName), ("Password", LocalWebFactory.Password), ("__RequestVerificationToken", login)));
        var token = Token(await client.GetStringAsync("/Accounting?eventId=" + factory.EventId));
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/Accounting/Configure", Form(("EventId", factory.EventId.ToString()),
            ("ChildWeight", "0,25"), ("Confirmed", "true"), ("__RequestVerificationToken", token)))).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await client.PostAsync("/Accounting/Configure", Form(("EventId", factory.EventId.ToString()),
            ("ChildWeight", "0,25"), ("Revision", "0"), ("Confirmed", "true"), ("__RequestVerificationToken", token)))).StatusCode);
        foreach (var amount in new[] { "0", "1000000.01", "0.001", "1e2", "1,000.00" })
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/Accounting/DeclareExpense", Form(("EventId", factory.EventId.ToString()),
                ("OperationId", Guid.NewGuid().ToString()), ("AdvancedByPersonId", first.ToString()), ("Description", "Spesa"), ("Amount", amount),
                ("Confirmed", "true"), ("__RequestVerificationToken", token)))).StatusCode);
        using var check = factory.Services.CreateScope(); var db = check.ServiceProvider.GetRequiredService<TRobotDbContext>();
        Assert.Equal(0.25m, (await db.Set<AccountingSettings>().SingleAsync()).ChildWeight); Assert.Empty(await db.Set<Expense>().ToListAsync());
    }

    private static Guid Operation(string html, string action)
    {
        var form = Regex.Match(html, "<form\\b[^>]*action=\"/Accounting/" + action + "\"[\\s\\S]*?</form>"); Assert.True(form.Success);
        var input = Regex.Match(form.Value, "name=\"OperationId\"[^>]*value=\"([^\"]+)\""); Assert.True(input.Success);
        return Guid.Parse(WebUtility.HtmlDecode(input.Groups[1].Value));
    }

    private sealed class FailAudit : SaveChangesInterceptor
    {
        public bool Armed { get; set; }
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data, InterceptionResult<int> result, CancellationToken ct = default)
        {
            if (Armed && data.Context!.ChangeTracker.Entries<LocalAudit>().Any(x => x.State == EntityState.Added)) throw new InvalidOperationException("Audit simulato fallito.");
            return ValueTask.FromResult(result);
        }
    }
    private static string Token(string html)
    {
        var match = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\""); Assert.True(match.Success);
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }
    private static FormUrlEncodedContent Form(params (string Key, string Value)[] pairs) => new(pairs.Select(x => new KeyValuePair<string, string>(x.Key, x.Value)));
}
