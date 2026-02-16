using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Security.Claims;

namespace Pbg.Logging;

public class PbgLoggingMiddleware
{
    private readonly RequestDelegate _next;

    public PbgLoggingMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, ILogger<PbgLoggingMiddleware> logger)
    {
        var sw = Stopwatch.StartNew();

        var userId = context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value
                     ?? context.User?.FindFirst("sub")?.Value
                     ?? string.Empty;

        var traceId = context.TraceIdentifier;

        context.Request.EnableBuffering();
        var requestBody = await new StreamReader(context.Request.Body).ReadToEndAsync();
        context.Request.Body.Position = 0;

        var originalBodyStream = context.Response.Body;
        using var responseBodyMemoryStream = new MemoryStream();
        context.Response.Body = responseBodyMemoryStream;

        var initialScope = new Dictionary<string, object>
        {
            ["UserId"] = userId,
            ["TraceId"] = traceId,
            ["Method"] = context.Request.Method,
            ["Path"] = context.Request.Path
        };

        if (context.Request.Headers.Count > 0)
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
                    ["RequestBody"] = requestBody,
                    ["ResponseBody"] = responseBody,
                    ["Elapsed"] = sw.Elapsed.TotalMilliseconds
                };

                if (context.Response.Headers.Count > 0)
                {
                    finalScope["ResponseHeaders"] = context.Response.Headers.ToDictionary(h => h.Key, h => h.Value.ToString());
                }

                using (logger.BeginScope(finalScope))
                {
                    logger.LogInformation("HTTP Transaction Completed");
                }

                await responseBodyMemoryStream.CopyToAsync(originalBodyStream);
            }
            catch (Exception)
            {
                throw;
            }
            finally
            {
                context.Response.Body = originalBodyStream;
            }
        }
    }
}