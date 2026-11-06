# Authentication

The API uses ASP.NET Core Identity's first-party opaque bearer tokens. There is no public registration or cookie sign-in. Provision the initial owner through the environment configuration in the [README](../README.md).

| Route | Request | Successful response |
| --- | --- | --- |
| `POST /api/v1/auth/login` | `{ "email": "you@example.com", "password": "..." }` | Token response |
| `POST /api/v1/auth/refresh` | `{ "refreshToken": "..." }` | Token response |
| `GET /api/v1/auth/me` | `Authorization: Bearer <accessToken>` | `{ "id": "<stable account UUID>", "email": "you@example.com" }` |

The token response contains `tokenType` (`Bearer`), `accessToken`, `expiresIn` (900 seconds) and `refreshToken`. Access tokens last 15 minutes; refresh tokens last 14 days. Refresh issues both tokens again. Tokens are opaque: clients must not parse them or assume JWT/OIDC semantics. The exported [OpenAPI contract](../contracts/openapi.json) declares request bodies, responses and bearer authorization for `/me`; MF-13 owns adopting this contract in the generated Android client, and MF-14 owns native sign-in.

Failures use the shared [API problem contract](api-errors.md). Incorrect credentials, unknown accounts, locked accounts and invalid/expired tokens return generic `401 authentication_required`. Invalid bodies return `400 invalid_request` or `400 validation_failed`. Login accepts existing passwords without applying creation-time complexity rules. After five failed password attempts an account is locked for five minutes. Login is limited to 10 requests per minute per remote IP; refresh to 30, with no queue. Throttling returns `429 rate_limited` and `Retry-After` in seconds. Limits are local to each API process. Forwarded headers are currently ignored; behind a proxy, its address shares a bucket. MF-43/MF-45 must configure explicitly trusted proxies and verify deployed limits.

## Persistent protected keys

Production startup requires `DataProtection:KeysPath` and `DataProtection:CertificatePath`. `DataProtection:CertificatePassword` unlocks the encrypted PFX. Compose maps these to a persistent `protection-keys` volume at `/var/lib/muscleforge/keys` and a read-only certificate at `/run/secrets/dataprotection.pfx`. The application name is fixed as `MuscleForge`. Key XML is encrypted with the certificate; the API runs as the image's non-root user.

Create the local certificate once before starting Compose:

```bash
mkdir -p .secrets
chmod 700 .secrets
umask 077
openssl req -x509 -newkey rsa:3072 -nodes -keyout .secrets/key.pem -out .secrets/cert.pem -days 3650 -subj '/CN=MuscleForge key protection'
openssl pkcs12 -export -out .secrets/dataprotection.pfx -inkey .secrets/key.pem -in .secrets/cert.pem
# Enter an export password, then set the same value in .env as DATA_PROTECTION_CERTIFICATE_PASSWORD.
rm .secrets/key.pem .secrets/cert.pem
chmod 644 .secrets/dataprotection.pfx
```

The encrypted PFX must be readable by the non-root container user; the restricted parent directory protects it from other host users. `.env` and `.secrets/` are excluded from Git and the Docker build context. `DATA_PROTECTION_CERTIFICATE_FILE` can override the host PFX path. This certificate protects stored keys and is separate from the deployment's HTTPS certificate.

Preserve and back up the key volume, PFX and password together. Do not regenerate them on each restart. Losing any of them can invalidate existing sessions. `docker compose down -v` deletes both database and protection-key volumes. Certificate rotation requires retaining the material needed to decrypt existing keys; automatic certificate rollover is not implemented. Development and tests without a configured key path use ephemeral keys, so their tokens intentionally do not survive host restarts. Configure the key path and certificate when persistence is needed there too. The OpenAPI exporter explicitly clears owner and protection-key configuration and uses an isolated Development host.

## Transport and session boundaries

Use HTTPS at the deployment boundary for all authentication and authenticated requests. Production must place the API behind the TLS proxy described in MF-43 and keep HTTP private; Compose's loopback HTTP binding is local development only. This increment does not deploy or verify that proxy. Native clients must use HTTPS outside explicit local development, retain access tokens in memory and refresh tokens in platform-protected storage, and clear credentials on logout (MF-14).

Resetting an account's security stamp invalidates its refresh tokens. Already-issued access tokens remain valid until their normal expiry. Local logout removes the client's credentials; it does not revoke a token copied elsewhere. There is no per-device revocation, single-use refresh guarantee, custom refresh replay detection or third-party OIDC authorization server. A valid refresh token may be reused until expiry or stamp invalidation. These are the framework's first-party token semantics; see the pinned [Identity endpoint implementation](https://github.com/dotnet/aspnetcore/blob/v10.0.14/src/Identity/Core/src/IdentityApiEndpointRouteBuilderExtensions.cs).
