using System.Text.Json.Serialization;

namespace HomelabDashboard.Models;

internal record ProxmoxEnvelope<T>([property: JsonPropertyName("data")] T Data);

internal record ProxmoxNodeRaw(
    [property: JsonPropertyName("node")] string Node,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("cpu")] double? Cpu,
    [property: JsonPropertyName("mem")] long? Mem,
    [property: JsonPropertyName("maxmem")] long? MaxMem,
    [property: JsonPropertyName("uptime")] long? Uptime
);

internal record ProxmoxGuestRaw(
    [property: JsonPropertyName("vmid")] int VmId,
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("cpu")] double? Cpu,
    [property: JsonPropertyName("mem")] long? Mem,
    [property: JsonPropertyName("maxmem")] long? MaxMem,
    [property: JsonPropertyName("uptime")] long? Uptime
);
