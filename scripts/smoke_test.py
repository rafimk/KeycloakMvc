"""End-to-end local sample check. Run: python scripts/smoke_test.py"""
import html
import http.cookiejar
import re
import urllib.parse
import urllib.request

# Browsers treat localhost as a secure context. Mirror that behavior for this
# localhost-only HTTP smoke test; Keycloak sets Secure cookies on localhost.
cookies = http.cookiejar.CookieJar(http.cookiejar.DefaultCookiePolicy(secure_protocols=("https", "http")))
browser = urllib.request.build_opener(urllib.request.HTTPCookieProcessor(cookies))

def get(url):
    return browser.open(url, timeout=30)

def post(url, fields):
    return browser.open(url, urllib.parse.urlencode(fields).encode(), timeout=30)

assert get("http://localhost:5000/health").status == 200
login = get("http://localhost:5000/Account/Profile")
assert login.url.startswith("http://localhost:8080/")
page = login.read().decode()
action = html.unescape(re.search(r'<form[^>]*action="([^"]+)"', page).group(1))
profile = post(action, {"username": "demo", "password": "Demo123!", "credentialId": ""})
body = profile.read().decode()
assert profile.url == "http://localhost:5000/Account/Profile", profile.url
assert "Signed in as <strong>demo</strong>" in body, "Login did not reach authenticated profile"
token = html.unescape(re.search(r'name="__RequestVerificationToken"[^>]*value="([^"]+)"', body).group(1))
logout = post("http://localhost:5000/Account/Logout", {"__RequestVerificationToken": token})
body = logout.read().decode()
assert "Sign in with Keycloak" in body, "Logout did not clear application session"
login = get("http://localhost:5000/Account/Profile")
assert login.url.startswith("http://localhost:8080/")
assert 'name="password"' in login.read().decode(), "Keycloak session survived logout"
print("PASS: health, protected route, PKCE login, claims, antiforgery logout and Keycloak session logout")
