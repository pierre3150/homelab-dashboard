using System.Net.WebSockets;
using Renci.SshNet;

namespace HomelabDashboard.Services;

public interface ISshConsoleRelayService
{
    Task RelayAsync(WebSocket clientSocket, int vmid, CancellationToken ct);
}

/// <summary>
/// Relays a browser WebSocket to an interactive SSH shell on the target CT.
/// The SSH private key never leaves the server - the browser only ever talks
/// to our own authenticated WebSocket endpoint.
/// </summary>
public class SshConsoleRelayService : ISshConsoleRelayService
{
    private readonly ISshHostMapService _hostMap;
    private readonly IConfiguration _configuration;
    private readonly ILogger<SshConsoleRelayService> _logger;

    public SshConsoleRelayService(
        ISshHostMapService hostMap, IConfiguration configuration, ILogger<SshConsoleRelayService> logger)
    {
        _hostMap = hostMap;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task RelayAsync(WebSocket clientSocket, int vmid, CancellationToken ct)
    {
        var ip = _hostMap.ResolveIp(vmid);
        if (ip is null)
        {
            _logger.LogWarning("SSH console: no host mapping configured for vmid {Vmid}", vmid);
            await SafeClose(clientSocket, $"Aucune IP configuree pour le CT {vmid} (SSH_HOSTS_MAP).");
            return;
        }

        var username = _configuration["Ssh:Username"] ?? "dashboard";
        var keyPath = _configuration["Ssh:PrivateKeyPath"] ?? "/data/ssh/dashboard_console_key";

        SshClient client;
        ShellStream shell;
        try
        {
            using var keyFile = new PrivateKeyFile(keyPath);
            var connectionInfo = new ConnectionInfo(ip, 22, username,
                new PrivateKeyAuthenticationMethod(username, keyFile));

            client = new SshClient(connectionInfo);
            client.Connect();
            _logger.LogInformation("SSH console: connected to {Ip} (vmid {Vmid}) as {Username}", ip, vmid, username);

            shell = client.CreateShellStream("xterm-256color", 80, 24, 800, 600, 4096);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SSH console: failed to connect to {Ip} (vmid {Vmid})", ip, vmid);
            await SafeClose(clientSocket, $"Connexion SSH echouee: {ex.GetType().Name}: {ex.Message}");
            return;
        }

        using (client)
        using (shell)
        {
            var sshToClient = PumpSshToClientAsync(shell, clientSocket, ct);
            var clientToSsh = PumpClientToSshAsync(clientSocket, shell, ct);

            await Task.WhenAny(sshToClient, clientToSsh);

            if (clientSocket.State == WebSocketState.Open)
                await clientSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "closing", CancellationToken.None);
        }
    }

    private async Task PumpSshToClientAsync(ShellStream shell, WebSocket clientSocket, CancellationToken ct)
    {
        var buffer = new byte[8192];
        try
        {
            while (clientSocket.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                var read = await shell.ReadAsync(buffer, ct);
                if (read == 0)
                {
                    await Task.Delay(50, ct); // ShellStream returns 0 when idle, not EOF
                    continue;
                }

                await clientSocket.SendAsync(
                    new ArraySegment<byte>(buffer, 0, read), WebSocketMessageType.Text, true, ct);
            }
        }
        catch (OperationCanceledException) { /* normal on shutdown */ }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SSH console [ssh->client]: relay ended unexpectedly");
        }
    }

    private async Task PumpClientToSshAsync(WebSocket clientSocket, ShellStream shell, CancellationToken ct)
    {
        var buffer = new byte[8192];
        try
        {
            while (clientSocket.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                var result = await clientSocket.ReceiveAsync(buffer, ct);
                if (result.MessageType == WebSocketMessageType.Close) break;

                await shell.WriteAsync(buffer.AsMemory(0, result.Count), ct);
                await shell.FlushAsync(ct);
            }
        }
        catch (OperationCanceledException) { /* normal on shutdown */ }
        catch (WebSocketException ex)
        {
            _logger.LogWarning(ex, "SSH console [client->ssh]: socket closed unexpectedly");
        }
    }

    private static async Task SafeClose(WebSocket socket, string reason)
    {
        try
        {
            if (socket.State == WebSocketState.Open)
            {
                var trimmed = reason.Length > 120 ? reason[..120] : reason;
                await socket.CloseAsync(WebSocketCloseStatus.InternalServerError, trimmed, CancellationToken.None);
            }
        }
        catch { /* best effort */ }
    }
}
