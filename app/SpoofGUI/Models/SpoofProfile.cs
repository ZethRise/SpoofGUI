namespace SpoofGUI.Models;

public sealed class SpoofProfile
{
    public long Id { get; set; }
    public string Name { get; set; } = "default";
    public string ListenHost { get; set; } = "0.0.0.0";
    public int ListenPort { get; set; } = 40443;
    public string ConnectIp { get; set; } = "";
    public int ConnectPort { get; set; } = 443;
    public string FakeSni { get; set; } = "";
    public bool IsActive { get; set; }
}

public sealed class EngineStatus
{
    public bool Running { get; init; }
    public ulong UptimeMs { get; init; }
    public uint Connections { get; init; }
}

public sealed class V2RayProfile
{
    public long Id { get; set; }
    public string Name { get; set; } = "new config";
    public string Protocol { get; set; } = "vless";
    public string Mode { get; set; } = "Proxy";
    public string Address { get; set; } = "";
    public int Port { get; set; } = 443;
    public string UserId { get; set; } = "";
    public string Security { get; set; } = "";
    public string Transport { get; set; } = "tcp";
    public string ServerName { get; set; } = "";
    public string RawUri { get; set; } = "";
}
