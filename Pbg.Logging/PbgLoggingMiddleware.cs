using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Security.Claims;

namespace Pbg.Logging;

public class PbgLoggingMiddleware(RequestDelegate next, PbgLoggerOptions options)
{
    private readonly RequestDelegate _next = next;
    private readonly PbgLoggerOptions _options = options;

    public async Task InvokeAsync(HttpContext context, ILogger<PbgLoggingMiddleware> logger)
    {
        if (IsStaticFileRequest(context.Request.Path))
        {
            await _next(context);
            return;
        }

        var sw = Stopwatch.StartNew();

        var userId = context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value
                     ?? context.User?.FindFirst("sub")?.Value
                     ?? string.Empty;

        var traceId = context.TraceIdentifier;
        var canCaptureBodyForPath = !_options.IsBodyCaptureExcludedPath(context.Request.Path.Value);

        var requestBody = string.Empty;
        Dictionary<string, string>? requestHeaders = null;

        if (context.Request.Headers.Count > 0)
        {
            requestHeaders = context.Request.Headers.ToDictionary(h => h.Key, h => h.Value.ToString());
        }

        if (canCaptureBodyForPath)
        {
            context.Request.EnableBuffering();
            requestBody = await ReadRequestBodyAsync(context.Request);
            context.Request.Body.Position = 0;
        }

        if (!canCaptureBodyForPath)
        {
            await _next(context);
            sw.Stop();

            var scopeWithoutBody = BuildScope(context, traceId, sw.Elapsed.TotalMilliseconds, userId, requestBody, null, requestHeaders, canCaptureBodyForPath);

            using (logger.BeginScope(scopeWithoutBody))
            {
                logger.LogInformation("HTTP IN {Method} {Path} responded {StatusCode}",
                    context.Request.Method,
                    context.Request.Path,
                    context.Response.StatusCode);
            }

            return;
        }

        var originalBodyStream = context.Response.Body;
        await using var responseBodyMemoryStream = new MemoryStream();
        context.Response.Body = responseBodyMemoryStream;

        try
        {
            await _next(context);

            sw.Stop();

            responseBodyMemoryStream.Position = 0;
            var responseBody = await ReadResponseBodyAsync(responseBodyMemoryStream, context.Response.ContentType);
            responseBodyMemoryStream.Position = 0;

            var scope = BuildScope(context, traceId, sw.Elapsed.TotalMilliseconds, userId, requestBody, responseBody, requestHeaders, canCaptureBodyForPath);

            using (logger.BeginScope(scope))
            {
                logger.LogInformation("HTTP IN {Method} {Path} responded {StatusCode}",
                    context.Request.Method,
                    context.Request.Path,
                    context.Response.StatusCode);
            }

            await responseBodyMemoryStream.CopyToAsync(originalBodyStream);
        }
        finally
        {
            context.Response.Body = originalBodyStream;
        }
    }

    private Dictionary<string, object> BuildScope(
        HttpContext context,
        string traceId,
        double elapsedMs,
        string userId,
        string requestBody,
        string? responseBody,
        Dictionary<string, string>? requestHeaders,
        bool canCaptureBodyForPath)
    {
        var scope = new Dictionary<string, object>
        {
            ["TraceId"] = traceId,
            ["RequestId"] = context.TraceIdentifier,
            ["Method"] = context.Request.Method,
            ["Path"] = context.Request.Path.ToString(),
            ["StatusCode"] = context.Response.StatusCode,
            ["Elapsed"] = elapsedMs
        };

        if (!string.IsNullOrWhiteSpace(userId))
        {
            scope["UserId"] = userId;
        }

        if (canCaptureBodyForPath && !string.IsNullOrEmpty(requestBody))
        {
            scope["RequestBody"] = requestBody;
        }

        if (canCaptureBodyForPath && !string.IsNullOrEmpty(responseBody))
        {
            scope["ResponseBody"] = responseBody;
        }

        if (requestHeaders is { Count: > 0 })
        {
            scope["RequestHeaders"] = requestHeaders;
        }

        if (context.Response.Headers.Count > 0)
        {
            scope["ResponseHeaders"] = context.Response.Headers.ToDictionary(h => h.Key, h => h.Value.ToString());
        }

        return scope;
    }

    private async Task<string> ReadRequestBodyAsync(HttpRequest request)
    {
        var maxCaptureBytes = PbgHttpBodyUtils.GetMaxCaptureBytes(PbgHttpBodyUtils.DefaultMaxBodyLength);
        var bytes = await ReadLimitedBytesAsync(request.Body, maxCaptureBytes);
        return PbgHttpBodyUtils.BytesToBodyString(bytes, request.ContentType, PbgHttpBodyUtils.DefaultMaxBodyLength);
    }

    private async Task<string> ReadResponseBodyAsync(Stream responseStream, string? contentType)
    {
        var maxCaptureBytes = PbgHttpBodyUtils.GetMaxCaptureBytes(PbgHttpBodyUtils.DefaultMaxBodyLength);
        var bytes = await ReadLimitedBytesAsync(responseStream, maxCaptureBytes);
        return PbgHttpBodyUtils.BytesToBodyString(bytes, contentType, PbgHttpBodyUtils.DefaultMaxBodyLength);
    }

    private static async Task<byte[]> ReadLimitedBytesAsync(Stream stream, int maxBytes)
    {
        var buffer = new byte[4096];
        var totalRead = 0;

        await using var memory = new MemoryStream();

        while (totalRead < maxBytes)
        {
            var toRead = Math.Min(buffer.Length, maxBytes - totalRead);
            var read = await stream.ReadAsync(buffer, 0, toRead);

            if (read <= 0)
            {
                break;
            }

            await memory.WriteAsync(buffer, 0, read);
            totalRead += read;
        }

        return memory.ToArray();
    }

    private bool IsStaticFileRequest(PathString path)
    {
        var extension = Path.GetExtension(path.Value);
        return !string.IsNullOrEmpty(extension) && _options.ExcludedExtensions.Contains(extension);
    }
}
