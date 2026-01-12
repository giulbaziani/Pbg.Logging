namespace Pbg.Logging;

public class PbgLogEntry
{
    public string? TraceId { get; set; }
    public string? Environment { get; set; }
    public string? UserId { get; set; }
    public DateTime Timestamp { get; set; }
    public string? LogLevel { get; set; }
    public string? Message { get; set; }
    public string? ProjectName { get; set; }
    public string? MachineName { get; set; }
    public string? IpAddress { get; set; }
    public string? Exception { get; set; }
}