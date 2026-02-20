using Microsoft.Extensions.Logging;
using Pbg.Logging.Model;
using System.Threading.Channels;

namespace Pbg.Logging;

[ProviderAlias("Pbg")]
internal class PbgLoggerProvider : ILoggerProvider, ISupportExternalScope
{
    private readonly ChannelWriter<PbgLogEntry> _writer;
    private IExternalScopeProvider? _scopeProvider;
    private readonly PbgLoggerOptions _options;

    public PbgLoggerProvider(Channel<PbgLogEntry> channel, PbgLoggerOptions options)
    {
        _writer = channel.Writer;
        _options = options;
    }

    public ILogger CreateLogger(string categoryName)
    {
        return new PbgLogger(_writer, _scopeProvider, _options, categoryName);
    }

    public void SetScopeProvider(IExternalScopeProvider scopeProvider)
    {
        _scopeProvider = scopeProvider;
    }

    public void Dispose() { }
}