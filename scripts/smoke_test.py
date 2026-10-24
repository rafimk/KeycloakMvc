"""End-to-end local sample check. Run: python scripts/smoke_test.py"""
import html
import http.cookiejar
import json
import pathlib
import re
import subprocess
import urllib.error
import urllib.parse
import urllib.request

APP = "http://localhost:5000"
SELECT = f"{APP}/Account/SelectAuthentication"
KEYCLOAK = {"Primary": "http://localhost:8080/", "Secondary": "http://localhost:8081/"}
PRIMARY_LABEL = "تسجيل الدخول عبر Keycloak الأول"
SECONDARY_LABEL = "تسجيل الدخول عبر Keycloak الثاني"

# Browsers treat localhost as a secure context. Mirror that behavior for this
# localhost-only HTTP smoke test; Keycloak sets Secure cookies on localhost.
cookies = http.cookiejar.CookieJar(http.cookiejar.DefaultCookiePolicy(secure_protocols=("https", "http")))
browser = urllib.request.build_opener(urllib.request.HTTPCookieProcessor(cookies))

def get(url):
    return browser.open(url, timeout=30)

def post(url, fields):
    return browser.open(url, urllib.parse.urlencode(fields).encode(), timeout=30)

def antiforgery(body):
    return html.unescape(re.search(r'name="__RequestVerificationToken"[^>]*value="([^"]+)"', body).group(1))

def keycloak_login(instance, return_url):
    """Choose the instance's button on the sign-in page and return its Keycloak login page.

    Without a Kerberos ticket, the Primary instance answers 401 Negotiate with a page that submits itself
    to continue to the password form. Submit it, as a browser would."""
    page = get(f"{SELECT}?ReturnUrl={urllib.parse.quote(return_url)}").read().decode()
    try:
        response = post(f"{APP}/Account/LoginWithKeycloak", {"__RequestVerificationToken": antiforgery(page),
                                                             "instance": instance, "returnUrl": return_url})
    except urllib.error.HTTPError as error:
        assert instance == "Primary" and error.code == 401 and error.headers["WWW-Authenticate"] == "Negotiate", error
        action = html.unescape(re.search(r'ACTION="([^"]+)"', error.read().decode()).group(1))
        response = post(action, {})
    assert response.url.startswith(KEYCLOAK[instance]), response.url
    return response

def keycloak_password_login(instance, username, password, return_url="/"):
    page = keycloak_login(instance, return_url).read().decode()
    action = html.unescape(re.search(r'<form[^>]*action="([^"]+)"', page).group(1))
    response = post(action, {"username": username, "password": password, "credentialId": ""})
    return response.url, response.read().decode()

def logout(page):
    response = post(f"{APP}/Account/Logout", {"__RequestVerificationToken": antiforgery(page)})
    assert response.url == SELECT, f"Logout went to {response.url}"

# Health: liveness, and readiness with both Keycloak instances. Password sign-in is disabled.
live = json.loads(get(f"{APP}/health").read())
assert live["status"] == "Healthy", live
ready = json.loads(get(f"{APP}/health/ready").read())
checks = {check["name"]: check for check in ready["checks"]}
assert checks["keycloak-primary"]["status"] == "Healthy", ready
assert checks["keycloak-secondary"]["status"] == "Healthy", ready
assert checks["active-directory"]["description"] == "Password sign-in is disabled.", ready
assert ready["status"] == "Healthy", ready

# Anonymous users see the selection page with one button per Keycloak instance, and the requested URL is preserved.
response = get(f"{APP}/Account/Profile")
assert response.url == f"{SELECT}?ReturnUrl=%2FAccount%2FProfile", response.url
body = response.read().decode()
assert PRIMARY_LABEL in body and SECONDARY_LABEL in body, "Keycloak buttons missing"
assert 'value="Primary"' in body and 'value="Secondary"' in body
assert "Login with Username and Password" not in body, "Password option still shown"

# The Active Directory password pages are off.
try:
    get(f"{APP}/Account/Login")
    raise AssertionError("Password login page is reachable")
except urllib.error.HTTPError as error:
    assert error.code == 404, error
try:
    post(f"{APP}/Account/Login", {"__RequestVerificationToken": antiforgery(body),
                                  "UserName": "alice", "Password": "Alice!Lab2026", "Domain": "lab.test"})
    raise AssertionError("Password login POST was accepted")
except urllib.error.HTTPError as error:
    assert error.code == 404, error

