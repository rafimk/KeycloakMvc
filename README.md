# .NET MVC + Keycloak Docker sample

## Run

Install Docker Desktop with Linux containers, then run from this folder:

```powershell
docker compose up -d --build
```

Open http://localhost:5000. Visitors are automatically redirected to the Keycloak login page. After signing in, the application home page opens. Signing out returns to the login page.

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

## Embedding in an iframe

`iframe-test/index.html` is a host page that loads the MVC app in an iframe. Serve it from a different origin than the app:

```powershell
python -m http.server 5500 --bind 127.0.0.1 --directory iframe-test
```

Open http://localhost:5500 and sign in inside the frame.

By default, browsers refuse to show the app or the Keycloak login inside another origin's iframe ("localhost refused to connect"). The cause is the framing headers `X-Frame-Options: SAMEORIGIN` and `Content-Security-Policy: frame-ancestors 'self'`, not cookies. Browsers treat different ports on `localhost` as the same site, so cookies still work. The sample allows the host origin `http://localhost:5500` as follows:

1. **Keycloak login page.** `keycloak/mvc-sample-realm.json` sets the realm's `browserSecurityHeaders`:
   - `contentSecurityPolicy` is `frame-src 'self'; frame-ancestors 'self' http://localhost:5500; object-src 'none';`
   - `xFrameOptions` is empty, which removes the header.

2. **Apply to an existing realm.** Realm import skips an existing realm, so update the running realm either in the admin console or with the admin CLI.

   **Admin console:**
   1. Open http://localhost:8080/admin and sign in as `admin` / `admin-local-password`.
   2. Select the **mvc-sample** realm from the realm dropdown at the top left.
   3. Go to **Realm settings** → **Security defenses** → **Headers**.
   4. Clear **X-Frame-Options**.
   5. Set **Content-Security-Policy** to `frame-src 'self'; frame-ancestors 'self' http://localhost:5500; object-src 'none';`
   6. Click **Save**. The change takes effect immediately.

   To allow more host pages, add their origins after `'self'`, separated by spaces. Console changes are stored only in Keycloak's database, so keep `mvc-sample-realm.json` in sync.

   **Admin CLI (Git Bash):**

   ```bash
   MSYS_NO_PATHCONV=1 docker compose exec -T keycloak bash -c '
     /opt/keycloak/bin/kcadm.sh config credentials --server http://localhost:8080 --realm master --user admin --password admin-local-password &&
     /opt/keycloak/bin/kcadm.sh update realms/mvc-sample \
       -s "browserSecurityHeaders.contentSecurityPolicy=frame-src '"'"'self'"'"'; frame-ancestors '"'"'self'"'"' http://localhost:5500; object-src '"'"'none'"'"';" \
       -s "browserSecurityHeaders.xFrameOptions="'
   ```

   As an alternative, run `docker compose down -v` and restart to re-import the realm. This deletes sample data.
3. **MVC pages.** ASP.NET Core antiforgery adds `X-Frame-Options: SAMEORIGIN` to pages that render forms, such as the logout form. In `KeycloakMvc/Program.cs`:
   - `AddAntiforgery(o => o.SuppressXFrameOptionsHeader = true)` removes that header.
   - A middleware sends `Content-Security-Policy: frame-ancestors 'self' <FrameAncestors>`.
4. **Allowed host origins.** List them in `FrameAncestors` in `KeycloakMvc/appsettings.Development.json`:

   ```json
   "FrameAncestors": [ "http://localhost:5500" ]
   ```

   For Docker you can also set them as environment variables, for example `FrameAncestors__0=http://localhost:5500`.
5. **Rebuild the app.** Run `docker compose up -d --build mvc`.
6. **Host page.** The page that embeds the app needs no special configuration. A plain iframe is enough:

   ```html
   <iframe src="http://localhost:5000/" style="width: 100%; height: 600px; border: 0"></iframe>
   ```

   If you add a `sandbox` attribute, include at least `allow-scripts allow-forms allow-same-origin`. Without these, the login form cannot submit and cookies are not sent.

To use a different host origin, change it in both the realm CSP and `FrameAncestors`. Allow only trusted origins, because framing the login page weakens clickjacking protection. Cross-site embedding on a different domain additionally requires HTTPS and `SameSite=None; Secure` cookies, and browsers that block third-party cookies may still break it. Prefer a popup or top-level login in that case.

### Red Hat SSO

The Red Hat build of Keycloak works like this sample, so the steps above apply unchanged.

Red Hat Single Sign-On 7.x uses the same realm headers and the same MVC changes, with these differences:

| What | This sample (Keycloak 26) | RH-SSO 7.x |
|---|---|---|
| Admin console | `http://host:8080/admin` | `http://host:8080/auth/admin` |
| Realm URL / MVC `Keycloak:Authority` | `http://host:8080/realms/mvc-sample` | `http://host:8080/auth/realms/mvc-sample` |
| `kcadm.sh` server URL | `http://localhost:8080` | `http://localhost:8080/auth` |
| `kcadm.sh` path | `/opt/keycloak/bin/kcadm.sh` | `$RHSSO_HOME/bin/kcadm.sh` (OpenShift image: `/opt/eap/bin/kcadm.sh`) |
| Realm JSON import | `--import-realm` on startup | Admin console **Add realm → Import**, or the `keycloak.import` system property |
| Hostname settings | `KC_HOSTNAME` environment variables | Server configuration (`standalone.xml` or the operator) |

The `browserSecurityHeaders` block in `mvc-sample-realm.json` uses the same keys in RH-SSO.

**RH-SSO admin console:**
1. Open `http://host:8080/auth/admin` and sign in as an administrator.
2. Hover over the realm name at the top left and select **mvc-sample**.
3. In the left menu under **Configure**, click **Realm Settings**.
4. Open the **Security Defenses** tab, then the **Headers** sub-tab.
5. Clear **X-Frame-Options**.
6. Set **Content-Security-Policy** to `frame-src 'self'; frame-ancestors 'self' http://localhost:5500; object-src 'none';`
7. Click **Save**.

**RH-SSO admin CLI:**

```bash
$RHSSO_HOME/bin/kcadm.sh config credentials --server http://localhost:8080/auth --realm master --user admin --password <password>
$RHSSO_HOME/bin/kcadm.sh update realms/mvc-sample \
  -s "browserSecurityHeaders.contentSecurityPolicy=frame-src 'self'; frame-ancestors 'self' http://localhost:5500; object-src 'none';" \
  -s "browserSecurityHeaders.xFrameOptions="
```

In the MVC app, point `Keycloak:Authority` (and `Keycloak:MetadataAddress`, if set) at the `/auth/realms/...` URL. `FrameAncestors` and the antiforgery change stay the same. Real RH-SSO deployments usually run on HTTPS and on a different domain from the host page. Embedding then also depends on the browser allowing third-party cookies, so a popup or top-level login is more reliable.

## Development only

This sample uses HTTP, Keycloak development mode, its local development database, and committed demo credentials. For production, use HTTPS, secure cookie defaults, an external supported database, secret management, production Keycloak startup and persistent protected data-protection keys. Enable `Keycloak__RequireHttpsMetadata=true` and supply the production authority and secret.

## References

- [Microsoft: OpenID Connect web authentication](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/configure-oidc-web-authentication?view=aspnetcore-10.0)
- [Keycloak container setup](https://www.keycloak.org/server/containers)
- [Keycloak hostname and backchannel configuration](https://www.keycloak.org/server/hostname)

## Verification

Build: `dotnet build KeycloakMvc`. With Docker services running and Python 3 available, run `python scripts/smoke_test.py` to verify the full login/logout flow.

