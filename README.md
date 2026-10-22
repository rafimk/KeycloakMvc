# .NET MVC + Keycloak Docker sample

## Run

Install Docker Desktop with Linux containers, then run from this folder:

```powershell
docker compose up -d --build
```

Open http://localhost:5000. Visitors see a sign-in page with two choices: **Login with Logged-in User** (Keycloak single sign-on) and **Login with Username and Password** (Active Directory). After signing in, the application home page opens, or the page originally requested. Signing out returns to the sign-in page. See [Hybrid authentication](#hybrid-authentication).

- Demo username: `demo`
- Demo password: `Demo123!`
- Username and password login (local AD test domain **Lab**): `alice` / `Alice!Lab2026` (see [Local test domain](#active-directory-login-with-username-and-password))
- Kerberos SSO through Keycloak against the lab domain: `docker compose run --rm kerberos-client` (see [Kerberos SSO with the lab domain](#kerberos-sso-with-the-lab-domain))
- Not permitted, to test **Not authorized**: `guest` / `Guest123!` (Keycloak), `dave` / `Dave!Lab2026` (AD)
- Health: http://localhost:5000/health and http://localhost:5000/health/ready
- Keycloak admin console: http://localhost:8080/admin
- Admin username: `admin`
- Admin password: `admin-local-password`

The protected `/Account/Profile` page displays identity claims. For Keycloak sign-ins, sign out ends both the MVC cookie session and the Keycloak session. For Active Directory sign-ins, it ends only the MVC session. Logout uses a POST with antiforgery validation.

`python scripts/smoke_test.py` checks the running sample end to end.

## Run MVC locally

With the .NET 10 SDK installed:

```powershell
docker compose up -d keycloak
dotnet run --project KeycloakMvc --no-launch-profile --urls http://localhost:5000
```

Set `ASPNETCORE_ENVIRONMENT=Development` before running locally (PowerShell: `$env:ASPNETCORE_ENVIRONMENT="Development"`).

When the app runs this way, outside Docker, `dc1.lab.test` does not resolve, so username and password sign-in reports "temporarily unavailable". To test it, run the full Docker setup. Logs are written to the console and to `KeycloakMvc/logs/`.

## Configuration

Each settings section binds to an options class and is validated at startup. Invalid settings stop the app with a fatal log entry.

| Section | Options class | Purpose |
|---|---|---|
| `Keycloak` | `Authentication/Keycloak/KeycloakOptions.cs` | Realm URL, client, secret, optional `KerberosAmrValue` |
| `ActiveDirectory` | `Authentication/ActiveDirectory/ActiveDirectoryOptions.cs` | AD domains for username/password sign-in |
| `PermittedUsers` | `Authentication/PermittedUsers/PermittedUsersOptions.cs` | Users allowed to use the app after sign-in |
| `Login` | `Authentication/LoginOptions.cs` | `SessionTimeoutMinutes` (sliding, default 60), `PasswordAttemptsPerMinute` (default 5) |
| `FrameAncestors` | `Security/FrameAncestorsOptions.cs` | Origins allowed to embed the app in an iframe |
| `Serilog` | Serilog configuration | Log levels and sinks |

`appsettings.json` holds production defaults. `PermittedUsers` is empty there, so nobody is permitted until you list users. `KeycloakMvc/appsettings.Development.json` holds the local realm URL, client ID, demo secret, the lab AD domain and the permitted demo users. Docker overrides `Keycloak__MetadataAddress` to use the internal service address. Keycloak publishes the public issuer and browser URLs as `http://localhost:8080`, while dynamic backchannel URLs allow the MVC container to retrieve keys and redeem authorization codes internally. Issuer validation remains enabled.

`keycloak/mvc-sample-realm.json` imports the realm, confidential client, PKCE requirement and demo user on first startup. Redirect URLs are restricted to the sample's exact callback URLs. If you change the port or hostname, update the realm callbacks, Keycloak hostname and application authority together.

Realm imports skip an existing realm. To apply JSON changes to an existing setup, edit through the admin console or reset the sample volumes with `docker compose down -v` (this deletes sample users, sessions and persisted settings), then start again.

```powershell
docker compose logs -f
docker compose down
```

### Project structure

```text
KeycloakMvc/
├── Program.cs                     Composition only: calls the extension methods below
├── Authentication/
│   ├── AuthenticationExtensions.cs   AddAppAuthentication() / UseAppAuthentication()
│   ├── AuthenticationConstants.cs    Sign-in paths and the rate limit policy name
│   ├── AppClaimTypes.cs              Shared claim types and sign-in method names
│   ├── AppPrincipalFactory.cs        Builds the session principal for AD users
│   ├── AppCookieEvents.cs            Re-checks permitted users on every request
│   ├── LoginOptions.cs
│   ├── Keycloak/                     KeycloakOptions, OIDC events (claims, permitted users, failures)
│   ├── ActiveDirectory/              Options, validator, LDAPS connection factory, IActiveDirectoryService
│   └── PermittedUsers/               Options, validator, IPermittedUserService
├── HealthChecks/                  /health and /health/ready, Keycloak and AD checks
├── Logging/                       Serilog setup and request logging
├── Security/                      iframe framing header (CSP frame-ancestors)
├── Controllers/, Models/, Views/  MVC pages
```

## Hybrid authentication

```text
Anonymous request ──► /Account/SelectAuthentication?ReturnUrl=...
                         │                               │
     Login with Logged-in User (POST)        Login with Username and Password
                         │                               │
     Keycloak OIDC (code + PKCE)              /Account/Login (username, password, domain)
     Keycloak browser flow: Kerberos/SPNEGO   LDAPS bind to the selected AD domain
                         │                               │
                         └──────► MVC cookie session ◄───┘ ──► return URL or Home
```

- The cookie scheme is the default challenge, so `[Authorize]` and the global fallback policy send anonymous users to the selection page. Nothing redirects to Keycloak automatically.
- The two methods never fall back to each other. A failed Keycloak sign-in shows `/Account/LoginFailed`.
- Return URLs are used only when `Url.IsLocalUrl` accepts them. Anything else goes to `/`.
- Password attempts are limited to 5 per minute per client IP (`Program.cs`, policy `login`). Behind a reverse proxy, configure `ForwardedHeaders` so the limiter sees client addresses. Per-account lockout is left to Active Directory.

### Permitted users

Signing in proves who the user is. The permitted users list decides whether that user may use the application. The list is checked after a successful sign-in with either method, before a session is created:

```json
"PermittedUsers": {
  "Keycloak": [ "demo" ],
  "ActiveDirectory": [ "alice@lab.test", "bob@lab.test" ]
}
```

- **Keycloak** entries are Keycloak usernames (`preferred_username`). For Kerberos users, this is usually their Windows account name.
- **ActiveDirectory** entries are `username@dns.domain`, where the domain is the `Domain` of a configured AD domain. A user named `alice` in another domain does not match.
- Matching is case-insensitive. The two lists are separate, so the same name in Keycloak and in AD never grants access to the other provider.
- An empty list permits nobody.

A user who is not on the list sees **Not authorized** (`/Account/NotAuthorized`) with a **Back to sign-in options** button, and gets no session. For Keycloak users, the app also ends the Keycloak session, so they can sign in with another account. Every denial is logged as a warning.

The list is re-read when `appsettings.json` changes, and every request re-checks it. Removing a user therefore ends their session on their next request, without a restart. Environment variables (for example `PermittedUsers__Keycloak__0=demo`) are only read at startup.

Avoid adding users in `appsettings.json` and then overriding them in an environment file. Configuration merges arrays by index, so entries from the base file can survive. Keep each list in one place.

### Session claims

Both methods produce the same claim types (`KeycloakMvc/Authentication/AppClaimTypes.cs`):

| Claim | Keycloak | Active Directory |
|---|---|---|
| `preferred_username` | from the ID token | `sAMAccountName` |
| `name` | from the ID token | `displayName` |
| `app_user_id` | `keycloak\|<issuer>\|<sub>` | `ad\|<domain>\|<objectGUID>` |
| `app_auth_method` | `KeycloakKerberos` or `KeycloakSso` | `ActiveDirectory` |
| `app_auth_domain` | issuer URL | AD DNS domain |
| `role` | only if Keycloak sends a `role` claim | only from `RoleMappings` |

Use `app_user_id` as the stable user key. It includes the provider and domain, so a Keycloak user and an AD user never share an identity, and neither do `jsmith` in two domains. Linking them would need an explicit mapping policy, which this sample does not include. Signing in to AD grants no roles by itself.

### Keycloak Kerberos (Login with Logged-in User)

The app only starts the existing OIDC flow and validates Keycloak's tokens. Keycloak handles Kerberos, but only when its browser flow is configured for it:

1. Create a service principal `HTTP/<keycloak host>@REALM` and a keytab for it in AD (or your KDC).
2. In the realm, add **User federation → Kerberos** (or LDAP with Kerberos authentication enabled) with that principal and keytab.
3. In **Authentication**, set the **Kerberos** execution of the browser flow to *Alternative* (or *Required*). Alternatively, bind a copy of the flow to the `mvc-app` client under **Advanced → Authentication flow overrides**.
4. Configure browsers to send tickets to the Keycloak host. For example, add it to the Local intranet zone in Windows, or set the `AuthServerAllowlist` policy in Chrome and Edge.

#### Silent sign-in

With Kerberos configured, the user never sees the Keycloak login page:

```text
Sign-in page ── click "Login with Logged-in User" ──► Keycloak (/auth?...&code_challenge=...)
                                                         │  401 WWW-Authenticate: Negotiate
                                                         ▼
                              Browser sends the Windows Kerberos ticket automatically
                                                         │  Keycloak validates the ticket
                                                         ▼
                            /signin-oidc?code=... ──► MVC validates the tokens ──► return URL or Home
```

To the user it is one click and a short redirect. While the Keycloak SSO session is active, later sign-ins skip the Kerberos exchange too. The app needs no change for this. It is all Keycloak and browser configuration.

The flow is only silent when all of these are true:

- **Domain sign-in:** the computer is joined to the domain and the user signed in to Windows with a domain account. Windows Home editions cannot join a domain.
- **Browser trust:** the browser trusts the Keycloak host for Negotiate authentication (step 4 above). For Firefox, set `network.negotiate-auth.trusted-uris`.
- **Host name:** the browser reaches Keycloak by the host name in the service principal, such as `https://sso.corp.example.com` for `HTTP/sso.corp.example.com@CORP.EXAMPLE.COM`. Kerberos does not work with `localhost` or an IP address. To try it with this sample, see [Kerberos SSO with the lab domain](#kerberos-sso-with-the-lab-domain).

When Kerberos fails, for example because there is no ticket or the browser does not trust the host, Keycloak decides what happens next:

- **Kerberos set to *Alternative*:** Keycloak shows its username and password form.
- **Kerberos is the only option:** Keycloak shows an error.

The app never falls back to Active Directory on its own. The user can return to the selection page and choose **Login with Username and Password**. The same exchange works inside the iframe, with the cookie limits described in [Embedding in an iframe](#embedding-in-an-iframe).

#### Showing the sign-in method

Without Kerberos configured, Keycloak shows its normal login form and the app reports **Keycloak SSO**. By default the ID token does not say how the user authenticated. The app therefore reports **Keycloak Kerberos SSO** only when the `amr` claim contains the value in `Keycloak:KerberosAmrValue`. This requires a Keycloak version that supports authentication method references:

1. Set a reference value, for example `kerberos`, on the Kerberos execution.
2. Add the **Authentication Method Reference (AMR)** mapper to the client with **Add to ID token** turned on.
3. Set `Keycloak__KerberosAmrValue=kerberos`.

The sample realm and `appsettings.Development.json` already do all three. Keycloak drops the reference once its max age (`default.reference.maxAge`, in seconds) has passed. Without a max age, it is dropped whenever the code is redeemed in a later second than the sign-in, so the realm sets it to the SSO session length.

#### Kerberos SSO with the lab domain

The [local test domain](#active-directory-login-with-username-and-password) doubles as the Kerberos realm `LAB.TEST`. The sample realm `mvc-sample` is configured for it:

- **Service principal.** `samba/entrypoint.sh` creates the account `keycloak-svc` with the SPN `HTTP/keycloak.lab.test`, limited to AES. Without the AES limit, the KDC issues RC4 tickets, which Java rejects. On every start, it exports the keytab to the `samba-keytab` volume. Keycloak mounts the volume at `/etc/keycloak-kerberos`, with `kerberos/krb5.conf` as `/etc/krb5.conf`.
- **User federation.** The realm has an LDAP provider `lab.test`: AD vendor, read-only, `ldaps://dc1.lab.test` bound as `keycloak-svc`, with Kerberos authentication enabled. Keycloak trusts the lab CA through `KC_TRUSTSTORE_PATHS`. A realm import does not add the default LDAP mappers, so the realm lists them, including **MSAD account controls**, which keeps disabled AD accounts disabled.
- **Browser flow.** The realm uses `browser with kerberos`: Cookie, Kerberos and Identity Provider Redirector as alternatives, then the username and password form. Keycloak never switches the Kerberos step on by itself for imported providers.

Lab users can therefore also sign in to Keycloak with their AD password. `alice` is on the Keycloak permitted users list. `bob` signs in to Keycloak but sees **Not authorized** in the app.

Without a ticket, Keycloak now answers the login request with `401` and `WWW-Authenticate: Negotiate`. The page submits itself and continues to the password form, so in a browser it is one short extra redirect.

**Automated check.** The `kerberos-client` service is not started by `up`. Run it with:

```powershell
docker compose run --rm kerberos-client
```

It runs `kerberos/sso_test.sh` on the compose network, where Keycloak is also reachable as `keycloak.lab.test`. The script:

- confirms that Keycloak offers Negotiate when there is no ticket;
- runs `kinit alice` and `kinit bob`, then sends the app's authorization code + PKCE request with `curl --negotiate`;
- redeems the code and checks `preferred_username` and `amr: ["kerberos"]` in the ID token;
- confirms that the KDC gives disabled `carol` no ticket.

`scripts/smoke_test.py` runs it too.

**From a browser.** Your Windows computer is not joined to `lab.test`, so it needs a ticket from MIT Kerberos, and the browser must reach Keycloak as `keycloak.lab.test`. The steps below are a guide only: the automated check proves the Keycloak and Samba side, but this sample has not tested a browser on Windows.

1. Create `.env` next to `compose.yaml` with `KEYCLOAK_URL=http://keycloak.lab.test:8080`, then run `docker compose up -d --force-recreate`. This sets Keycloak's `KC_HOSTNAME` and the app's `Keycloak__Authority`. Keep the setting in `.env`: running a compose command without it, such as `docker compose run kerberos-client`, recreates Keycloak with `localhost`. Restart `mvc` after Keycloak changes host name, because the app caches the discovery document.
2. As administrator, add `127.0.0.1 keycloak.lab.test dc1.lab.test` to `C:\Windows\System32\drivers\etc\hosts`. The `samba` service publishes the KDC on `127.0.0.1:88` (TCP).
3. Install MIT Kerberos for Windows. Copy the `[libdefaults]`, `[realms]` and `[domain_realm]` sections of `kerberos/krb5.conf` into `C:\ProgramData\MIT\Kerberos5\krb5.ini`, then run `kinit alice@LAB.TEST` with password `Alice!Lab2026`.
4. In Firefox `about:config`, set `network.negotiate-auth.trusted-uris` to `keycloak.lab.test` and `network.auth.use_sspi` to `false`, so Firefox uses the MIT ticket instead of Windows sign-in.
5. Open http://localhost:5000 and choose **Login with Logged-in User**. The home page should show **Keycloak Kerberos SSO** for Alice Anderson without a password prompt.

Remove `.env` and run `docker compose up -d --force-recreate` to go back to `localhost`.

### Active Directory (Login with Username and Password)

Domains are configured under `ActiveDirectory:Domains` in `KeycloakMvc/appsettings.json`. The values there are examples and do not resolve:

| Setting | Meaning |
|---|---|
| `Name` | Label in the domain list |
| `Domain` | DNS domain, for example `corp.example.com` |
| `NetBiosName` | Optional, so users can type `CORP\jsmith` |
| `Server` | DC host name; must match the LDAPS certificate |
| `Port` / `UseSsl` | `636` / `true`. Startup fails if `UseSsl` is `false` |
| `SearchBase` | Optional; defaults to the domain root |
| `TimeoutSeconds` | 1–60, default 10 |
| `RoleMappings` | `[{ "Group": "CN=App Admins,OU=Groups,DC=corp,DC=example,DC=com", "Role": "Admin" }]`, matched against the user's direct `memberOf` entries |

Startup fails on invalid or duplicate settings. An empty `Domains` list disables the password option.

`ActiveDirectoryService` uses `System.DirectoryServices.Protocols`. It works as follows:

- **Bind.** It performs an LDAPS simple bind as `user@domain` against the server configured for the selected domain. Users cannot supply a server, and the app never tries another domain.
- **Usernames.** It accepts `jsmith`, `jsmith@corp.example.com` and `CORP\jsmith`, but only when the domain part matches the selected domain.
- **Account check.** Over the same connection, it reads the user's `objectGUID`, `displayName`, `userAccountControl` and `memberOf`, and rejects disabled accounts. No service account is needed.
- **Empty passwords** are rejected, because an LDAP bind with an empty password is an anonymous bind.
- **Passwords** are never logged, stored, or put into claims.
- **Errors.** Users see only "The username, password or domain is incorrect" or "Sign-in is temporarily unavailable". AD's reason code (`52e`, `533`, `775`, and so on) is written to the log.
- **Async.** The bind is synchronous because the library has no asynchronous bind. The user lookup is asynchronous.

**Certificate trust.** The LDAPS certificate is always validated; validation is never disabled. On Windows, the issuing CA must be in the machine's trusted root store. In the Linux container, `libldap` uses the system CA bundle, so add your CA to the image:

```dockerfile
COPY corp-root-ca.crt /usr/local/share/ca-certificates/
RUN update-ca-certificates
```

The Dockerfile installs `libldap2`, which `System.DirectoryServices.Protocols` needs on Linux.

**Local test domain.** `docker compose up` also starts `samba`, a Samba 4 AD domain controller built from `samba/`. It provisions the domain `lab.test` (NetBIOS `LAB`, DC `dc1.lab.test`) and its own CA for LDAPS. `appsettings.Development.json` replaces the first sample domain with it, and the `mvc` service trusts the lab CA through `LDAPTLS_CACERT`. Select **Lab (lab.test)** on the login page:

| Username | Password | Result |
|---|---|---|
| `alice` | `Alice!Lab2026` | Signs in. Member of `App Admins`, mapped to the `Admin` role |
| `bob` | `Bob!Lab2026` | Signs in with no roles |
| `carol` | `Carol!Lab2026` | Rejected: account disabled |
| `dave` | `Dave!Lab2026` | Valid password but not a permitted user: **Not authorized** |

The Keycloak realm also has `guest` / `Guest123!`. That user is not permitted either, so it sees **Not authorized**. If your Keycloak volume predates this user, add it in the admin console or run `docker compose down -v`.

Three wrong passwords lock an account for 5 minutes. `LAB\alice` and `alice@lab.test` also work. **Operations (ops.example.com)** stays unreachable, to show the "temporarily unavailable" message. The domain administrator is `Administrator` / `Admin!Lab2026`. The domain is provisioned again whenever the container is recreated, which resets users and lockouts.

The lab container needs the `SYS_ADMIN` capability because Samba stores sysvol ACLs in extended attributes. It is a test fixture only; don't run it in production.

**Using your real Active Directory.** Make these changes:

1. Remove the `samba` and `kerberos-client` services, the `samba-ca` and `samba-keytab` volumes, `LDAPTLS_CACERT`, `KC_TRUSTSTORE_PATHS` and the Kerberos mounts from `compose.yaml`. Then point the realm's `lab.test` user federation at your domain, or remove it, and use your own service principal and keytab.
2. Set your domains under `ActiveDirectory` and your users under `PermittedUsers`, in `appsettings.json` or an environment-specific file.
3. Trust your CA as described above. An MIT Kerberos KDC or a plain OpenLDAP server can't stand in for AD. Neither provides AD's `user@domain` bind, `sAMAccountName`, `objectGUID` or `userAccountControl`.

## Logging

Logging uses [Serilog](https://serilog.net/), configured entirely in the `Serilog` section of `appsettings.json`:

- **Console:** short lines (`docker compose logs -f mvc`).
- **File:** `logs/keycloakmvc-YYYYMMDD.log`, rolled daily and kept for 14 days. In the container this is `/app/logs`.
- **Requests:** one line per request, with method, path, status and duration. The query string is never logged, because it can carry OIDC authorization codes. Health probes are logged at Verbose and static files at Debug, so they stay out of the default output.
- **Startup failures,** such as invalid settings, are logged as Fatal before the app exits.

Passwords, tokens and authorization codes are never logged. Sign-in outcomes are logged by username and domain, including AD's reason code for rejected passwords.

To change verbosity, edit `Serilog:MinimumLevel`, for example `"Override": { "KeycloakMvc": "Debug" }`. To add sinks such as Seq, Elasticsearch or JSON files, add the sink package and an entry under `WriteTo`.

## Health checks

| Endpoint | Checks | Use |
|---|---|---|
| `/health` | The process answers | Liveness probe |
| `/health/ready` | Keycloak's discovery document. An anonymous LDAPS rootDSE read on each AD domain, which also validates the certificate | Readiness probe, monitoring |

Both endpoints are anonymous and return JSON:

```json
{"status":"Degraded","durationMs":49,"checks":[
  {"name":"keycloak","status":"Healthy","description":"Keycloak is reachable.", ...},
  {"name":"active-directory","status":"Degraded","description":"Unreachable: ops.example.com.",
   "data":{"lab.test":"reachable","ops.example.com":"unreachable"}}]}
```

An unreachable AD domain reports **Degraded** (HTTP 200), because the other sign-in methods still work. An unreachable Keycloak reports **Unhealthy** (HTTP 503). Exception details are logged, not returned.

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

This sample uses HTTP, Keycloak development mode, its local development database, a Samba lab domain controller, and committed demo credentials. For production, use HTTPS, secure cookie defaults, an external supported database, secret management, production Keycloak startup and persistent protected data-protection keys. Enable `Keycloak__RequireHttpsMetadata=true` and supply the production authority and secret.

## References

- [Microsoft: OpenID Connect web authentication](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/configure-oidc-web-authentication?view=aspnetcore-10.0)
- [Keycloak container setup](https://www.keycloak.org/server/containers)
- [Keycloak hostname and backchannel configuration](https://www.keycloak.org/server/hostname)

## Verification

Build: `dotnet build KeycloakMvc`. With the Docker services running and Python 3 available, run `python scripts/smoke_test.py`. It checks:

- `/health` and `/health/ready`, including per-domain AD status.
- Anonymous users see the selection page and keep their return URL.
- Users who are not permitted (`dave` in AD, `guest` in Keycloak) see **Not authorized** and get no session. The Keycloak user's Keycloak session is ended.
- The login form validates input and rejects missing antiforgery tokens.
- Active Directory sign-in against `lab.test`: success, wrong password, disabled account, wrong domain prefix, role mapping, and local-only logout.
- Keycloak login with PKCE, claims, open-redirect protection, Keycloak logout, and login rate limiting.
- LDAP user federation: `alice` signs in to Keycloak with her AD password; disabled `carol` is refused.
- Kerberos SSO through Keycloak, with the `kerberos-client` container (see [Kerberos SSO with the lab domain](#kerberos-sso-with-the-lab-domain)).

A Windows browser that uses Kerberos is not covered. That needs a client set up as described in that section.