# An unknown instance is rejected.
try:
    post(f"{APP}/Account/LoginWithKeycloak", {"__RequestVerificationToken": antiforgery(body), "instance": "Other"})
    raise AssertionError("Unknown Keycloak instance was accepted")
except urllib.error.HTTPError as error:
    assert error.code == 400, error

# Primary: a Keycloak user who is not permitted gets no session, and their Keycloak session is ended.
url, body = keycloak_password_login("Primary", "guest", "Guest123!")
assert url == f"{APP}/Account/NotAuthorized", f"Unpermitted Keycloak user reached {url}"
assert get(f"{APP}/").url.startswith(SELECT), "Unpermitted Keycloak user got a session"
assert 'name="password"' in keycloak_login("Primary", "/").read().decode(), "Unpermitted user's Keycloak session survived"

# Primary: OIDC + PKCE sign-in returns to the original URL.
url, body = keycloak_password_login("Primary", "demo", "Demo123!", "/Account/Profile")
assert url == f"{APP}/Account/Profile", url
assert "Signed in as <strong>demo</strong>" in body, "Login did not reach authenticated profile"
assert "KeycloakSso" in body, "Authentication method claim missing"
assert re.search(r"app_keycloak_instance</td><td[^>]*>Primary<", body), "Keycloak instance claim missing"
assert "http://localhost:8080/realms/mvc-sample" in body, "Primary issuer missing"
home = get(f"{APP}/").read().decode()
assert "Keycloak SSO" in home and "Keycloak (Primary)" in home, "Home page does not show the session details"

# Signed-in users skip the selection page; external return URLs are ignored.
response = get(f"{SELECT}?ReturnUrl=https%3A%2F%2Fevil.example.org%2F")
assert response.url == f"{APP}/", f"Open redirect: {response.url}"
response = get(f"{SELECT}?ReturnUrl=%2F%2Fevil.example.org")
assert response.url == f"{APP}/", f"Open redirect: {response.url}"

# Primary logout ends both sessions and returns to the selection page.
logout(home)
assert 'name="password"' in keycloak_login("Primary", "/").read().decode(), "Primary Keycloak session survived logout"

# Primary: lab.test users also sign in with their AD password, through LDAP user federation.
url, body = keycloak_password_login("Primary", "alice", "Alice!Lab2026", "/Account/Profile")
assert "Signed in as <strong>alice</strong>" in body and "KeycloakSso" in body, "Federated AD user did not sign in"
logout(get(f"{APP}/").read().decode())
url, body = keycloak_password_login("Primary", "carol", "Carol!Lab2026")
assert url.startswith(KEYCLOAK["Primary"]) and 'name="password"' in body, "Disabled AD account signed in to Keycloak"

# Secondary: permitted users are per instance. "demo" exists there too but is only permitted on Primary.
url, body = keycloak_password_login("Secondary", "demo", "Demo123!")
assert url == f"{APP}/Account/NotAuthorized", f"Secondary demo user reached {url}"
assert get(f"{APP}/").url.startswith(SELECT), "Unpermitted Secondary user got a session"
assert 'name="password"' in keycloak_login("Secondary", "/").read().decode(), "Unpermitted user's Secondary session survived"

# Secondary: sign-in goes to the second instance and its own callback path.
url, body = keycloak_password_login("Secondary", "partner", "Partner123!", "/Account/Profile")
assert url == f"{APP}/Account/Profile", url
assert "Signed in as <strong>partner</strong>" in body, "Secondary login did not reach authenticated profile"
assert re.search(r"app_keycloak_instance</td><td[^>]*>Secondary<", body), "Secondary instance claim missing"
assert "http://localhost:8081/realms/mvc-secondary" in body, "Secondary issuer missing"
home = get(f"{APP}/").read().decode()
assert "Keycloak (Secondary)" in home and "Partner User" in home, "Home page does not show the Secondary session"

# Secondary logout ends its Keycloak session.
logout(home)
assert get(f"{APP}/").url.startswith(SELECT), "Secondary session survived logout"
assert 'name="password"' in keycloak_login("Secondary", "/").read().decode(), "Secondary Keycloak session survived logout"

# Kerberos SSO through the Primary instance, from a container that holds a lab.test ticket (kerberos/sso_test.sh).
subprocess.run(["docker", "compose", "run", "--rm", "-T", "kerberos-client"],
               cwd=pathlib.Path(__file__).resolve().parent.parent, check=True)

print("PASS: health checks, selection page with both Keycloak instances, password login disabled, "
      "return URL, permitted users per instance, PKCE login and logout on both instances, claims, "
      "open-redirect protection, LDAP federation and Kerberos SSO")
