# ADR 0007: Shared web service plumbing

- Status: Accepted
- Date: 2026-08-30

## Context

MediFlow runs two REST APIs (`MediFlow.Api` for members/plans/enrollments,
`MediFlow.Claims.Api` for claims and rollups) with identical cross-cutting
needs: structured logging, OpenAPI with a docs UI, rate limiting,
authentication, health endpoints, security headers, and telemetry. Two
hand-rolled `Program.cs` files would drift - the classic failure being one API
getting a hardening fix or telemetry export and the other quietly not.

## Decision

Both APIs compose the same two extension methods from
`src/MediFlow.Infrastructure/Web/WebServiceExtensions.cs`:

```csharp
builder.AddMediFlowWebService("mediflow-api");   // or "mediflow-claims-api"
...
app.UseMediFlowWebService("mediflow-api");
```

`AddMediFlowWebService` registers, once, for both services:

- **Serilog** console logging, enriched with the service name and sharing one
  output template.
- **OpenAPI + Scalar** (`AddOpenApi` / `MapOpenApi` / `MapScalarApiReference`).
- **ProblemDetails** and HTTP logging for uniform error bodies.
- **Rate limiting**: a fixed-window limiter (100 requests/minute) and
  `UseRateLimiter()` in the pipeline.
- **Health checks**, including a `MediFlowDbContext` readiness check, at
  `/health/live`, `/health/ready`, and a JSON-summarizing `/health`.
- **OpenTelemetry**: ASP.NET Core, HttpClient, and SqlClient instrumentation
  plus the `MediFlow.Worker` meter, exporting over OTLP only when
  `OTEL_EXPORTER_OTLP_ENDPOINT` is configured (compose/Azure); otherwise
  telemetry stays local and Serilog owns the console.
- **API-key options** bound from the `Api` configuration section.

`UseMediFlowWebService` applies the shared pipeline: exception handler, Serilog
request logging, baseline security headers (`X-Content-Type-Options`,
`X-Frame-Options`, `Referrer-Policy`), `ApiKeyMiddleware`, rate limiting, the
three health endpoints, and the OpenAPI/Scalar routes. `ApiKeyMiddleware`
rejects requests without a valid `X-Api-Key` using constant-time comparison
(`CryptographicOperations.FixedTimeEquals`) against the configured keys, with
`/health`, `/openapi`, and `/scalar` prefixes left anonymous so probes and
docs stay reachable. It is demo-grade by design; production replaces it with
OIDC, and the swap happens in exactly one file.

## Consequences

- Drift is impossible: a hardening or observability change lands in one place
  and applies to both APIs on the next build. A third API would be two lines in
  its `Program.cs`.
- `Infrastructure` gains ASP.NET Core references - accepted for a composition
  root that already owns the hosts' shared configuration surface. The worker,
  Blazor host, and MCP server keep their own lighter hosts: the worker adds
  Serilog and OTel metrics directly, and the MCP server must keep stdout empty
  because the stdio channel is its protocol.
- Service identity is a parameter, not a convention - each host passes its own
  name, which flows into logs, telemetry resources, and the Scalar page title.
