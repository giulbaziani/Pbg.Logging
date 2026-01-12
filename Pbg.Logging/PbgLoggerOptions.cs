namespace Pbg.Logging;

public class PbgLoggerOptions
{
    public Guid LicenseKey { get; set; }
    public PbgEnvironment Environment { get; set; }
    public string ProjectName { get; set; } = "UnknownProject";
    public string EndpointUrl { get; set; } = string.Empty;
    public int BatchSize { get; set; } = 50;
    public TimeSpan FlushInterval { get; set; } = TimeSpan.FromSeconds(3);

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
    }
}