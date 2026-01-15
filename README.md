# Pbg.Logging

[![NuGet](https://img.shields.io/nuget/v/Pbg.Logging.svg)](https://www.nuget.org/packages/Pbg.Logging/)
[![NuGet Downloads](https://img.shields.io/nuget/dt/Pbg.Logging.svg)](https://www.nuget.org/packages/Pbg.Logging/)

A high-performance, centralized logging library for .NET applications that captures and sends structured logs to a remote endpoint in batches.

## Features

- 🚀 **Asynchronous & Non-blocking** - Uses channels for efficient log processing
- 📦 **Batch Processing** - Groups logs into batches to reduce network overhead
- 🔄 **Automatic Retry** - Built-in retry logic with exponential backoff
- 🌐 **HTTP Request/Response Logging** - Middleware for automatic API transaction logging
- 🔍 **Distributed Tracing** - Automatic TraceId propagation using Activity API
- 👤 **User Context** - Captures user identity from claims
- ⚙️ **Configurable** - Flexible options for batch size, flush intervals, and environments
- 🎯 **Smart Filtering** - Reduces noise from Microsoft/System logs

## Installation

Install via NuGet Package Manager:

```bash
dotnet add package Pbg.Logging
```

Or via NuGet Package Manager Console:

```powershell
Install-Package Pbg.Logging
```

Or visit the [NuGet Gallery](https://www.nuget.org/packages/Pbg.Logging/)

## Quick Start

### 1. Configure in `Program.cs`

```csharp
using Pbg.Logging;

var builder = WebApplication.CreateBuilder(args);

// Add Pbg.Logging
builder.Logging.AddPbgLogging(options =>
{
    options.LicenseKey = Guid.Parse("your-license-key-here");
    options.EndpointUrl = "https://your-logging-endpoint.com/api/logs";
    options.ProjectName = "MyAwesomeApp";
    options.Environment = PbgEnvironment.Production;
    options.BatchSize = 50;
    options.FlushInterval = TimeSpan.FromSeconds(3);
});

var app = builder.Build();

// Add middleware for HTTP logging
app.UsePbgLogging();

app.Run();
```

### 2. Use in Your Code

```csharp
public class MyService
{
    private readonly ILogger<MyService> _logger;

    public MyService(ILogger<MyService> logger)
    {
        _logger = logger;
    }

    public void DoWork()
    {
        _logger.LogInformation("Processing started");
        
        try
        {
            // Your code here
            _logger.LogInformation("Processing completed successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Processing failed");
        }
    }
}
```

## Configuration Options

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `LicenseKey` | `Guid` | *Required* | Your Pbg.Logging license key |
| `EndpointUrl` | `string` | *Required* | The URL endpoint where logs will be sent |
| `ProjectName` | `string` | `"UnknownProject"` | Name of your project for identification |
| `Environment` | `PbgEnvironment` | - | Environment type (Development, Staging, Production, Testing, Uat) |
| `BatchSize` | `int` | `50` | Number of logs to group before sending |
| `FlushInterval` | `TimeSpan` | `3 seconds` | Time to wait between batch sends |

## PbgLoggingMiddleware

The middleware automatically captures:

- HTTP Method and Path
- Request Body
- Response Body
- Status Code
- Elapsed Time (milliseconds)
- User ID (from JWT claims)
- Trace ID

### Custom Scopes

```csharp
using (_logger.BeginScope(new Dictionary<string, object>
{
    ["UserId"] = "user-123",
    ["TraceId"] = "custom-trace-id"
}))
{
    _logger.LogInformation("This log includes scope data");
}
```

## Log Entry Structure

Each log entry includes:

```json
{
  "timestamp": "2026-01-15T10:30:00Z",
  "logLevel": "Information",
  "message": "Your log message",
  "projectName": "MyAwesomeApp",
  "environment": "Production",
  "machineName": "SERVER-01",
  "ipAddress": "192.168.1.100",
  "traceId": "abc123...",
  "userId": "user-123",
  "method": "POST",
  "path": "/api/users",
  "statusCode": 200,
  "requestBody": "{...}",
  "responseBody": "{...}",
  "elapsedMilliseconds": 45.2,
  "exception": null
}
```

## Log Filtering

Smart filtering to reduce noise:

- **Error and above**: Always captured
- **Microsoft.Hosting.Lifetime**: Always captured
- **Microsoft/System namespaces**: Warning level or higher
- **Application logs**: All levels

## Retry Strategy

- **Max Retries**: 3 attempts
- **Initial Delay**: 2 seconds
- **Backoff**: Exponential (2s → 4s → 8s)
- **Timeout**: 15 seconds per HTTP request

## Requirements

- .NET 10 or higher
- ASP.NET Core (for middleware)

## Best Practices

1. Use appropriate log levels - reserve `Error` for actual errors
2. Avoid logging sensitive data in request/response bodies
3. Configure batch size based on traffic volume
4. Monitor console output for internal errors
5. Test in staging before production deployment

## License

Requires a valid license key. Contact the provider for licensing information.

## Support

- **GitHub**: [https://github.com/guliv3r/Pbg.Logging](https://github.com/guliv3r/Pbg.Logging)
- **Issues**: Report bugs and request features via GitHub Issues

---

Built with ❤️ for high-performance logging
