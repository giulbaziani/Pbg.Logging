using Microsoft.Extensions.Logging;
using Pbg.Logging.Model;
using System.Diagnostics;
using System.Threading.Channels;

namespace Pbg.Logging;

internal class PbgLogger : ILogger
{
    private readonly ChannelWriter<PbgLogEntry> _writer;
    private readonly IExternalScopeProvider? _scopeProvider;
    private readonly string _categoryName;

    public PbgLogger(ChannelWriter<PbgLogEntry> writer, IExternalScopeProvider? scopeProvider, string categoryName)
    {
        _writer = writer;
        _scopeProvider = scopeProvider;
        _categoryName = categoryName;
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => _scopeProvider?.Push(state);

    public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!ShouldLog(logLevel)) return;

        var entry = new PbgLogEntry
        {
            Timestamp = DateTime.UtcNow,
            LogLevel = logLevel.ToString(),
            Message = formatter(state, exception),
            Exception = exception?.ToString()
        };

        string? foundTraceId = Activity.Current?.TraceId.ToHexString();

        _scopeProvider?.ForEachScope((scope, currentEntry) =>
        {
            if (scope is IEnumerable<KeyValuePair<string, object>> properties)
            {
                foreach (var prop in properties)
                {
                    switch (prop.Key)
                    {
                        case "UserId": currentEntry.UserId = prop.Value?.ToString(); break;
                        case "TraceId":  if (string.IsNullOrEmpty(foundTraceId)) foundTraceId = prop.Value?.ToString(); break;
                        case "RequestBody": currentEntry.RequestBody = prop.Value?.ToString(); break;
                        case "ResponseBody": currentEntry.ResponseBody = prop.Value?.ToString(); break;
                        case "Method": currentEntry.Method = prop.Value?.ToString();  break;
                        case "Path": currentEntry.Path = prop.Value?.ToString(); break;
                        case "StatusCode": if (prop.Value is int code) currentEntry.StatusCode = code; break;
                        case "Elapsed": if (prop.Value is double ms) currentEntry.ElapsedMilliseconds = ms; break;
                        case "RequestHeaders": if (prop.Value is Dictionary<string, string> reqHeaders) currentEntry.RequestHeaders = reqHeaders; break;
                    }
                }
            }
        }, entry);

        entry.TraceId = foundTraceId ?? Guid.NewGuid().ToString("N");

        _writer.TryWrite(entry);
    }

    private bool ShouldLog(LogLevel logLevel)
    {
        if (logLevel >= LogLevel.Error) return true;

        if (_categoryName.Contains("Microsoft.Hosting.Lifetime")) return true;

        if (_categoryName.StartsWith("Microsoft") || _categoryName.StartsWith("System"))
        {
            return logLevel >= LogLevel.Warning;
        }

        return true;
    }
}