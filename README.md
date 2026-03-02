# Pbg.Logging

[![NuGet](https://img.shields.io/nuget/v/Pbg.Logging.svg)](https://www.nuget.org/packages/Pbg.Logging/)
[![NuGet Downloads](https://img.shields.io/nuget/dt/Pbg.Logging.svg)](https://www.nuget.org/packages/Pbg.Logging/)

A high-performance, centralized logging library for .NET applications that captures and sends structured logs to a remote endpoint in batches.

## ✨ Features

- 🚀 Asynchronous & non-blocking
- 📦 Batch processing with configurable intervals
- 🔄 Automatic retry with exponential backoff
- 🌐 HTTP request/response middleware
- 🔍 Distributed tracing support
- 🎯 Smart log filtering

## 📦 Installation

Install via NuGet Package Manager:

```bash
dotnet add package Pbg.Logging
```

Or via NuGet Package Manager Console:

```powershell
Install-Package Pbg.Logging
```

Or visit the [NuGet Gallery](https://www.nuget.org/packages/Pbg.Logging/)


## 🌐 Log Dashboard

Visit **[https://logs.pbg.ge/](https://logs.pbg.ge/)** to:

- 📋 **View and search logs** — browse all captured logs from your applications in real time
- 🔑 **Generate a Service-Key** — after registration, create a `LicenseKey` to use with this library

> **Getting started:** Register at [logs.pbg.ge](https://logs.pbg.ge/), generate your Service-Key, and use it as the `LicenseKey` in your configuration.

## Quick Start

### ASP.NET Core Web API

```csharp
using Pbg.Logging;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.AddPbgLogger(options =>
{
    options.LicenseKey = new Guid("your-license-key-here");
    options.EndpointUrl = "https://logs.pbg.ge/api/v1/log";
    options.ProjectName = "MyWebApi";
    options.Environment = PbgEnvironment.Production;
});

var app = builder.Build();

app.UsePbgLogging(); // Automatic HTTP logging middleware

app.Run();
```

### Console Application

```csharp
using Pbg.Logging;

var host = Host.CreateDefaultBuilder(args)
    .ConfigureServices((context, services) =>
    {
        services.AddLogging(builder =>
        {
            builder.AddPbgLogger(options =>
            {
                options.LicenseKey = new Guid("your-license-key-here");
                options.EndpointUrl = "https://your-endpoint.com/api/logs";
                options.ProjectName = "MyConsoleApp";
                options.Environment = PbgEnvironment.Development;
            });
        });
    })
    .Build();

await host.RunAsync();
```

### Usage

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
        _logger.LogError("Something went wrong");
    }
}
```

### Environment Mapping (One Code for All Environments)

Use `ASPNETCORE_ENVIRONMENT`/`builder.Environment.EnvironmentName` and map it to `PbgEnvironment` once:

```csharp
using Pbg.Logging.Model;

builder.Logging.AddPbgLogger(options =>
{
    options.LicenseKey = Guid.Parse(builder.Configuration["PbgLogging:LicenseKey"]!);
    options.EndpointUrl = builder.Configuration["PbgLogging:EndpointUrl"]!;
    options.ProjectName = builder.Configuration["PbgLogging:ProjectName"] ?? builder.Environment.ApplicationName;
    options.Environment = builder.Environment.EnvironmentName.ToLowerInvariant() switch
    {
        "development" => PbgEnvironment.Development,
        "staging" => PbgEnvironment.Staging,
        "production" => PbgEnvironment.Production,
        "testing" => PbgEnvironment.Testing,
        "uat" => PbgEnvironment.Uat,
        _ => PbgEnvironment.Production
    };
});
```

`LicenseKey` can be the same for all environments or different per environment (recommended).
Keep values in `appsettings.{Environment}.json`:

```json
{
  "PbgLogging": {
    "LicenseKey": "11111111-1111-1111-1111-111111111111",
    "EndpointUrl": "https://logs.pbg.ge/api/v1/log",
    "ProjectName": "MyService"
  }
}
```

## ⚙️ Configuration

| Property | Type | Required | Default | Description |
|----------|------|----------|---------|-------------|
| `LicenseKey` | `Guid` | ✅ | - | Your license key |
| `EndpointUrl` | `string` | ✅ | - | API endpoint URL |
| `ProjectName` | `string` | | `"UnknownProject"` | Project identifier |
| `Environment` | `PbgEnvironment` | ✅ | - | Development, Staging, Production, Testing, Uat |
| `BatchSize` | `int` | | `50` | Logs per batch |
| `FlushInterval` | `TimeSpan` | | `3s` | Batch send interval |
| `IncludeUserId` | `bool` | | `false` | Include user identifier in logs |
| `IncludeRequestHeaders` | `bool` | | `true` | Include request headers (consider allowlist/redaction) |
| `IncludeResponseHeaders` | `bool` | | `true` | Include response headers |
| `IncludeRequestBody` | `bool` | | `true` | Include request body (sensitive; enabled by default) |
| `IncludeResponseBody` | `bool` | | `true` | Include response body (sensitive; enabled by default) |
| `EnableHttpClientLogging` | `bool` | | `true` | Capture outbound `HttpClient` calls created via `IHttpClientFactory` |
| `MaxBodyLength` | `int` | | `4096` | Maximum characters stored for request/response bodies |

## 📊 Log Structure

```json
{
  "timestamp": "2026-01-15T10:30:00Z",
  "logLevel": "Information",
  "message": "Your log message",
  "projectName": "MyApp",
  "environment": "Production",
  "machineName": "SERVER-01",
  "ipAddress": "192.168.1.100",
  "traceId": "abc123",
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

## 🎯 Automatic Filtering

- ✅ All `Error` and above
- ✅ `Microsoft.Hosting.Lifetime` logs
- ⚠️ Microsoft/System logs: `Warning` and above only
- ✅ Application logs: All levels

## 🔄 Retry Strategy

- Max retries: **3**
- Backoff: **2s → 4s → 8s** (exponential)
- Timeout: **15s** per request

## 📝 License

Requires a valid license key.

## 🔗 Links

- **Dashboard**: [logs.pbg.ge](https://logs.pbg.ge/) — View logs & generate Service-Keys
- **GitHub**: [guliv3r/Pbg.Logging](https://github.com/guliv3r/Pbg.Logging)
- **Issues**: [Report bugs](https://github.com/guliv3r/Pbg.Logging/issues)
