using Pbg.Logging.Model;

namespace Pbg.Logging;

public class PbgLoggerOptions
{
    public Guid LicenseKey { get; set; }
    public PbgEnvironment Environment { get; set; }
    public string ProjectName { get; set; } = "UnknownProject";
    public string EndpointUrl { get; set; } = string.Empty;
    public int BatchSize { get; set; } = 20;
    public TimeSpan FlushInterval { get; set; } = TimeSpan.FromSeconds(3);
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(30);
    public int MaxRetries { get; set; } = 3;
    public TimeSpan RetryBaseDelay { get; set; } = TimeSpan.FromSeconds(2);
    public HashSet<string> ExcludedBodyPathPrefixes { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        "/health",
        "/healthz",
        "/metrics",
        "/swagger"
    };

    public HashSet<string> ExcludedExtensions { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ".css", ".js", ".map",
        ".png", ".jpg", ".jpeg", ".gif", ".svg", ".ico", ".webp", ".bmp",
        ".woff", ".woff2", ".ttf", ".eot", ".otf",
        ".mp3", ".mp4", ".wav", ".avi", ".webm",
        ".pdf", ".zip", ".gz", ".br"
    };

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

        if (RequestTimeout <= TimeSpan.Zero)
            throw new ArgumentException("Pbg.Logging: RequestTimeout must be greater than zero.");

        if (MaxRetries <= 0)
            throw new ArgumentException("Pbg.Logging: MaxRetries must be greater than 0.");

        if (RetryBaseDelay <= TimeSpan.Zero)
            throw new ArgumentException("Pbg.Logging: RetryBaseDelay must be greater than zero.");
    }

    public bool IsBodyCaptureExcludedPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || ExcludedBodyPathPrefixes.Count == 0)
        {
            return false;
        }

        foreach (var prefix in ExcludedBodyPathPrefixes)
        {
            if (string.IsNullOrWhiteSpace(prefix))
            {
                continue;
            }

            if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
