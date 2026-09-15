using System.Net.WebSockets;

namespace HomelabDashboard.Services;

public interface IConsoleRelayService
{
    Task RelayAsync(WebSocket clientSocket, string node, int vmid, CancellationToken ct);
}

/// <summary>
/// Relays a browser WebSocket connection to Proxmox's LXC console (termproxy +
/// vncwebsocket) - the browser only ever talks to us, authenticated by our own
/// session cookie; the Proxmox ticket/token never reaches the client.
/// </summary>
public class ConsoleRelayService : IConsoleRelayService
{
    private readonly IProxmoxConsoleService _console;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ConsoleRelayService> _logger;

    public ConsoleRelayService(
        IProxmoxConsoleService console, IConfiguration configuration, ILogger<ConsoleRelayService> logger)
    {
        _console = console;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task RelayAsync(WebSocket clientSocket, string node, int vmid, CancellationToken ct)
    {
        var ticket = await _console.OpenLxcConsoleAsync(node, vmid, ct);
        var host = _configuration["Proxmox:Host"]
            ?? throw new InvalidOperationException("Proxmox:Host is not configured.");
        var tokenId = _configuration["Proxmox:TokenId"];
        var tokenSecret = _configuration["Proxmox:TokenSecret"];

        using var proxmoxSocket = new ClientWebSocket();
        // Proxmox homelab instances run with a self-signed cert - same documented
        // trade-off as ProxmoxClient's HttpClientHandler, LAN-internal call only.
        proxmoxSocket.Options.RemoteCertificateValidationCallback = (_, _, _, _) => true;
        proxmoxSocket.Options.SetRequestHeader("Authorization", $"PVEAPIToken {tokenId}={tokenSecret}");

        var wsUri = new Uri(
            $"wss://{host}:8006/api2/json/nodes/{node}/lxc/{vmid}/vncwebsocket" +
            $"?port={ticket.Port}&vncticket={Uri.EscapeDataString(ticket.Ticket)}");

        await proxmoxSocket.ConnectAsync(wsUri, ct);

        // Proxmox's termproxy doesn't just validate the ticket from the query
        // string - it also expects the SAME ticket sent as the very first
        // WebSocket message, and times out ("failed reading ticket") if that
        // never arrives. This isn't documented anywhere obvious; found by
        // reading the actual task log Proxmox produced on a failed attempt.
        var ticketBytes = System.Text.Encoding.UTF8.GetBytes(ticket.Ticket);
        await proxmoxSocket.SendAsync(ticketBytes, WebSocketMessageType.Text, endOfMessage: true, ct);

        var toProxmox = PumpAsync(clientSocket, proxmoxSocket, ct);
        var toClient = PumpAsync(proxmoxSocket, clientSocket, ct);

        await Task.WhenAny(toProxmox, toClient);

        if (clientSocket.State == WebSocketState.Open)
            await clientSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "closing", CancellationToken.None);
        if (proxmoxSocket.State == WebSocketState.Open)
            await proxmoxSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "closing", CancellationToken.None);
    }

    /// <summary>Raw byte relay, one direction. No framing/resize protocol on top
    /// (yet) - keystrokes and output pass through unmodified.</summary>
    private async Task PumpAsync(WebSocket from, WebSocket to, CancellationToken ct)
    {
        var buffer = new byte[8192];
        try
        {
            while (from.State == WebSocketState.Open && to.State == WebSocketState.Open)
            {
                var result = await from.ReceiveAsync(buffer, ct);
                if (result.MessageType == WebSocketMessageType.Close) break;

                await to.SendAsync(
                    new ArraySegment<byte>(buffer, 0, result.Count),
                    result.MessageType, result.EndOfMessage, ct);
            }
        }
        catch (OperationCanceledException) { /* normal on shutdown */ }
        catch (WebSocketException ex)
        {
            _logger.LogWarning(ex, "Console relay socket closed unexpectedly");
        }
    }
}
