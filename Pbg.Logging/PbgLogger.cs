using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Security.Claims;
using System.Threading.Channels;

namespace Pbg.Logging;

internal class PbgLogger : ILogger
{
    private readonly string _categoryName;
    private readonly ChannelWriter<PbgLogEntry> _writer;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public PbgLogger(string categoryName, ChannelWriter<PbgLogEntry> writer, IHttpContextAccessor httpContextAccessor)
    {
        _categoryName = categoryName;
        _writer = writer;
        _httpContextAccessor = httpContextAccessor;
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel)) return;

        var context = _httpContextAccessor.HttpContext;

        var entry = new PbgLogEntry
        {
            Timestamp = DateTime.UtcNow,
            LogLevel = logLevel.ToString(),
            Message = formatter(state, exception),
            Exception = exception?.ToString(),

            TraceId = Activity.Current?.TraceId.ToHexString()
                      ?? context?.TraceIdentifier,

            UserId = context?.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value
                     ?? context?.User?.FindFirst("sub")?.Value
        };

        _writer.TryWrite(entry);
    }
}