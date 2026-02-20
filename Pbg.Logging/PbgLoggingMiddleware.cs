using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Security.Claims;

namespace Pbg.Logging;

public class PbgLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly PbgLoggerOptions _options;

    public PbgLoggingMiddleware(RequestDelegate next, PbgLoggerOptions options)
    {
        _next = next;
        _options = options;
    }

    public async Task InvokeAsync(HttpContext context, ILogger<PbgLoggingMiddleware> logger)
    {
        var sw = Stopwatch.StartNew();

        var userId = context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value
                     ?? context.User?.FindFirst("sub")?.Value
                     ?? string.Empty;

        var traceId = context.TraceIdentifier;
        string requestBody = string.Empty;

        if (_options.IncludeRequestBody)
        {
            context.Request.EnableBuffering();
            requestBody = await new StreamReader(context.Request.Body).ReadToEndAsync();
            context.Request.Body.Position = 0;
            requestBody = TrimToMaxLength(requestBody);
        }

        var originalBodyStream = context.Response.Body;
        using var responseBodyMemoryStream = new MemoryStream();
        context.Response.Body = responseBodyMemoryStream;

        var initialScope = new Dictionary<string, object>
        {
            ["TraceId"] = traceId,
            ["RequestId"] = context.TraceIdentifier,
            ["Method"] = context.Request.Method,
            ["Path"] = context.Request.Path
        };

        if (_options.IncludeUserId)
        {
            initialScope["UserId"] = userId;
        }

        if (_options.IncludeRequestHeaders && context.Request.Headers.Count > 0)
        {
            initialScope["RequestHeaders"] = context.Request.Headers.ToDictionary(h => h.Key, h => h.Value.ToString());
        }

        using (logger.BeginScope(initialScope))
        {
            try
            {
                await _next(context);

                responseBodyMemoryStream.Position = 0;
                var responseBody = await new StreamReader(responseBodyMemoryStream).ReadToEndAsync();
                responseBodyMemoryStream.Position = 0;

                sw.Stop();

                var finalScope = new Dictionary<string, object>
                {
                    ["StatusCode"] = context.Response.StatusCode,
                    ["Elapsed"] = sw.Elapsed.TotalMilliseconds
                };

                if (_options.IncludeRequestBody)
                {
                    finalScope["RequestBody"] = requestBody;
                }

                if (_options.IncludeResponseBody)
                {
                    finalScope["ResponseBody"] = TrimToMaxLength(responseBody);
                }

                if (_options.IncludeResponseHeaders && context.Response.Headers.Count > 0)
                {
                    finalScope["ResponseHeaders"] = context.Response.Headers.ToDictionary(h => h.Key, h => h.Value.ToString());
                }

                using (logger.BeginScope(finalScope))
                {
                    logger.LogInformation("HTTP Transaction Completed");
                }

                await responseBodyMemoryStream.CopyToAsync(originalBodyStream);
            }
            finally
            {
                context.Response.Body = originalBodyStream;
            }
        }
    }

    private string TrimToMaxLength(string value)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= _options.MaxBodyLength)
        {
            return value;
        }

        return value[.._options.MaxBodyLength];
    }
}
