namespace Daeron;

public sealed class Config
{
    public string? DeviceId { get; set; }
    public bool AutoReconnect { get; set; } = true;
    public bool StartWithWindows { get; set; }
}
