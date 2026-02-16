namespace Pbg.Logging.Model;

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
    public string? Method { get; set; }
    public string? Path { get; set; }
    public int? StatusCode { get; set; }
    public string? RequestBody { get; set; }
    public string? ResponseBody { get; set; }
    public Dictionary<string, string>? RequestHeaders { get; set; }
    public double? ElapsedMilliseconds { get; set; }
}