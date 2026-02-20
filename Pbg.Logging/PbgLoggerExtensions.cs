using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Configuration;
using Pbg.Logging.Model;
using System.Threading.Channels;

namespace Pbg.Logging;

public static class PbgLoggerExtensions
{
    public static ILoggingBuilder AddPbgLogger(this ILoggingBuilder builder, Action<PbgLoggerOptions> configure)
    {
        var options = new PbgLoggerOptions();
        configure(options);
        options.Validate();

        var channel = Channel.CreateBounded<PbgLogEntry>(new BoundedChannelOptions(10000)
        {
            FullMode = BoundedChannelFullMode.DropNewest
        });

        builder.Services.AddSingleton(options);
        builder.Services.AddSingleton(channel);

        builder.Services.AddHostedService<PbgLogProcessor>();

        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<ILoggerProvider, PbgLoggerProvider>());

        LoggerProviderOptions.RegisterProviderOptions<PbgLoggerOptions, PbgLoggerProvider>(builder.Services);

        return builder;
    }

    public static IApplicationBuilder UsePbgLogging(this IApplicationBuilder app)
    {
        return app.UseMiddleware<PbgLoggingMiddleware>();
    }
}