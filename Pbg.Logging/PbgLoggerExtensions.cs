using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Pbg.Logging;

public static class PbgLoggerExtensions
{
    public static ILoggingBuilder AddPbgLogger(this ILoggingBuilder builder, Action<PbgLoggerOptions> configure)
    {
        var options = new PbgLoggerOptions();
        configure(options);

        builder.Services.AddSingleton(options);
        return builder;
    }
}