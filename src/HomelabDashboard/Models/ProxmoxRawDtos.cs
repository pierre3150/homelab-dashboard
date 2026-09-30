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
    [property: JsonPropertyName("disk")] long? Disk,
    [property: JsonPropertyName("maxdisk")] long? MaxDisk,
    [property: JsonPropertyName("uptime")] long? Uptime
);

/// <summary>Response of /nodes/{node}/status - includes the node's own root filesystem usage.</summary>
internal record ProxmoxNodeStatusRaw(
    [property: JsonPropertyName("rootfs")] ProxmoxRootFsRaw? RootFs
);

internal record ProxmoxRootFsRaw(
    [property: JsonPropertyName("used")] long? Used,
    [property: JsonPropertyName("total")] long? Total,
    [property: JsonPropertyName("avail")] long? Avail
);

/// <summary>One entry from /nodes/{node}/disks/list - a physical disk and its SMART health.</summary>
internal record ProxmoxDiskRaw(
    [property: JsonPropertyName("devpath")] string DevPath,
    [property: JsonPropertyName("model")] string? Model,
    [property: JsonPropertyName("health")] string? Health,
    [property: JsonPropertyName("wearout")] object? Wearout, // "N/A" for HDDs, or an int percent for SSDs
    [property: JsonPropertyName("size")] long? Size
);
