# API validation, errors and diagnostics

Application endpoints use `/api/v1` JSON DTOs. They do not serialize EF entities. MF-9 supplies the common pipeline and response contract; login, ownership queries and workout mutation logic are implemented by their feature tickets.

## Error envelope

Errors use `Content-Type: application/problem+json`, including when the request's `Accept` header asks for another format. The server returns `type`, `title`, HTTP `status`, stable machine-readable `code`, and an opaque server `traceId`. Validation errors also contain an `errors` dictionary keyed by JSON property name. Clients branch on status/code rather than parsing titles or validation messages.

For example, an unauthenticated request returns HTTP 401 without a browser redirect:

```json
{
  "type": "about:blank",
  "title": "Authentication required",
  "status": 401,
  "code": "authentication_required",
  "traceId": "example-trace-id"
}
```

An invalid field returns HTTP 400:

```json
{
  "type": "about:blank",
  "title": "Validation failed",
  "status": 400,
  "code": "validation_failed",
  "traceId": "example-trace-id",
  "errors": { "name": ["Name is required."] }
}
```

The same envelope represents missing resources, stale versions and oversized bodies:

```json
{"type":"about:blank","title":"Resource not found","status":404,"code":"resource_not_found","traceId":"example-trace-id"}
```

```json
{"type":"about:blank","title":"Conflict","status":409,"code":"version_conflict","traceId":"example-trace-id"}
```

```json
{"type":"about:blank","title":"Request too large","status":413,"code":"request_too_large","traceId":"example-trace-id"}
```

Generic failures never include exception text, stack traces, submitted field values, tokens or connection details. The trace ID connects a response to the safe completion log. Health endpoints retain their generic liveness/readiness output and HTTP 200/503 behavior.

## Stable codes and endpoint integration

| HTTP status | Code | Use |
| --- | --- | --- |
| 400 | `invalid_request` | Malformed JSON or another invalid binding/request |
| 400 | `validation_failed` | Explicit DTO/domain validation, with field errors |
| 401 | `authentication_required` | Missing/invalid authentication; no redirect |
| 403 | `access_denied` | General authorization failure |
| 404 | `resource_not_found` | Unknown resource or a resource owned by another account |
| 405 | `method_not_allowed` | Unsupported method for a route |
| 409 | `conflict` | Generic conflict |
| 409 | `version_conflict` | Stale expected version |
| 409 | `state_conflict` | A mutation conflicts with resource state |
| 413 | `request_too_large` | Request body exceeds the byte limit |
| 415 | `unsupported_media_type` | Unsupported request content type |
| 429 | `rate_limited` | Request throttled |
| 500 | `internal_error` | Unexpected failure with a generic public response |
| 503 | `service_unavailable` | Application endpoint unavailable |
| Other 4xx/5xx | `request_failed` | Fallback for an unlisted status |

Use `ApiErrors.Validation(errors)`, `ApiErrors.VersionConflict()`, `ApiErrors.StateConflict()`, or `ApiErrors.ForStatus(status)`. The last helper accepts only 400–599. Feature endpoints must scope resource lookups by the authenticated owner and use the same 404 for missing and foreign-owned resources. MF-12 and later resource tickets prove actual database ownership enforcement; the common contract alone does not implement those queries. MF-11 supplies the real bearer authentication endpoints and rate limits; MF-23 supplies version/state mutation checks and any additional conflict metadata.

Register services with `AddApiStandards()` and install `UseApiStandards()` after routing and before authentication/authorization or endpoint execution. When authentication is added, configure its middleware explicitly in this order so Minimal API's automatic auth middleware does not short-circuit before common error handling:

```csharp
app.UseRouting();
app.UseApiStandards();
app.UseAuthentication(); // When MF-11 registers authentication services.
app.UseAuthorization();
// Map endpoints after composing the pipeline.
```

The current public status endpoint preserves its 200 DTO and declares the shared 500 problem schema in OpenAPI. Future routes must declare their actual error response schemas with `Produces<ApiProblem>(...)`; validation opt-in adds the 400 `ApiValidationProblem` metadata. MF-13 owns authenticated/request-body client generation and broader exporter support. No diagnostic or sample error route is shipped in production.

## Validation and limits

DTO routes opt in with `.WithApiValidation<TRequest>()`. This applies top-level DataAnnotations and `IValidatableObject`, maps field keys to JSON names, and returns `validation_failed` on invalid input. Use fixed public messages; custom validators must not embed submitted values in messages. Nested objects, collection elements, domain invariants and referenced-resource ownership require explicit feature validation.

| Limit | Initial value | Source/ownership |
| --- | --- | --- |
| Name | 100 characters | `ApiStandards.MaxNameLength`, aliases the exercise domain constant |
| Equipment | 100 characters | `ApiStandards.MaxEquipmentLength`, aliases the exercise domain constant |
| Notes | 2,000 characters | `ApiStandards.MaxNotesLength` |
| Exercises per session | 50 | `ApiStandards.MaxExercisesPerSession` |
| Sets per exercise | 50 | `ApiStandards.MaxSetsPerExercise` |
| Request body | 262,144 bytes (256 KiB) | `ApiStandards.MaxRequestBodyBytes` |
| Transport body/framing | 2,097,152 bytes (2 MiB) | `ApiStandards.MaxTransportBodyBytes` |

The 256 KiB application cap counts decoded request-body bytes. A declared oversized body is rejected before endpoint execution. Unknown-length/chunked bodies are counted as they are consumed, without eager body reads or unbounded buffering. The stream limit also protects TestServer and .NET 10 `BodyReader`/JSON binding. Exactly 262,144 consumed body bytes are permitted; the next byte produces 413.

Kestrel 10 also counts HTTP/1 chunk framing toward its own body limit. Its separate 2 MiB transport limit permits ordinary chunk framing around a valid 256 KiB body while still bounding framing overhead. Pathologically large extensions/framing can therefore receive 413 even with fewer decoded bytes. The higher transport limit does not allow a larger decoded application body.

Name/equipment invariants already exist in the exercise domain. Future session/template DTOs must use the documented limits and enforce nested data within their own features; this foundation does not add workout endpoints or models. Native clients need no wildcard CORS configuration.

## Safe request completion logs

One completion event records `Route` (the route template, or `(unmatched)`), `StatusCode`, `DurationMs`, `TraceId`, and `ErrorCode`. It does not record raw paths, query strings, headers, bodies, passwords, tokens, workout notes or exception messages. Keep framework request diagnostics at their safe configured levels; do not enable HTTP body logging or EF sensitive-data logging for personal data.

Pass `HttpContext.RequestAborted` through asynchronous feature I/O. Caller cancellation is aborted without an internal-error response; an unstarted response is recorded internally as 499 with `request_cancelled`. If headers/body have already started, an error cannot be rewritten as JSON: the connection is aborted and the log records `response_aborted`. This avoids reporting an incomplete response as an ordinary successful request.

## Verification and references

HTTP tests exercise the shared pipeline through real Kestrel and TestServer, including JSON binding, streaming limits, opt-in validation, authentication challenges, cancellation and safe logs. Actual feature ownership/authentication/device acceptance remains with the paired feature tickets. MF-45 verifies the deployed proxy, health, rate-limit and logging configuration.

Primary framework references: [API error handling](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/error-handling-api?view=aspnetcore-10.0), [request/response body adapters](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/middleware/request-response?view=aspnetcore-10.0), and [Kestrel request limits](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/kestrel/options?view=aspnetcore-10.0).
