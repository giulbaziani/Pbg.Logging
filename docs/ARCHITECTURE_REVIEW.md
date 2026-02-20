# Pbg.Logging Architecture Review (Solution Architect View)

## Current strengths
- Async pipeline using `Channel<T>` and `BackgroundService` keeps application request paths non-blocking.
- Batching plus retry logic already exists and is simple to reason about.
- Middleware enriches logs with request context (user, path, method, headers, body, elapsed time).

## Priority improvements

### 1) Reliability and shutdown behavior
- Respect cancellation in queue reads (`WaitToReadAsync(stoppingToken)` instead of `CancellationToken.None`).
- Drain remaining queue entries during shutdown and do one final flush to reduce lost logs.
- Add explicit metrics/counters for dropped logs (channel full, failed send, retries exhausted).

### 2) HTTP client and transport hardening
- Replace direct `new HttpClient()` with `IHttpClientFactory` and named client configuration.
- Add retry jitter and consider Polly-based policy composition (retry + circuit breaker + timeout).
- Add optional gzip compression and payload size limits for large batches.

### 3) Data model consistency
- Add missing properties that are already captured by middleware (for example `ResponseHeaders`).
- Add `CategoryName`, `EventId`, and optional `Scopes` projection for richer diagnostics.
- Standardize trace semantics: prefer W3C trace id from `Activity` and use `context.TraceIdentifier` separately as request id.

### 4) Privacy and compliance controls
- Make request/response body logging opt-in and size-bounded.
- Add sensitive-data scrubbing hooks (headers, json fields, query values).
- Add configurable allow/deny lists for headers and route patterns.

### 5) Configuration and package ergonomics
- Add option validation for `BatchSize > 0`, `FlushInterval > TimeSpan.Zero`, and URI scheme restrictions.
- Multi-target LTS frameworks (for example `net8.0` + latest) to increase adoption.
- Publish guidance for production defaults (timeouts, retry, sampling, body logging disabled by default).

### 6) Observability of the logger itself
- Emit internal health telemetry (queue depth, send latency, success/failure ratio).
- Provide hooks for fallback sink (file/stdout) when remote endpoint is unavailable.
- Add structured internal diagnostics instead of only `Console.Error`.

### 7) Testing strategy
- Unit tests for filtering, scope mapping, and options validation.
- Integration tests with a local test server validating batching, retry, and shutdown flush.
- Load tests for throughput and memory usage under high cardinality logs.


## Recommended minimum fields (keep these mandatory)
For a general-purpose logger, keep only the fields required for **time, severity, message, and correlation** as mandatory:

- `timestamp`
- `level`
- `message`
- `service` (or `projectName`)
- `environment`
- `traceId` (or `requestId` when distributed tracing is unavailable)

Everything else should be optional and preferably configurable.

### Optional fields (enable by need)
- **Debugging context**: `category`, `eventId`, `machineName`, `ipAddress`, `userId`
- **HTTP context**: `method`, `path`, `statusCode`, `elapsedMs`
- **High-risk/sensitive**: `requestBody`, `responseBody`, full headers

### Practical policy
- Always emit the 6 mandatory fields.
- Emit HTTP fields only for request logs (not every application log).
- Keep body/header capture **off by default** and guard with redaction + size limits.
- Prefer a stable top-level schema and put non-standard details under a `metadata` object to avoid field explosion.

## Suggested backlog (high level)
1. **R1**: Cancellation correctness, shutdown drain/flush, dropped-log counters.
2. **R2**: `IHttpClientFactory` integration and resilience policy hardening.
3. **R3**: Privacy controls + schema enrichment (`ResponseHeaders`, `EventId`, category).
4. **R4**: Test suite + benchmark harness + CI quality gates.
5. **R5**: Framework targeting/package refinements + operational documentation.
