# User API — Design

**Date:** 2026-09-21
**Status:** Approved

## Purpose

A production-shaped ASP.NET Core Web API providing JWT authentication and user
management. The architecture demonstrates SOLID principles, dependency
injection, the repository pattern, and a service layer, with controllers kept
thin.

## Tech stack

| Concern | Choice |
|---|---|
| Framework | .NET 8 (`net8.0`), ASP.NET Core, controller-based |
| ORM | Entity Framework Core, code-first migrations |
| Database | PostgreSQL 17, run locally via Docker Compose |
| Provider | `Npgsql.EntityFrameworkCore.PostgreSQL` |
| Auth | JWT bearer tokens, custom `User` entity |
| Hashing | `PasswordHasher<User>` (PBKDF2, ASP.NET Core shared framework) |
| API docs | Swashbuckle, with a Bearer security definition |

PostgreSQL replaced an earlier SQLite choice. SQLite supports a single writer,
has no network protocol, and cannot scale horizontally, so it is unsuitable for
a web API behind a load balancer. Running the production engine locally also
gives dev/prod parity: migrations, SQL dialect, and type mapping behave
identically in both places.

### Packages to add

```
Npgsql.EntityFrameworkCore.PostgreSQL
Microsoft.EntityFrameworkCore.Design
Microsoft.AspNetCore.Authentication.JwtBearer
```

Swashbuckle and `Microsoft.AspNetCore.OpenApi` are already referenced.

## Architecture

Single project, layered by folder. SOLID is enforced through interfaces and
dependency injection rather than project boundaries.

```
ApiTemplate/
├── ApiTemplate.sln
├── docker-compose.yml
├── docs/superpowers/specs/
└── ApiTemplate/
    ├── Controllers/
    │   ├── AuthController.cs
    │   └── UsersController.cs
    ├── Services/
    │   ├── IAuthService.cs   / AuthService.cs
    │   ├── IUserService.cs   / UserService.cs
    │   ├── ITokenService.cs  / TokenService.cs
    │   └── ICurrentUser.cs   / CurrentUser.cs
    ├── Repositories/
    │   ├── IUserRepository.cs         / UserRepository.cs
    │   └── IRefreshTokenRepository.cs / RefreshTokenRepository.cs
    ├── Data/
    │   ├── AppDbContext.cs
    │   ├── Configurations/
    │   │   ├── UserConfiguration.cs
    │   │   └── RefreshTokenConfiguration.cs
    │   └── DbSeeder.cs
    ├── Models/
    │   ├── User.cs
    │   ├── RefreshToken.cs
    │   └── UserRole.cs
    ├── DTOs/
    │   ├── Auth/
    │   │   ├── RegisterRequest.cs
    │   │   ├── LoginRequest.cs
    │   │   ├── RefreshRequest.cs
    │   │   └── AuthResponse.cs
    │   └── Users/
    │       ├── UserDto.cs
    │       ├── CreateUserRequest.cs
    │       ├── UpdateUserRequest.cs
    │       └── PagedResult.cs
    ├── Options/
    │   └── JwtOptions.cs
    ├── Common/
    │   ├── Result.cs
    │   ├── ResultExtensions.cs
    │   └── Mapping/UserMappings.cs
    ├── Program.cs
    ├── appsettings.json
    └── appsettings.Development.json
```

### Dependency rule

```
Controller  →  Service  →  Repository  →  DbContext
```

Dependencies flow in one direction only, and every arrow crosses an interface.

| Layer | Responsibilities | Prohibited |
|---|---|---|
| Controller | Bind and validate DTOs, call one service method, map `Result` to HTTP | Touching `DbContext`, business rules, exposing entities |
| Service | Business rules, authorization decisions, hashing, orchestration | LINQ against `DbContext`, referencing `HttpContext` |
| Repository | Querying and persisting entities | Business rules, returning DTOs |

### Two deliberate choices

