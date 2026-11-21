using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using TRobot.Application.Local;
using TRobot.Application.WhatsApp;

namespace TRobot.Infrastructure.WhatsApp;

public sealed class WhatsAppGatewayClient(HttpClient client, WhatsAppGatewayOptions options) : IWhatsAppGateway
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private sealed record GroupsResponse(IReadOnlyList<GatewayGroup> Groups);
    private sealed record MembersResponse(IReadOnlyList<GatewayMember> Members);
    public Task<GatewayHealth> HealthAsync(CancellationToken ct) => Request<GatewayHealth>(HttpMethod.Get, "health", null, ct);
    public Task<GatewayHealth> ConnectAsync(CancellationToken ct) => Request<GatewayHealth>(HttpMethod.Post, "connect", new { }, ct);
    public Task<GatewayHealth> DisconnectAsync(CancellationToken ct) => Request<GatewayHealth>(HttpMethod.Post, "disconnect", new { }, ct);
    public async Task<IReadOnlyList<GatewayGroup>> GroupsAsync(CancellationToken ct) => (await Request<GroupsResponse>(HttpMethod.Get, "groups", null, ct)).Groups;
    public async Task<IReadOnlyList<GatewayMember>> MembersAsync(string groupId, CancellationToken ct) =>
        (await Request<MembersResponse>(HttpMethod.Get, "groups/" + Uri.EscapeDataString(groupId) + "/members", null, ct)).Members;
    public async Task SetGroupAsync(string? groupId, CancellationToken ct)
    { using var response = await Send(HttpMethod.Post, "group", new { groupId }, ct); }
    public Task<GatewaySendResult> SendAsync(Guid operationId, string groupId, string text, CancellationToken ct) =>
        Request<GatewaySendResult>(HttpMethod.Post, "send", new { operationId, groupId, text }, ct);
    private async Task<T> Request<T>(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        using var response = await Send(method, path, body, ct);
        if (response.Content.Headers.ContentLength is > 2_000_000) throw new LocalValidationException("Risposta gateway troppo grande.");
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var bytes = new MemoryStream(); var buffer = new byte[8192]; int read;
        while ((read = await stream.ReadAsync(buffer, ct)) > 0)
        { if (bytes.Length + read > 2_000_000) throw new LocalValidationException("Risposta gateway troppo grande."); bytes.Write(buffer, 0, read); }
        try { return JsonSerializer.Deserialize<T>(bytes.ToArray(), Json) ?? throw new LocalValidationException("Risposta gateway non valida."); }
        catch (JsonException) { throw new LocalValidationException("Risposta gateway non valida."); }
    }
    private async Task<HttpResponseMessage> Send(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        options.RequireEnabled();
        using var request = new HttpRequestMessage(method, new Uri(new Uri(options.BaseUrl), path));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.Token);
        if (body is not null) request.Content = JsonContent.Create(body, options: Json);
        HttpResponseMessage response;
        try { response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct); }
        catch (HttpRequestException) { throw new LocalValidationException("Gateway locale non raggiungibile."); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new LocalValidationException("Gateway locale non risponde entro il tempo previsto."); }
        if (!response.IsSuccessStatusCode)
        {
            var status = (int)response.StatusCode;
            var code = status is >= 400 and < 500 ? await ErrorCodeAsync(response) : null;
            response.Dispose();
            // Only a client error proves nothing was transmitted. 5xx stays ambiguous.
            if (status is >= 400 and < 500) throw new WhatsAppGatewayRejectedException(code);
            throw new LocalValidationException("Gateway non disponibile o richiesta fallita.");
        }
        return response;
    }
    private static async Task<string?> ErrorCodeAsync(HttpResponseMessage response)
    {
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync();
            var buffer = new byte[512]; var read = await stream.ReadAsync(buffer);
            using var document = JsonDocument.Parse(buffer.AsMemory(0, read));
            var value = document.RootElement.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.String
                ? error.GetString() : null;
            // Only a short technical token is kept: never propagate free-form text from the gateway.
            return value is { Length: > 0 and <= 40 } && value.All(x => x is >= 'a' and <= 'z' or '_') ? value : null;
        }
        catch { return null; }
    }
}
