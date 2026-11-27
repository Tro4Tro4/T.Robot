using Microsoft.Extensions.DependencyInjection;
using TRobot.Application.Local;
using TRobot.Application.Messaging;
using TRobot.Application.WhatsApp;
using TRobot.Domain;

namespace TRobot.Tests;

internal sealed class RecordingGateway : IWhatsAppGateway
{
    public const string SyntheticGroup = "synthetic-group@g.us";
    public int Sends { get; private set; }
    public List<Guid> OperationIds { get; } = [];
    public TaskCompletionSource? SendStarted { get; set; }
    public TaskCompletionSource? ReleaseSend { get; set; }
    /// <summary>Custom transport: throw WhatsAppGatewayRejectedException for a definite refusal.</summary>
    public Func<Guid, string, string, Task<GatewaySendResult>>? OnSend { get; set; }
    public string? ActiveGroup { get; set; } = SyntheticGroup;
    public int GroupSets { get; private set; }
    public Func<GatewayHealth>? OnHealth { get; set; }
    public Task<GatewayHealth> HealthAsync(CancellationToken ct) => Task.FromResult(OnHealth?.Invoke()
        ?? new GatewayHealth("connected", null, ActiveGroup is null ? [] : [ActiveGroup], []));
    public Task<GatewayHealth> ConnectAsync(CancellationToken ct) => HealthAsync(ct);
    public Task<GatewayHealth> DisconnectAsync(CancellationToken ct) { ActiveGroup = null; return Task.FromResult(new GatewayHealth("disconnected")); }
    public Task<IReadOnlyList<GatewayGroup>> GroupsAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<GatewayGroup>>([new(SyntheticGroup, "Gruppo sintetico")]);
    public Task<IReadOnlyList<GatewayMember>> MembersAsync(string groupId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<GatewayMember>>([]);
    public Task<GatewayHealth> SetGroupsAsync(IReadOnlyList<string> groupIds, CancellationToken ct)
    { GroupSets++; ActiveGroup = groupIds.FirstOrDefault(); return HealthAsync(ct); }
    public async Task<GatewaySendResult> SendAsync(Guid operationId, string groupId, string text, CancellationToken ct)
    {
        Sends++; OperationIds.Add(operationId);
        SendStarted?.TrySetResult();
        if (ReleaseSend is not null) await ReleaseSend.Task.WaitAsync(ct);
        if (OnSend is not null) return await OnSend(operationId, groupId, text);
        return new GatewaySendResult("sent", "synthetic-outgoing");
    }
}

internal static class MessageTestSupport
{
    public static WhatsAppGatewayOptions Options(bool enabled = true) =>
        new(enabled, "http://127.0.0.1:5240", new string('a', 64));

    public static MessageDispatchService Dispatcher(IServiceProvider provider, TimeProvider clock, RecordingGateway gateway,
        bool enabled = true)
    {
        var store = provider.GetRequiredService<ILocalStore>();
        return new MessageDispatchService(store, gateway, Options(enabled),
            provider.GetRequiredService<IWhatsAppStore>(), new LocalOperations(store, clock), clock);
    }

    public static MessageService Service(IServiceProvider provider, TimeProvider clock, RecordingGateway gateway,
        bool enabled = true) =>
        new(provider.GetRequiredService<ILocalStore>(), new LocalOperations(provider.GetRequiredService<ILocalStore>(), clock),
            clock, Dispatcher(provider, clock, gateway, enabled));

    /// <summary>Registers an active WhatsApp connection for the event with an authorized activator.</summary>
    public static async Task ActivateAsync(IServiceProvider provider, Guid eventId, Guid actorId, DateTime utc,
        string groupId = RecordingGateway.SyntheticGroup)
    {
        var store = provider.GetRequiredService<ILocalStore>();
        var connection = await store.FindAsync<WhatsAppConnection>(eventId, eventId, default);
        if (connection is null) { connection = new WhatsAppConnection(eventId); store.Add(connection); }
        connection.Activate(groupId, actorId, utc);
        connection.UpdateFeatures(WhatsAppFeatureSet.All);
        await store.SaveAsync(default);
    }

    public static async Task<Guid> QueueAsync(MessageService service, Guid actorId, Guid eventId, string body = "Avviso sintetico",
        MessageKind kind = MessageKind.Operational)
    {
        var input = new MessageEdit(Guid.NewGuid(), body, kind, null, 0);
        var id = await service.SaveDraftAsync(actorId, eventId, null, input);
        await service.TransitionAsync(actorId, eventId, id, 0, true, false);
        return id;
    }
}
