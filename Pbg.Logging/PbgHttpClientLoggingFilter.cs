using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;

namespace Pbg.Logging;

internal sealed class PbgHttpClientLoggingFilter : IHttpMessageHandlerBuilderFilter
{
    private readonly IServiceProvider _serviceProvider;
    private readonly PbgLoggerOptions _options;

    public PbgHttpClientLoggingFilter(IServiceProvider serviceProvider, PbgLoggerOptions options)
    {
        _serviceProvider = serviceProvider;
        _options = options;
    }

    public Action<HttpMessageHandlerBuilder> Configure(Action<HttpMessageHandlerBuilder> next)
    {
        return builder =>
        {
            next(builder);

            if (!_options.EnableHttpClientLogging)
            {
                return;
            }

            if (builder.AdditionalHandlers.Any(static handler => handler is PbgHttpClientLoggingHandler))
            {
                return;
            }

            var loggingHandler = _serviceProvider.GetRequiredService<PbgHttpClientLoggingHandler>();
            builder.AdditionalHandlers.Add(loggingHandler);
        };
    }
}
