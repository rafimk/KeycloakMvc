# .NET MVC + Keycloak Docker sample

## Run

Install Docker Desktop with Linux containers, then run from this folder:

```powershell
docker compose up -d --build
```

Open http://localhost:5000 and choose **Sign in with Keycloak**.

- Demo username: `demo`
- Demo password: `Demo123!`
- Keycloak admin console: http://localhost:8080/admin
- Admin username: `admin`
- Admin password: `admin-local-password`

The protected `/Account/Profile` page displays identity claims. Sign out ends both the MVC cookie session and the Keycloak session. Logout uses a POST with antiforgery validation.

## Run MVC locally

With the .NET 10 SDK installed:

```powershell
docker compose up -d keycloak
dotnet run --project KeycloakMvc --no-launch-profile --urls http://localhost:5000
```

Set `ASPNETCORE_ENVIRONMENT=Development` before running locally (PowerShell: `$env:ASPNETCORE_ENVIRONMENT="Development"`).

## Configuration

`KeycloakMvc/appsettings.Development.json` holds the local realm URL, client ID and demo secret. Docker overrides `Keycloak__MetadataAddress` to use the internal service address. Keycloak publishes the public issuer and browser URLs as `http://localhost:8080`, while dynamic backchannel URLs allow the MVC container to retrieve keys and redeem authorization codes internally. Issuer validation remains enabled.

`keycloak/mvc-sample-realm.json` imports the realm, confidential client, PKCE requirement and demo user on first startup. Redirect URLs are restricted to the sample's exact callback URLs. If you change the port or hostname, update the realm callbacks, Keycloak hostname and application authority together.

Realm imports skip an existing realm. To apply JSON changes to an existing setup, edit through the admin console or reset the sample volumes with `docker compose down -v` (this deletes sample users, sessions and persisted settings), then start again.

```powershell
docker compose logs -f
docker compose down
```

## Development only

This sample uses HTTP, Keycloak development mode, its local development database, and committed demo credentials. For production, use HTTPS, secure cookie defaults, an external supported database, secret management, production Keycloak startup and persistent protected data-protection keys. Enable `Keycloak__RequireHttpsMetadata=true` and supply the production authority and secret.

## References

- [Microsoft: OpenID Connect web authentication](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/configure-oidc-web-authentication?view=aspnetcore-10.0)
- [Keycloak container setup](https://www.keycloak.org/server/containers)
- [Keycloak hostname and backchannel configuration](https://www.keycloak.org/server/hostname)

## Verification

Build: `dotnet build KeycloakMvc`. With Docker services running and Python 3 available, run `python scripts/smoke_test.py` to verify the full login/logout flow.

