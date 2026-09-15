using System.Net.WebSockets;
using System.Text;

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
        TermProxyTicket ticket;
        try
        {
            ticket = await _console.OpenLxcConsoleAsync(node, vmid, ct);
            _logger.LogInformation("Console: termproxy opened for {Node}/{Vmid}, port={Port}", node, vmid, ticket.Port);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Console: failed to open termproxy session for {Node}/{Vmid}", node, vmid);
            await SafeCloseWithReason(clientSocket, $"termproxy request failed: {ex.Message}");
            return;
        }

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

        try
        {
            _logger.LogInformation("Console: connecting to Proxmox websocket {Uri}", wsUri.GetLeftPart(UriPartial.Path));
            await proxmoxSocket.ConnectAsync(wsUri, ct);
            _logger.LogInformation("Console: connected to Proxmox websocket, state={State}", proxmoxSocket.State);

            // Proxmox's termproxy doesn't just validate the ticket from the query
            // string - it also expects the SAME ticket sent as the very first
            // WebSocket message, and times out ("failed reading ticket") if that
            // never arrives.
            var ticketBytes = Encoding.UTF8.GetBytes(ticket.Ticket);
            await proxmoxSocket.SendAsync(ticketBytes, WebSocketMessageType.Text, endOfMessage: true, ct);
            _logger.LogInformation("Console: ticket sent as first WS message ({Bytes} bytes)", ticketBytes.Length);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Console: failed to establish/authenticate Proxmox websocket for {Node}/{Vmid}", node, vmid);
            await SafeCloseWithReason(clientSocket, $"proxmox websocket connect failed: {ex.GetType().Name}: {ex.Message}");
            return;
        }

        var toProxmox = PumpAsync(clientSocket, proxmoxSocket, "client->proxmox", ct);
        var toClient = PumpAsync(proxmoxSocket, clientSocket, "proxmox->client", ct);

        await Task.WhenAny(toProxmox, toClient);

        _logger.LogInformation("Console: relay ending for {Node}/{Vmid}, client={ClientState}, proxmox={ProxmoxState}",
            node, vmid, clientSocket.State, proxmoxSocket.State);

        if (clientSocket.State == WebSocketState.Open)
            await clientSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "closing", CancellationToken.None);
        if (proxmoxSocket.State == WebSocketState.Open)
            await proxmoxSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "closing", CancellationToken.None);
    }

    /// <summary>Raw byte relay, one direction. No framing/resize protocol on top
    /// (yet) - keystrokes and output pass through unmodified.</summary>
    private async Task PumpAsync(WebSocket from, WebSocket to, string direction, CancellationToken ct)
    {
        var buffer = new byte[8192];
        try
        {
            while (from.State == WebSocketState.Open && to.State == WebSocketState.Open)
            {
                var result = await from.ReceiveAsync(buffer, ct);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    _logger.LogInformation("Console [{Direction}]: received Close frame", direction);
                    break;
                }

                await to.SendAsync(
                    new ArraySegment<byte>(buffer, 0, result.Count),
                    result.MessageType, result.EndOfMessage, ct);
            }
        }
        catch (OperationCanceledException) { /* normal on shutdown */ }
        catch (WebSocketException ex)
        {
            _logger.LogWarning(ex, "Console [{Direction}]: socket closed unexpectedly ({Message})", direction, ex.Message);
        }
    }

    private static async Task SafeCloseWithReason(WebSocket socket, string reason)
    {
        try
        {
            if (socket.State == WebSocketState.Open)
            {
                // Close reason is capped at 123 bytes by the WebSocket spec.
                var trimmed = reason.Length > 120 ? reason[..120] : reason;
                await socket.CloseAsync(WebSocketCloseStatus.InternalServerError, trimmed, CancellationToken.None);
            }
        }
        catch { /* best effort - the socket may already be unusable */ }
    }
}