**`ICurrentUser`** abstracts the calling user's identity (`Id`, `IsAdmin`) as
read from JWT claims. Without it, services would depend on
`IHttpContextAccessor`, coupling business logic to the web layer. With it, a
service asks who is calling without knowing the answer came from HTTP.

**Entities never leave the service layer.** Controllers accept and return DTOs
only. This is what prevents `PasswordHash` from being serialised into a
response.

**Specific repositories, not a generic `IRepository<T>`.** A generic repository
over EF Core is a leaky abstraction — `DbSet<T>` is already a repository — and
forces consumers to depend on methods they do not use, violating interface
segregation.

## Data model

### `User`

| Field | Type | Constraints |
|---|---|---|
| `Id` | `Guid` | Primary key |
| `Email` | `string` | Required, unique index, max 255, stored lowercase |
| `PasswordHash` | `string` | Required |
| `DisplayName` | `string?` | Max 100 |
| `Role` | `UserRole` | Enum (`Admin`, `User`), stored as text |
| `CreatedAt` | `DateTime` | UTC |
| `UpdatedAt` | `DateTime?` | UTC |

### `RefreshToken`

| Field | Type | Constraints |
|---|---|---|
| `Id` | `Guid` | Primary key |
| `UserId` | `Guid` | Foreign key to `User`, cascade delete |
| `TokenHash` | `string` | SHA-256 of the raw token, unique index |
| `ExpiresAt` | `DateTime` | UTC |
| `RevokedAt` | `DateTime?` | UTC, null while active |
| `CreatedAt` | `DateTime` | UTC |

`IsActive` is computed: `RevokedAt is null && ExpiresAt > DateTime.UtcNow`.

### Data model decisions

**`Guid` primary keys, not `int`.** Sequential identifiers let a caller
enumerate `/api/users/1`, `/2`, `/3` and count the user base. Guid maps to a
native `uuid` column in PostgreSQL.

**Raw refresh tokens are never stored** — only their SHA-256 hash. A database
leak therefore yields unusable tokens. SHA-256 rather than PBKDF2 is correct
here: the token is 256 bits of CSPRNG output, so there is no dictionary to
attack, unlike a human-chosen password.

**Emails are normalised to lowercase on write.** PostgreSQL string comparison is
case-sensitive, so `Patrick@example.com` and `patrick@example.com` would
otherwise become two accounts. Lowercasing on write plus a unique index solves
this and stays portable, unlike the `citext` extension.

**All timestamps are UTC.** Npgsql maps `DateTime` to `timestamptz` and throws
at runtime when given a value whose `Kind` is `Local` or `Unspecified`. Use
`DateTime.UtcNow` exclusively.

## Endpoints

| Method | Route | Authorization | Success | Failures |
|---|---|---|---|---|
| POST | `/api/auth/register` | Anonymous | 201 `AuthResponse` | 400, 409 |
| POST | `/api/auth/login` | Anonymous | 200 `AuthResponse` | 400, 401 |
| POST | `/api/auth/refresh` | Anonymous | 200 `AuthResponse` | 401 |
| POST | `/api/auth/logout` | Authenticated | 204 | 401 |
| GET | `/api/users` | Admin | 200 `PagedResult<UserDto>` | 401, 403 |
| GET | `/api/users/me` | Authenticated | 200 `UserDto` | 401 |
| GET | `/api/users/{id}` | Admin or self | 200 `UserDto` | 401, 403, 404 |
| POST | `/api/users` | Admin | 201 `UserDto` | 400, 401, 403, 409 |
| PUT | `/api/users/{id}` | Admin or self | 200 `UserDto` | 400, 401, 403, 404 |
| DELETE | `/api/users/{id}` | Admin | 204 | 401, 403, 404 |

### Endpoint decisions

**`POST /api/users` is not redundant with `/api/auth/register`.** Register is
public self-signup and always creates `Role = User`; `RegisterRequest` has no
`Role` field, so nobody can sign up as an administrator. `POST /api/users` is
admin-only and may assign a role.

