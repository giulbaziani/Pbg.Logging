using Pbg.Logging.Model;

namespace Pbg.Logging;

public class PbgLoggerOptions
{
    public Guid LicenseKey { get; set; }
    public PbgEnvironment Environment { get; set; }
    public string ProjectName { get; set; } = "UnknownProject";
    public string EndpointUrl { get; set; } = string.Empty;
    public int BatchSize { get; set; } = 50;
    public TimeSpan FlushInterval { get; set; } = TimeSpan.FromSeconds(3);
    public bool IncludeUserId { get; set; } = false;
    public bool IncludeRequestHeaders { get; set; } = false;
    public bool IncludeResponseHeaders { get; set; } = false;
    public bool IncludeRequestBody { get; set; } = false;
    public bool IncludeResponseBody { get; set; } = false;
    public int MaxBodyLength { get; set; } = 4096;

    public void Validate()
    {
        if (LicenseKey == Guid.Empty)
            throw new ArgumentException("Pbg.Logging: LicenseKey cannot be empty.");

        if (string.IsNullOrWhiteSpace(EndpointUrl))
            throw new ArgumentException("Pbg.Logging: EndpointUrl cannot be empty.");

        if (!Uri.IsWellFormedUriString(EndpointUrl, UriKind.Absolute))
            throw new ArgumentException("Pbg.Logging: EndpointUrl must be a valid absolute URL.");

        if (!Enum.IsDefined(typeof(PbgEnvironment), Environment))
        {
            throw new ArgumentException($"Pbg.Logging: Environment value '{(int)Environment}' is not a valid PbgEnvironment.");
        }

        if (BatchSize <= 0)
            throw new ArgumentException("Pbg.Logging: BatchSize must be greater than 0.");

        if (FlushInterval <= TimeSpan.Zero)
            throw new ArgumentException("Pbg.Logging: FlushInterval must be greater than zero.");

        if (MaxBodyLength <= 0)
            throw new ArgumentException("Pbg.Logging: MaxBodyLength must be greater than 0.");
    }
}
