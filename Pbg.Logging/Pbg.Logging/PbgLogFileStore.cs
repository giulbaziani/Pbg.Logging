using Pbg.Logging.Model;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Pbg.Logging;

internal sealed class PbgLogFileStore
{
    private readonly string _directory;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public PbgLogFileStore(string projectName)
    {
        _directory = Path.Combine(Path.GetTempPath(), "Pbg.Logging", SanitizeName(projectName));
        Directory.CreateDirectory(_directory);
    }

    public async Task SaveAsync(List<PbgLogEntry> logs)
    {
        var fileName = $"batch_{DateTime.UtcNow:yyyyMMddHHmmssfff}_{Guid.NewGuid():N}.json";
        var filePath = Path.Combine(_directory, fileName);

        await using var stream = File.Create(filePath);
        await JsonSerializer.SerializeAsync(stream, logs, JsonOptions);
    }

    public IEnumerable<string> GetPendingFiles()
    {
        if (!Directory.Exists(_directory))
            return [];

        return Directory.EnumerateFiles(_directory, "batch_*.json")
            .OrderBy(static f => f);
    }

    public async Task<List<PbgLogEntry>?> LoadBatchAsync(string filePath)
    {
        try
        {
            await using var stream = File.OpenRead(filePath);
            return await JsonSerializer.DeserializeAsync<List<PbgLogEntry>>(stream, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    public void DeleteBatch(string filePath)
    {
        try
        {
            File.Delete(filePath);
        }
        catch
        {
            // Ignore — will be retried next cycle
        }
    }

    private static string SanitizeName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return string.Concat(name.Select(c => invalid.Contains(c) ? '_' : c));
    }
}