**Role escalation is blocked on update.** A user updating their own record
cannot change `Role`. Only an administrator may change it.

**`UpdateUserRequest` carries `DisplayName` and `Role` only.** Email is
immutable through this endpoint. Changing an email is an identity change that
needs verification of the new address, which is out of scope; allowing it here
would let a user silently take over an unverified address. `Role` is ignored
unless the caller is an administrator.

**An administrator cannot delete their own account.** `DELETE` returns 403 when
`id` equals the caller's id, preventing the last administrator from locking
everyone out of user management.

**Authorization is evaluated before the lookup.** A non-admin requesting another
user's id receives 403 whether or not that id exists. Returning 404 for missing
ids and 403 for real ones would let a caller enumerate valid accounts.

**`GET /api/users` is paged** via `?page=1&pageSize=20`, with `pageSize` capped
at 100. An unbounded list endpoint becomes a production problem as the table
grows.

## Service contract and error handling

Services return `Result<T>`. Expected failures — not found, conflict, forbidden
— are ordinary outcomes, not exceptions. Encoding them in the return type keeps
every failure path visible to the compiler and the reader, and avoids
stack-unwinding on routine paths.

```csharp
public async Task<Result<UserDto>> GetByIdAsync(Guid id, CancellationToken ct)
{
    if (!_currentUser.IsAdmin && _currentUser.Id != id)
        return Result<UserDto>.Forbidden("You can only access your own account.");

    var user = await _repository.GetByIdAsync(id, ct);

    return user is null
        ? Result<UserDto>.NotFound($"No user with id {id}.")
        : Result<UserDto>.Success(user.ToDto());
}
```

`Result<T>` carries a success flag, a value, an error kind
(`NotFound`, `Conflict`, `Forbidden`, `Validation`, `Unauthorized`), and a
message. A single `ResultExtensions.ToActionResult` maps the error kind to the
HTTP status code, so controllers remain one-liners:

```csharp
[HttpGet("{id:guid}")]
public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    => (await _userService.GetByIdAsync(id, ct)).ToActionResult(this);
```

### Error responses

Every error uses RFC 9457 `ProblemDetails`:

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.5",
  "title": "User not found",
  "status": 404,
  "detail": "No user with id 3f2a...",
  "traceId": "00-2677715bb7f9...-00"
}
```

`app.UseStatusCodePages()` is required so framework-generated 401 responses
carry a body. Without it they return zero bytes, which breaks any client that
attempts to deserialise the response.

**Validation** uses DataAnnotations on request DTOs. `[ApiController]` returns
400 with a `ValidationProblemDetails` listing every invalid field before the
action runs.

**Unexpected exceptions** are caught by a global handler that returns a bare 500
with a `traceId` and logs the detail server-side. Stack traces never reach the
client.

**Authentication failures are deliberately vague.** An unknown email and a wrong
password both return an identical `401 Invalid credentials`; distinguishing them
allows account enumeration.

## Authentication flow

### Token strategy

| Token | Lifetime | Storage |
|---|---|---|
| Access (JWT) | 15 minutes | Client memory or secure storage; not persisted server-side |
| Refresh | 30 days | Hashed in `refresh_tokens`; revocable |

Access token claims: `sub` (user id), `jti`, `email`, `role`, `exp`, `iss`,
`aud`.

**Refresh rotation.** Every call to `/api/auth/refresh` issues a new token pair
and revokes the presented token. If an already-revoked token is presented, all
refresh tokens for that user are revoked, since replay indicates theft. This is
the standard defence against refresh token compromise.

`/api/auth/logout` revokes the supplied refresh token and returns 204
regardless of whether it existed, so the response does not reveal token
validity.

### Token validation

```csharp
options.MapInboundClaims = false;

