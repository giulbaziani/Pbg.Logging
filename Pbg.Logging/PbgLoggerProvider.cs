using Microsoft.Extensions.Logging;
using System.Threading.Channels;

namespace Pbg.Logging;

[ProviderAlias("Pbg")]
internal class PbgLoggerProvider : ILoggerProvider
{
    private readonly ChannelWriter<PbgLogEntry> _writer;

    public PbgLoggerProvider(Channel<PbgLogEntry> channel)
    {
        _writer = channel.Writer;
    }

    public ILogger CreateLogger(string categoryName)
    {
        return new PbgLogger(categoryName, _writer);
    }

    public void Dispose() { }
}