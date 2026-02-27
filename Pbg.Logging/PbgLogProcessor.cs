using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Pbg.Logging.Model;
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

    public PbgLogProcessor(Channel<PbgLogEntry> channel, PbgLoggerOptions options)
    {
        _channel = channel;
        _options = options;
        _httpClient = new HttpClient();
        _httpClient.Timeout = TimeSpan.FromSeconds(15);
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
                    if (await SendLogsAsync(batch))
                    {
                        await FlushStoredLogsAsync(stoppingToken);
                    }
                    else
                    {
                        await _fileStore.SaveAsync(batch);
                        await SelfLogAsync("[Pbg.Logging] Batch saved to local fallback storage.", LogLevel.Warning);
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
                break;
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _channel.Writer.TryComplete();

        await base.StopAsync(cancellationToken);
    }

    private async Task<bool> SendLogsAsync(List<PbgLogEntry> logs)
    {
        int maxRetries = 3;
        int delaySeconds = 2;

        for (int i = 0; i < maxRetries; i++)
        {
            try
            {
                var response = await _httpClient.PostAsJsonAsync(_options.EndpointUrl, logs, JsonOptions);

                if (response.IsSuccessStatusCode)
                {
                    return true;
                }

                await SelfLogAsync($"[Pbg.Logging] Server returned error: {response.StatusCode}. Attempt {i + 1} of {maxRetries}", LogLevel.Error);
            }
            catch (Exception ex)
            {
                await SelfLogAsync($"[Pbg.Logging] Network error: {ex.Message}. Attempt {i + 1} of {maxRetries}", LogLevel.Error);
            }

            if (i < maxRetries - 1)
            {
                await Task.Delay(TimeSpan.FromSeconds(delaySeconds));
                delaySeconds *= 2;
            }
        }

        return false;
    }

    private async Task FlushStoredLogsAsync(CancellationToken stoppingToken)
    {
        foreach (var file in _fileStore.GetPendingFiles())
        {
            if (stoppingToken.IsCancellationRequested)
                break;

            var logs = await _fileStore.LoadBatchAsync(file);

            if (logs is null or { Count: 0 })
            {
                _fileStore.DeleteBatch(file);
                continue;
            }

            if (await SendLogsAsync(logs))
            {
                _fileStore.DeleteBatch(file);
            }
            else
            {
                break;
            }
        }
    }

    private async Task SelfLogAsync(string message, LogLevel level)
    {
        var selfLog = new PbgLogEntry
        {
            Timestamp = DateTime.UtcNow,
            LogLevel = level.ToString(),
            Message = message,
            ProjectName = _options.ProjectName,
            Environment = _options.Environment.ToString(),
            MachineName = _machineName,
            IpAddress = _ipAddress
        };

        Console.Error.WriteLine($"[Pbg.Logging][{level}] {message}");

        try
        {
            await _httpClient.PostAsJsonAsync(_options.EndpointUrl, new[] { selfLog }, JsonOptions);
        }
        catch
        {
            // Silently ignore — console output above is the fallback
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
        catch { return "0.0.0.0"; }
    }
}