options.TokenValidationParameters = new TokenValidationParameters
{
    ValidateIssuer = true,
    ValidateAudience = true,
    ValidateLifetime = true,
    ValidateIssuerSigningKey = true,
    ValidIssuer = jwtOptions.Issuer,
    ValidAudience = jwtOptions.Audience,
    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.SigningKey)),
    ClockSkew = TimeSpan.Zero,
    RoleClaimType = "role",
    NameClaimType = "email"
};
```

`MapInboundClaims = false` is required. By default the handler rewrites `role`
into a Microsoft schema URI, after which `RoleClaimType = "role"` matches
nothing and every `[Authorize(Roles = "Admin")]` check returns 403 despite a
valid token.

`ClockSkew = TimeSpan.Zero` overrides a five-minute default grace period that
would otherwise keep expired tokens working past their `exp`.

## Configuration

`appsettings.json` holds non-secret values only:

```json
{
  "Jwt": {
    "Issuer": "ApiTemplate",
    "Audience": "ApiTemplate",
    "AccessTokenMinutes": 15,
    "RefreshTokenDays": 30
  },
  "Seed": {
    "AdminEmail": "admin@example.com"
  }
}
```

Secrets — the JWT signing key, the connection string (which contains a
password), and the seed admin password — come from User Secrets in development
and environment variables in production:

```
ConnectionStrings__DefaultConnection
Jwt__SigningKey
Seed__AdminPassword
```

`JwtOptions` is bound with `ValidateDataAnnotations().ValidateOnStart()`, so a
missing or too-short signing key (minimum 32 characters) prevents the
application from starting rather than failing on the first login.

## Database setup

`docker-compose.yml` at the solution root runs `postgres:17` with a named volume
for persistence, a `pg_isready` healthcheck, and port 5432 published so the API
can run from the IDE while the database runs in the container.

Schema is managed with **EF Core migrations**, not `EnsureCreated`. Migrations
are version-controlled, reviewable, and replayable against production.

Entity configuration lives in `IEntityTypeConfiguration<T>` classes rather than
an expanding `OnModelCreating`.

**Seeding.** On startup, if no user with `Role = Admin` exists, one is created
from `Seed:AdminEmail` and `Seed:AdminPassword`. Without this there is no way to
obtain the first administrator, because `/api/auth/register` can only create
regular users.

## Program.cs composition

```
Options       AddOptions<JwtOptions>().Bind(...).ValidateDataAnnotations().ValidateOnStart()
Data          AddDbContext<AppDbContext>(o => o.UseNpgsql(...))
Repositories  AddScoped<IUserRepository, UserRepository>
              AddScoped<IRefreshTokenRepository, RefreshTokenRepository>
Services      AddScoped<IAuthService, AuthService>
              AddScoped<IUserService, UserService>
              AddScoped<ITokenService, TokenService>
              AddScoped<ICurrentUser, CurrentUser>
              AddHttpContextAccessor()
              AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>
Auth          AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(...)
              AddAuthorization()
Docs          AddSwaggerGen(...)  with Bearer security definition
```

Pipeline order, which determines behaviour rather than style:

```csharp
app.UseStatusCodePages();
app.UseExceptionHandler();
app.UseHttpsRedirection();
app.UseAuthentication();   // establishes identity
app.UseAuthorization();    // evaluates [Authorize]
app.MapControllers();
```

`UseAuthentication` must precede `UseAuthorization`. Reversed, authorization
runs before any identity exists and every protected endpoint returns 401
regardless of token validity.

`IPasswordHasher<User>` is registered as a singleton because it is stateless and
thread-safe. Repositories and services are scoped to the request.

## Out of scope

- **Unit and integration tests.** Explicitly excluded. The architecture remains
  test-ready: every dependency is an interface, so tests can be added later
  without modifying production code.
- Email confirmation, password reset, and two-factor authentication.
- Rate limiting and account lockout.
- Containerising the API itself; only the database runs in Docker.
- CORS configuration, which depends on a frontend that does not yet exist.
