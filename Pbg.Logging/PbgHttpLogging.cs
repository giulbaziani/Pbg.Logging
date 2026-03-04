using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;

namespace Pbg.Logging;

internal static class PbgHttpBodyUtils
{
    internal const int DefaultMaxBodyLength = 2000;

    public static int GetMaxCaptureBytes(int maxChars)
    {
        return Math.Max(64, maxChars * 4);
    }

    public static bool IsTextLikeContentType(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType))
        {
            return true;
        }

        var value = contentType.ToLowerInvariant();

        return value.StartsWith("text/")
               || value.Contains("json")
               || value.Contains("xml")
               || value.Contains("html")
               || value.Contains("x-www-form-urlencoded");
    }

    public static bool IsJsonContentType(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType))
        {
            return false;
        }

        return contentType.Contains("json", StringComparison.OrdinalIgnoreCase);
    }

    public static Encoding GetEncodingFromContentType(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType))
        {
            return Encoding.UTF8;
        }

        var parts = contentType.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var part in parts)
        {
            if (!part.StartsWith("charset=", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var charset = part["charset=".Length..].Trim().Trim('"');

            try
            {
                return Encoding.GetEncoding(charset);
            }
            catch
            {
                return Encoding.UTF8;
            }
        }

        return Encoding.UTF8;
    }

    public static string BytesToBodyString(byte[] bytes, string? contentType, int maxChars, bool logFullJson = false)
    {
        if (bytes.Length == 0)
        {
            return string.Empty;
        }

        if (IsTextLikeContentType(contentType))
        {
            var encoding = GetEncodingFromContentType(contentType);
            var text = encoding.GetString(bytes);

            if (logFullJson && IsJsonContentType(contentType))
            {
                return text;
            }

            return TrimToMaxLength(text, maxChars);
        }

        var base64 = Convert.ToBase64String(bytes);
        base64 = TrimToMaxLength(base64, maxChars);
        return $"[binary;base64]{base64}";
    }

    public static string TrimToMaxLength(string value, int maxChars)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxChars)
        {
            return value;
        }

        return value[..maxChars];
    }
}

internal static class PbgHeaderSanitizer
{
    private const int DefaultMaxHeaderValueLength = 512;

    private static readonly HashSet<string> SensitiveHeaderNames = new(StringComparer.OrdinalIgnoreCase)
    {
        //"Authorization",
        "Cookie",
        "Set-Cookie",
        "X-Api-Key",
        "Proxy-Authorization"
    };

    public static Dictionary<string, string> Sanitize(IHeaderDictionary headers)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var header in headers)
        {
            result[header.Key] = SanitizeHeaderValue(header.Key, header.Value.ToString());
        }

        return result;
    }

    public static Dictionary<string, string> Sanitize(HttpHeaders headers, HttpHeaders? contentHeaders)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var header in headers)
        {
            result[header.Key] = SanitizeHeaderValue(header.Key, string.Join(",", header.Value));
        }

        if (contentHeaders is not null)
        {
            foreach (var header in contentHeaders)
            {
                result[header.Key] = SanitizeHeaderValue(header.Key, string.Join(",", header.Value));
            }
        }

        return result;
    }

    private static string SanitizeHeaderValue(string headerName, string value)
    {
        if (SensitiveHeaderNames.Contains(headerName))
        {
            return "[REDACTED]";
        }

        if (string.IsNullOrEmpty(value) || value.Length <= DefaultMaxHeaderValueLength)
        {
            return value;
        }

        return value[..DefaultMaxHeaderValueLength];
    }
}

internal sealed class PbgHttpClientLoggingFilter : IHttpMessageHandlerBuilderFilter
{
    private readonly IServiceProvider _serviceProvider;

