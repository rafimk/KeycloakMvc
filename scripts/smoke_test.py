"""End-to-end local sample check. Run: python scripts/smoke_test.py"""
import html
import http.cookiejar
import re
import time
import urllib.error
import urllib.parse
import urllib.request

APP = "http://localhost:5000"
SELECT = f"{APP}/Account/SelectAuthentication"

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

def keycloak_login(return_url):
    """Choose "Login with Logged-in User" and return the Keycloak login page."""
    page = get(f"{SELECT}?ReturnUrl={urllib.parse.quote(return_url)}").read().decode()
    response = post(f"{APP}/Account/LoginWithLoggedUser", {"__RequestVerificationToken": antiforgery(page), "returnUrl": return_url})
    assert response.url.startswith("http://localhost:8080/"), response.url
    return response

def ad_login(fields, return_url="/"):
    """Submit the username/password form, waiting out the login rate limit if needed."""
    deadline = time.time() + 75
    while True:
        page = get(f"{APP}/Account/Login?ReturnUrl={urllib.parse.quote(return_url)}").read().decode()
        try:
            response = post(f"{APP}/Account/Login", {"__RequestVerificationToken": antiforgery(page),
                                                     "ReturnUrl": return_url, **fields})
            url, body = response.url, response.read().decode()
        except urllib.error.HTTPError as error:
            url, body = error.url, error.read().decode()
        if "Too many sign-in attempts" not in body or time.time() > deadline:
            return url, body
        time.sleep(5)

assert get(f"{APP}/health").status == 200

# Anonymous users see the selection page, not Keycloak, and the requested URL is preserved.
response = get(f"{APP}/Account/Profile")
assert response.url == f"{SELECT}?ReturnUrl=%2FAccount%2FProfile", response.url
body = response.read().decode()
assert "Login with Logged-in User" in body and "Login with Username and Password" in body

# Manual login page lists the configured domains and rejects bad input with generic messages.
body = get(f"{APP}/Account/Login").read().decode()
assert 'value="lab.test"' in body and 'value="ops.example.com"' in body, "Configured domains missing"
assert 'type="password"' in body
url, body = ad_login({"UserName": "alice", "Password": "", "Domain": "lab.test"})
assert "Enter your password." in body, "Empty password was not rejected"
url, body = ad_login({"UserName": "alice", "Password": "x", "Domain": "evil.example.org"})
assert "Select a domain from the list." in body, "Unconfigured domain was accepted"
# The ops.example.com sample domain does not exist, so its directory is unreachable.
url, body = ad_login({"UserName": "jsmith", "Password": "not-a-real-password", "Domain": "ops.example.com"})
assert "Sign-in is temporarily unavailable" in body, "Unreachable domain controller not reported"
assert "not-a-real-password" not in body, "Password was echoed back"
try:
    post(f"{APP}/Account/Login", {"UserName": "alice", "Password": "x", "Domain": "lab.test"})
    raise AssertionError("Login POST without antiforgery token was accepted")
except urllib.error.HTTPError as error:
    assert error.code == 400

# Active Directory sign-in against the local Samba lab domain (lab.test).
generic = "The username, password or domain is incorrect."
url, body = ad_login({"UserName": "bob", "Password": "wrong-password", "Domain": "lab.test"})
assert generic in body, "Wrong password was not rejected"
url, body = ad_login({"UserName": "carol", "Password": "Carol!Lab2026", "Domain": "lab.test"})
assert generic in body, "Disabled account was not rejected"
url, body = ad_login({"UserName": "OPS\\alice", "Password": "Alice!Lab2026", "Domain": "lab.test"})
assert generic in body, "Username from another domain was accepted"
url, body = ad_login({"UserName": "LAB\\alice", "Password": "Alice!Lab2026", "Domain": "lab.test"}, "/Account/Profile")
assert url == f"{APP}/Account/Profile", f"AD login did not return to the requested page: {url}"
assert "Signed in as <strong>alice</strong>" in body, "AD login did not reach authenticated profile"
assert re.search(r"app_user_id</td><td[^>]*>ad\|lab\.test\|[0-9a-f-]{36}<", body), "AD user id claim missing"
assert re.search(r"<td>role</td><td[^>]*>Admin<", body), "AD group role mapping missing"
home = get(f"{APP}/").read().decode()
assert "Active Directory" in home and "lab.test" in home and "Alice Anderson" in home, "Home page AD details missing"
logout = post(f"{APP}/Account/Logout", {"__RequestVerificationToken": antiforgery(home)})
assert logout.url == SELECT, f"AD logout went to {logout.url}"
assert get(f"{APP}/").url.startswith(SELECT), "AD session survived logout"
url, body = ad_login({"UserName": "bob", "Password": "Bob!Lab2026", "Domain": "lab.test"})
assert url == f"{APP}/", url
assert re.search(r"Welcome, Bob Brown", body) and "Admin" not in body
token = antiforgery(body)
post(f"{APP}/Account/Logout", {"__RequestVerificationToken": token})

# Keycloak login through the existing OIDC + PKCE flow returns to the original URL.
page = keycloak_login("/Account/Profile").read().decode()
action = html.unescape(re.search(r'<form[^>]*action="([^"]+)"', page).group(1))
profile = post(action, {"username": "demo", "password": "Demo123!", "credentialId": ""})
assert profile.url == f"{APP}/Account/Profile", profile.url
body = profile.read().decode()
assert "Signed in as <strong>demo</strong>" in body, "Login did not reach authenticated profile"
assert "app_auth_method" in body and "KeycloakSso" in body, "Authentication method claim missing"
home = get(f"{APP}/").read().decode()
assert "Keycloak SSO" in home and "Username" in home, "Home page does not show the session details"

# Signed-in users skip the selection page; external return URLs are ignored.
response = get(f"{SELECT}?ReturnUrl=https%3A%2F%2Fevil.example.org%2F")
assert response.url == f"{APP}/", f"Open redirect: {response.url}"
response = get(f"{SELECT}?ReturnUrl=%2F%2Fevil.example.org")
assert response.url == f"{APP}/", f"Open redirect: {response.url}"

# Keycloak logout ends both sessions and returns to the selection page.
logout = post(f"{APP}/Account/Logout", {"__RequestVerificationToken": antiforgery(home)})
assert logout.url == SELECT, logout.url
assert 'name="password"' in keycloak_login("/").read().decode(), "Keycloak session survived logout"

# Password attempts are rate limited per client (5 per fixed one-minute window, so 11
# back-to-back attempts must hit the limit even if one window boundary passes).
token = antiforgery(get(f"{APP}/Account/Login").read().decode())
for _ in range(11):
    body = post(f"{APP}/Account/Login", {"__RequestVerificationToken": token, "UserName": "jsmith",
                                         "Password": "x", "Domain": "ops.example.com"}).read().decode()
    if "Too many sign-in attempts" in body:
        break
else:
    raise AssertionError("Login attempts were not rate limited")

print("PASS: selection page, return URL, AD form validation, AD sign-in/rejections/roles/logout, "
      "PKCE Keycloak login, claims, open-redirect protection, Keycloak logout and login rate limiting")
