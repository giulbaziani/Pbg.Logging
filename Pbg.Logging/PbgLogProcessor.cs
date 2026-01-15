using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Pbg.Logging.Model;
using System.Net.Http.Json;
using System.Threading.Channels;

namespace Pbg.Logging;

internal class PbgLogProcessor : BackgroundService
{
    private readonly Channel<PbgLogEntry> _channel;
    private readonly PbgLoggerOptions _options;
    private readonly HttpClient _httpClient;
    private readonly string _machineName;
    private readonly string _ipAddress;

    public PbgLogProcessor(Channel<PbgLogEntry> channel, PbgLoggerOptions options)
    {
        _channel = channel;
        _options = options;
        _httpClient = new HttpClient();
        _httpClient.Timeout = TimeSpan.FromSeconds(15);

        _httpClient.DefaultRequestHeaders.Add("X-License-Key", _options.LicenseKey.ToString());

        _machineName = Environment.MachineName;
        _ipAddress = GetLocalIpAddress();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var reader = _channel.Reader;
        var batch = new List<PbgLogEntry>();

        while (await reader.WaitToReadAsync(CancellationToken.None))
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
                    await SendLogsAsync(batch);
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

    private async Task SendLogsAsync(List<PbgLogEntry> logs)
    {
        int maxRetries = 3;
        int delaySeconds = 2;

        for (int i = 0; i < maxRetries; i++)
        {
            try
            {
                var response = await _httpClient.PostAsJsonAsync(_options.EndpointUrl, logs);

                if (response.IsSuccessStatusCode)
                {
                    return;
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

        await SelfLogAsync("[Pbg.Logging] Critical: All retry attempts failed. Logs for this batch are lost.", LogLevel.Error);
    }

    private async Task SelfLogAsync(string message, LogLevel level)
    {
        var selfLog = new PbgLogEntry
        {
            Timestamp = DateTime.UtcNow,
            LogLevel = level.ToString(),
            Message = $"[Pbg.Logging Internal]: {message}",
            ProjectName = _options.ProjectName,
            Environment = _options.Environment.ToString(),
            MachineName = _machineName,
            IpAddress = _ipAddress
        };

        Console.WriteLine(selfLog.Message);
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