    public PbgHttpClientLoggingFilter(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public Action<HttpMessageHandlerBuilder> Configure(Action<HttpMessageHandlerBuilder> next)
    {
        return builder =>
        {
            next(builder);

            if (builder.AdditionalHandlers.Any(static handler => handler is PbgHttpClientLoggingHandler))
            {
                return;
            }

            var loggingHandler = _serviceProvider.GetRequiredService<PbgHttpClientLoggingHandler>();
            builder.AdditionalHandlers.Add(loggingHandler);
        };
    }
}

internal sealed class PbgHttpClientLoggingHandler : DelegatingHandler
{
    private readonly ILogger<PbgHttpClientLoggingHandler> _logger;
    private readonly PbgLoggerOptions _options;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public PbgHttpClientLoggingHandler(
        ILogger<PbgHttpClientLoggingHandler> logger,
        PbgLoggerOptions options,
        IHttpContextAccessor httpContextAccessor)
    {
        _logger = logger;
        _options = options;
        _httpContextAccessor = httpContextAccessor;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();

        var traceId = Activity.Current?.TraceId.ToHexString() ?? Guid.NewGuid().ToString("N");
        var requestId = Guid.NewGuid().ToString("N");
        var requestPath = request.RequestUri?.AbsolutePath;
        var canCaptureBodyForPath = !_options.IsBodyCaptureExcludedPath(requestPath);
        var userId = ResolveUserId(request.Headers.Authorization);

        var requestBody = string.Empty;
        string? requestBodyCaptureError = null;
        Dictionary<string, string>? requestHeaders = null;

        requestHeaders = PbgHeaderSanitizer.Sanitize(request.Headers, request.Content?.Headers);

        if (canCaptureBodyForPath)
        {
            try
            {
                requestBody = await CaptureAndCloneRequestBodyAsync(request, PbgHttpBodyUtils.DefaultMaxBodyLength);
            }
            catch (Exception ex)
            {
                requestBodyCaptureError = ex.Message;
            }
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

            if (!string.IsNullOrWhiteSpace(userId))
            {
                failedScope["UserId"] = userId;
            }

            if (canCaptureBodyForPath && !string.IsNullOrEmpty(requestBody))
            {
                failedScope["RequestBody"] = requestBody;
            }

            if (requestHeaders is { Count: > 0 })
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
        string? responseBodyCaptureError = null;
        Dictionary<string, string>? responseHeaders = null;

        responseHeaders = PbgHeaderSanitizer.Sanitize(response.Headers, response.Content?.Headers);

        if (canCaptureBodyForPath)
        {
            try
            {
                responseBody = await CaptureAndCloneResponseBodyAsync(response, PbgHttpBodyUtils.DefaultMaxBodyLength);
            }
            catch (Exception ex)
            {
                responseBodyCaptureError = ex.Message;
            }
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

        if (responseHeaders is { Count: > 0 })
        {
            scope["ResponseHeaders"] = responseHeaders;
        }

        if (!string.IsNullOrWhiteSpace(requestBodyCaptureError))
        {
            scope["RequestBodyCaptureError"] = requestBodyCaptureError;
        }

        if (!string.IsNullOrWhiteSpace(responseBodyCaptureError))
        {
            scope["ResponseBodyCaptureError"] = responseBodyCaptureError;
        }

        using (_logger.BeginScope(scope))
        {
            _logger.LogInformation("{Method} {Url}", request.Method, request.RequestUri);
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

        var contentType = response.Content.Headers.ContentType?.ToString();
        if (PbgHttpBodyUtils.IsJsonContentType(contentType))
        {
            return PbgHttpBodyUtils.BytesToBodyString(bytes, contentType, maxChars, logFullJson: true);
        }

        var maxCaptureBytes = PbgHttpBodyUtils.GetMaxCaptureBytes(maxChars);
        var captureBytes = bytes.Length <= maxCaptureBytes ? bytes : bytes[..maxCaptureBytes];

        return PbgHttpBodyUtils.BytesToBodyString(captureBytes, contentType, maxChars);
    }

    private string? ResolveUserId(AuthenticationHeaderValue? authorizationHeader)
    {
        var fromContext = _httpContextAccessor.HttpContext?.User;
        var contextUserId = fromContext?.FindFirst(ClaimTypes.NameIdentifier)?.Value
                            ?? fromContext?.FindFirst("sub")?.Value;

        if (!string.IsNullOrWhiteSpace(contextUserId))
        {
            return contextUserId;
        }

        if (authorizationHeader is null
            || !authorizationHeader.Scheme.Equals("Bearer", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(authorizationHeader.Parameter))
        {
            return null;
        }

        return TryGetUserIdFromJwt(authorizationHeader.Parameter);
    }

    private static string? TryGetUserIdFromJwt(string token)
    {
        var parts = token.Split('.');
        if (parts.Length < 2)
        {
            return null;
        }

        try
        {
            var payloadBytes = DecodeBase64Url(parts[1]);
            using var json = JsonDocument.Parse(payloadBytes);
            var root = json.RootElement;

            if (TryGetClaim(root, "sub", out var sub))
            {
                return sub;
            }

            if (TryGetClaim(root, ClaimTypes.NameIdentifier, out var nameId))
            {
                return nameId;
            }
        }
        catch
        {
            return null;
        }

        return null;
    }

    private static bool TryGetClaim(JsonElement payload, string claimName, out string value)
    {
        value = string.Empty;

        if (!payload.TryGetProperty(claimName, out var element))
        {
            return false;
        }

        var text = element.GetString();
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        value = text;
        return true;
    }

    private static byte[] DecodeBase64Url(string value)
    {
        var normalized = value.Replace('-', '+').Replace('_', '/');
        normalized = normalized.PadRight(normalized.Length + ((4 - normalized.Length % 4) % 4), '=');
        return Convert.FromBase64String(normalized);
    }

    private static void CopyContentHeaders(HttpHeaders source, HttpHeaders destination)
    {
        foreach (var header in source)
        {
            destination.TryAddWithoutValidation(header.Key, header.Value);
        }
    }
}
