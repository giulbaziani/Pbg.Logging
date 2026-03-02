using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Pbg.Logging.Model;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;

namespace Pbg.Logging;

internal class PbgLogProcessor : BackgroundService
{
    private readonly Channel<PbgLogEntry> _channel;
    private readonly PbgLoggerOptions _options;
    private readonly HttpClient _httpClient;
    private readonly PbgLogFileStore _fileStore;
    private readonly string _machineName;
    private readonly string _ipAddress;
    private static readonly JsonSerializerOptions JsonOptions = new() { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
    private const int MaxResponseSnippetLength = 512;

    public PbgLogProcessor(Channel<PbgLogEntry> channel, PbgLoggerOptions options)
    {
        _channel = channel;
        _options = options;
        _httpClient = new HttpClient();
        _httpClient.Timeout = _options.RequestTimeout;
        _fileStore = new PbgLogFileStore(options.ProjectName);

        _httpClient.DefaultRequestHeaders.Add("X-License-Key", _options.LicenseKey.ToString());

        _machineName = Environment.MachineName;
        _ipAddress = GetLocalIpAddress();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await FlushStoredLogsAsync(stoppingToken);

        var reader = _channel.Reader;
        var batch = new List<PbgLogEntry>();

        while (await reader.WaitToReadAsync(stoppingToken))
        {
            try
            {
                while (batch.Count < _options.BatchSize && reader.TryRead(out var log))
                {
                    log.ProjectName = _options.ProjectName;
                    log.Environment = _options.Environment.ToString();
                    log.MachineName = _machineName;
                    log.IpAddress = _ipAddress;
                    batch.Add(log);
                }

                if (batch.Count > 0)
                {
                    var sendResult = await SendLogsAsync(batch, stoppingToken);

                    if (sendResult.Outcome == SendOutcome.Success)
                    {
                        await FlushStoredLogsAsync(stoppingToken);
                    }
                    else if (sendResult.Outcome == SendOutcome.RetryableFailure)
                    {
                        await _fileStore.SaveAsync(batch);
                        await SelfLogAsync("[Pbg.Logging] Batch saved to local fallback storage.", LogLevel.Warning);
                    }
                    else
                    {
                        await _fileStore.SaveRejectedAsync(batch, sendResult.Detail ?? "Non-retriable error from server.");
                        await SelfLogAsync("[Pbg.Logging] Batch moved to rejected local storage due to non-retriable server error.", LogLevel.Warning);
                    }

                    batch.Clear();
                }

                if (!stoppingToken.IsCancellationRequested)
                {
                    await Task.Delay(_options.FlushInterval, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                await SelfLogAsync($"[Pbg.Logging Error]: {ex.Message}", LogLevel.Error);
            }

            if (stoppingToken.IsCancellationRequested && reader.Completion.IsCompleted)
            {
                break;
            }
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _channel.Writer.TryComplete();
        await base.StopAsync(cancellationToken);
    }

    private async Task<SendResult> SendLogsAsync(List<PbgLogEntry> logs, CancellationToken cancellationToken)
    {
        var maxRetries = _options.MaxRetries;
        var retryDelay = _options.RetryBaseDelay;

        for (int i = 0; i < maxRetries; i++)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, _options.EndpointUrl)
                {
                    Content = JsonContent.Create(logs, options: JsonOptions)
                };

                using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

                if (response.IsSuccessStatusCode)
                {
                    return SendResult.Success();
                }

                var responseSnippet = await ReadResponseSnippetAsync(response, cancellationToken);
                var detail = $"Status {(int)response.StatusCode} ({response.StatusCode}).{responseSnippet}";

                if (IsNonRetriableStatusCode(response.StatusCode))
                {
                    await SelfLogAsync($"[Pbg.Logging] Server returned non-retriable error: {detail}", LogLevel.Error);
                    return SendResult.NonRetryable(detail);
                }

                await SelfLogAsync($"[Pbg.Logging] Server returned retryable error: {detail} Attempt {i + 1} of {maxRetries}", LogLevel.Error);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                await SelfLogAsync($"[Pbg.Logging] Network error: {ex.Message}. Attempt {i + 1} of {maxRetries}", LogLevel.Error);
            }

            if (i >= maxRetries - 1)
            {
                continue;
            }

            await Task.Delay(retryDelay, cancellationToken);
            retryDelay = TimeSpan.FromTicks(retryDelay.Ticks * 2);
        }

        return SendResult.Retryable("[Pbg.Logging] Upload failed after max retries.");
    }

    private async Task FlushStoredLogsAsync(CancellationToken stoppingToken)
    {
        foreach (var file in _fileStore.GetPendingFiles())
        {
            if (stoppingToken.IsCancellationRequested)
            {
                break;
            }

            var logs = await _fileStore.LoadBatchAsync(file);

            if (logs is null or { Count: 0 })
            {
                _fileStore.DeleteBatch(file);
                continue;
            }

            var sendResult = await SendLogsAsync(logs, stoppingToken);

            if (sendResult.Outcome == SendOutcome.Success)
            {
                _fileStore.DeleteBatch(file);
            }
            else if (sendResult.Outcome == SendOutcome.NonRetryableFailure)
            {
                await _fileStore.SaveRejectedAsync(logs, sendResult.Detail ?? "Non-retriable error from server.");
                _fileStore.DeleteBatch(file);

                await SelfLogAsync(
                    $"[Pbg.Logging] Pending batch '{Path.GetFileName(file)}' moved to rejected storage due to non-retriable server error.",
                    LogLevel.Warning);
            }
            else
            {
                break;
            }
        }
    }

    private async Task SelfLogAsync(string message, LogLevel level)
    {
        await Console.Error.WriteLineAsync($"[Pbg.Logging][{level}] {message}");
    }

    private static bool IsNonRetriableStatusCode(HttpStatusCode statusCode)
    {
        var code = (int)statusCode;

        if (code < 400 || code >= 500)
        {
            return false;
        }

        return statusCode != HttpStatusCode.RequestTimeout && code != 429;
    }

    private static async Task<string> ReadResponseSnippetAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.Content is null)
        {
            return string.Empty;
        }

        try
        {
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(responseBody))
            {
                return string.Empty;
            }

            responseBody = responseBody.ReplaceLineEndings(" ");
            responseBody = PbgHttpBodyUtils.TrimToMaxLength(responseBody, MaxResponseSnippetLength);
            return $" Response: {responseBody}";
        }
        catch
        {
            return string.Empty;
        }
    }

    private string GetLocalIpAddress()
    {
        try
        {
            return System.Net.Dns.GetHostEntry(_machineName).AddressList
                .FirstOrDefault(ip => ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)?
                .ToString() ?? "127.0.0.1";
        }
        catch
        {
            return "0.0.0.0";
        }
    }

    private readonly record struct SendResult(SendOutcome Outcome, string? Detail)
    {
        public static SendResult Success() => new(SendOutcome.Success, null);
        public static SendResult Retryable(string? detail) => new(SendOutcome.RetryableFailure, detail);
        public static SendResult NonRetryable(string? detail) => new(SendOutcome.NonRetryableFailure, detail);
    }

    private enum SendOutcome
    {
        Success,
        RetryableFailure,
        NonRetryableFailure
    }
}
