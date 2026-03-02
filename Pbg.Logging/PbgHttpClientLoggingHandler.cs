using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Net.Http.Headers;

namespace Pbg.Logging;

internal sealed class PbgHttpClientLoggingHandler : DelegatingHandler
{
    private readonly ILogger<PbgHttpClientLoggingHandler> _logger;
    private readonly PbgLoggerOptions _options;

    public PbgHttpClientLoggingHandler(ILogger<PbgHttpClientLoggingHandler> logger, PbgLoggerOptions options)
    {
        _logger = logger;
        _options = options;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (!_options.EnableHttpClientLogging)
        {
            return await base.SendAsync(request, cancellationToken);
        }

        var sw = Stopwatch.StartNew();

        var traceId = Activity.Current?.TraceId.ToHexString() ?? Guid.NewGuid().ToString("N");
        var requestId = Guid.NewGuid().ToString("N");

        var requestBody = string.Empty;
        Dictionary<string, string>? requestHeaders = null;

        if (_options.IncludeRequestHeaders)
        {
            requestHeaders = ExtractHeaders(request.Headers, request.Content?.Headers);
        }

        if (_options.IncludeRequestBody)
        {
            requestBody = await CaptureAndCloneRequestBodyAsync(request, _options.MaxBodyLength);
        }

        HttpResponseMessage response;
        try
        {
            response = await base.SendAsync(request, cancellationToken);
        }
        catch (Exception ex)
        {
            sw.Stop();

            var failedScope = new Dictionary<string, object>
            {
                ["TraceId"] = traceId,
                ["RequestId"] = requestId,
                ["Method"] = request.Method.Method,
                ["Path"] = request.RequestUri?.ToString() ?? string.Empty,
                ["Elapsed"] = sw.Elapsed.TotalMilliseconds
            };

            if (_options.IncludeRequestBody)
            {
                failedScope["RequestBody"] = requestBody;
            }

            if (_options.IncludeRequestHeaders && requestHeaders is not null)
            {
                failedScope["RequestHeaders"] = requestHeaders;
            }

            using (_logger.BeginScope(failedScope))
            {
                _logger.LogError(ex, "HTTP OUT {Method} {Url} failed", request.Method, request.RequestUri);
            }

            throw;
        }

        sw.Stop();

        var responseBody = string.Empty;
        Dictionary<string, string>? responseHeaders = null;

        if (_options.IncludeResponseHeaders)
        {
            responseHeaders = ExtractHeaders(response.Headers, response.Content?.Headers);
        }

        if (_options.IncludeResponseBody)
        {
            responseBody = await CaptureAndCloneResponseBodyAsync(response, _options.MaxBodyLength);
        }

        var scope = new Dictionary<string, object>
        {
            ["TraceId"] = traceId,
            ["RequestId"] = requestId,
            ["Method"] = request.Method.Method,
            ["Path"] = request.RequestUri?.ToString() ?? string.Empty,
            ["StatusCode"] = (int)response.StatusCode,
            ["Elapsed"] = sw.Elapsed.TotalMilliseconds
        };

        if (_options.IncludeRequestBody)
        {
            scope["RequestBody"] = requestBody;
        }

        if (_options.IncludeResponseBody)
        {
            scope["ResponseBody"] = responseBody;
        }

        if (_options.IncludeRequestHeaders && requestHeaders is not null)
        {
            scope["RequestHeaders"] = requestHeaders;
        }

        if (_options.IncludeResponseHeaders && responseHeaders is not null)
        {
            scope["ResponseHeaders"] = responseHeaders;
        }

        using (_logger.BeginScope(scope))
        {
            _logger.LogInformation("HTTP OUT {Method} {Url} responded {StatusCode}", request.Method, request.RequestUri, (int)response.StatusCode);
        }

        return response;
    }

    private static async Task<string> CaptureAndCloneRequestBodyAsync(HttpRequestMessage request, int maxChars)
    {
        if (request.Content is null)
        {
            return string.Empty;
        }

        var bytes = await request.Content.ReadAsByteArrayAsync();

        var clonedContent = new ByteArrayContent(bytes);
        CopyContentHeaders(request.Content.Headers, clonedContent.Headers);
        request.Content = clonedContent;

        var maxCaptureBytes = PbgHttpBodyUtils.GetMaxCaptureBytes(maxChars);
        var captureBytes = bytes.Length <= maxCaptureBytes ? bytes : bytes[..maxCaptureBytes];
        var contentType = request.Content.Headers.ContentType?.ToString();

        return PbgHttpBodyUtils.BytesToBodyString(captureBytes, contentType, maxChars);
    }

    private static async Task<string> CaptureAndCloneResponseBodyAsync(HttpResponseMessage response, int maxChars)
    {
        if (response.Content is null)
        {
            return string.Empty;
        }

        var bytes = await response.Content.ReadAsByteArrayAsync();

        var clonedContent = new ByteArrayContent(bytes);
        CopyContentHeaders(response.Content.Headers, clonedContent.Headers);
        response.Content = clonedContent;

        var maxCaptureBytes = PbgHttpBodyUtils.GetMaxCaptureBytes(maxChars);
        var captureBytes = bytes.Length <= maxCaptureBytes ? bytes : bytes[..maxCaptureBytes];
        var contentType = response.Content.Headers.ContentType?.ToString();

        return PbgHttpBodyUtils.BytesToBodyString(captureBytes, contentType, maxChars);
    }

    private static Dictionary<string, string> ExtractHeaders(HttpHeaders headers, HttpHeaders? contentHeaders)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var header in headers)
        {
            result[header.Key] = string.Join(",", header.Value);
        }

        if (contentHeaders is not null)
        {
            foreach (var header in contentHeaders)
            {
                result[header.Key] = string.Join(",", header.Value);
            }
        }

        return result;
    }

    private static void CopyContentHeaders(HttpHeaders source, HttpHeaders destination)
    {
        foreach (var header in source)
        {
            destination.TryAddWithoutValidation(header.Key, header.Value);
        }
    }
}
