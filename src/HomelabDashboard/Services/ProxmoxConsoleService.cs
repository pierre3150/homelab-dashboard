using System.Net.Http.Headers;
using System.Text.Json;

namespace HomelabDashboard.Services;

public record TermProxyTicket(int Port, string Ticket);

public interface IProxmoxConsoleService
{
    /// <summary>Requests a term proxy session for an LXC container's console.
    /// Returns the port + ticket needed to open the actual terminal WebSocket.</summary>
    Task<TermProxyTicket> OpenLxcConsoleAsync(string node, int vmid, CancellationToken ct = default);
}

public class ProxmoxConsoleService : IProxmoxConsoleService
{
    private readonly HttpClient _http;

    public ProxmoxConsoleService(HttpClient http, IConfiguration configuration)
    {
        _http = http;

        var host = configuration["Proxmox:Host"]
            ?? throw new InvalidOperationException("Proxmox:Host is not configured.");
        var tokenId = configuration["Proxmox:TokenId"]
            ?? throw new InvalidOperationException("Proxmox:TokenId is not configured.");
        var tokenSecret = configuration["Proxmox:TokenSecret"]
            ?? throw new InvalidOperationException("Proxmox:TokenSecret is not configured.");

        _http.BaseAddress = new Uri($"https://{host}:8006/api2/json/");
        _http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("PVEAPIToken", $"{tokenId}={tokenSecret}");
    }

    public async Task<TermProxyTicket> OpenLxcConsoleAsync(string node, int vmid, CancellationToken ct = default)
    {
        var response = await _http.PostAsync($"nodes/{node}/lxc/{vmid}/termproxy", content: null, ct);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(body);
        var data = doc.RootElement.GetProperty("data");

        var portElement = data.GetProperty("port");
        var port = portElement.ValueKind == JsonValueKind.Number
            ? portElement.GetInt32()
            : int.Parse(portElement.GetString()!);

        return new TermProxyTicket(
            Port: port,
            Ticket: data.GetProperty("ticket").GetString()!
        );
    }
}
