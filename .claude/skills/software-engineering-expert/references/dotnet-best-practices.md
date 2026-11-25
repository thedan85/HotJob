# .NET / ASP.NET Core / EF Core Best Practices

Use as a yardstick; always defer to the project's established conventions unless they are defective.

## C# language
- Enable nullable reference types; avoid `!` suppression without justification.
- Naming: PascalCase types/methods/properties, camelCase locals/params, `_camelCase` private fields, `I` prefix interfaces, `Async` suffix for async methods.
- Prefer `FirstOrDefault` + null check (or `Single` when exactly one is guaranteed) over `First` on uncertain data.
- Use `using`/`await using` for disposables; avoid exceptions for flow control.
- Prefer records/immutable DTOs for data carriers; don't expose mutable collections.

## Async
- Async all the way; never `.Result`, `.Wait()`, `.GetAwaiter().GetResult()` in request paths.
- No `async void` except event handlers. Don't fire-and-forget without a hosted/background mechanism.
- Accept and propagate `CancellationToken` in I/O methods.
- Don't wrap sync CPU work in `Task.Run` inside controllers.

## Dependency injection
- Lifetimes: Transient (stateless, light), Scoped (per request, DbContext), Singleton (thread-safe, stateless/cached). Never inject Scoped into Singleton (use `IServiceScopeFactory`).
- Register via interfaces; no `new` for services; avoid service locator (`GetService` in business code).
- Use `IOptions<T>` for configuration; don't read `IConfiguration` all over the code.
- `HttpClient` via `IHttpClientFactory`.

## ASP.NET Core
- Controllers thin: bind, validate, call service, map result. Business logic in services.
- Web API: `[ApiController]`, correct status codes (400/401/403/404/409/422), `ProblemDetails`, DTOs instead of entities, pagination for lists.
- MVC: ViewModels, `[ValidateAntiForgeryToken]` on state-changing actions, `ModelState.IsValid` checks, PRG pattern.
- Global exception handling middleware/`IExceptionHandler`; no stack traces to clients outside Development.
- Middleware order: exception handling, HTTPS redirection, static files, routing, CORS, authentication, authorization, endpoints.
- Logging with `ILogger<T>` and structured templates; no secrets/PII in logs.

## Entity Framework Core
- DbContext is Scoped; not shared across threads; not held in long-lived objects.
- Read-only queries: `AsNoTracking()`; project with `Select` to DTOs instead of loading whole graphs.
- Avoid N+1: use `Include`/`ThenInclude`, projection, or split queries; never query inside loops.
- Filter on the server: keep `IQueryable` until `Where`/`OrderBy`/`Skip`/`Take` are applied, then materialize.
- Use `Any()` not `Count() > 0`; paginate with stable ordering.
- Fluent configuration for keys, lengths, required, indexes, unique constraints, delete behavior (avoid accidental cascades).
- Wrap multi-step writes in one `SaveChanges` or an explicit transaction; use concurrency tokens (`RowVersion`) for contested rows.
- Avoid raw SQL string concatenation; use `FromSqlInterpolated`/parameters.
- Migrations: small, reviewed, one concern each; never edit applied migrations; keep model snapshot consistent.
- Index columns used in WHERE/JOIN/ORDER BY; unique index for natural keys (email, code).

## Architecture
- Layered/Clean: dependencies point inward (Presentation → Application → Domain; Infrastructure implements Application interfaces). Domain has no framework/EF dependencies in strict Clean Architecture.
- Repository/Unit of Work only if the project uses it; don't add a repository layer over EF just by habit.
- Keep DTOs/ViewModels separate from entities; map explicitly (or with the project's mapper).
- Configuration per environment; secrets via user-secrets/env vars/key vault, never committed.

## Frontend/backend integration
- Contract (routes, DTO shapes, status codes) agreed and consistent; camelCase JSON default.
- CORS restricted to known origins; credentials/cookie settings explicit.
- Handle loading/error/empty states; never trust client-side validation alone.
