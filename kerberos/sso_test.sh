#!/bin/bash
# Kerberos SSO check: a lab.test user with a Kerberos ticket signs in to Keycloak without a password.
# Sends the same authorization code + PKCE request as the MVC app, then redeems the code.
set -euo pipefail

KEYCLOAK=http://keycloak.lab.test:8080/realms/mvc-sample/protocol/openid-connect
REDIRECT=http://localhost:5000/signin-oidc

fail() { echo "FAIL: $*" >&2; exit 1; }

b64url() { base64 -w0 | tr '+/' '-_' | tr -d '='; }

jwt_payload() {
    local part
    part=$(cut -d. -f2 | tr -- '-_' '+/')
    while [ $(( ${#part} % 4 )) -ne 0 ]; do part="$part="; done
    printf '%s' "$part" | base64 -d
}

# Requests a code for the mvc-app client; prints the HTTP status and the redirect target.
authorize() {
    local verifier=$1; shift
    local challenge
    challenge=$(printf '%s' "$verifier" | openssl dgst -sha256 -binary | b64url)
    curl -s -o /dev/null -D /tmp/headers -w '%{http_code} %{redirect_url}\n' "$@" \
        "$KEYCLOAK/auth?client_id=mvc-app&response_type=code&scope=openid&state=kerberos-test&nonce=kerberos-test&redirect_uri=$REDIRECT&code_challenge=$challenge&code_challenge_method=S256"
}

sign_in() {
    local user=$1 password=$2 verifier status location code token claims
    kdestroy -q 2>/dev/null || true
    printf '%s\n' "$password" | kinit "$user@LAB.TEST" >/dev/null || fail "kinit $user failed"
    verifier=$(head -c 32 /dev/urandom | b64url)
    read -r status location < <(authorize "$verifier" --negotiate -u :)
    [ "$status" = 302 ] || fail "$user: Keycloak answered $status instead of redirecting with a code"
    code=$(sed -n 's/.*[?&]code=\([^&]*\).*/\1/p' <<< "$location")
    [ -n "$code" ] || fail "$user: no code in redirect $location"
    token=$(curl -s "$KEYCLOAK/token" -d grant_type=authorization_code -d client_id=mvc-app \
        -d client_secret=mvc-local-secret -d redirect_uri="$REDIRECT" -d code="$code" -d code_verifier="$verifier" \
        | jq -er .id_token) || fail "$user: code exchange failed"
    claims=$(jwt_payload <<< "$token")
    [ "$(jq -r .preferred_username <<< "$claims")" = "$user" ] || fail "$user: wrong user in ID token: $claims"
    jq -e '.amr | index("kerberos")' <<< "$claims" >/dev/null || fail "$user: amr claim lacks kerberos: $claims"
    echo "$user: signed in with Kerberos; ID token amr=$(jq -c .amr <<< "$claims")"
}

# Without a ticket, Keycloak offers Negotiate and falls back to its login form.
kdestroy -q 2>/dev/null || true
read -r status _ < <(authorize "no-ticket-verifier-0000000000000000000000000")
[ "$status" = 401 ] || fail "Keycloak answered $status without a ticket, expected 401 Negotiate"
grep -qi '^WWW-Authenticate: Negotiate' /tmp/headers || fail "No Negotiate challenge from Keycloak"
echo "no ticket: Keycloak offers Negotiate"

sign_in alice 'Alice!Lab2026'
sign_in bob 'Bob!Lab2026'

# The KDC refuses tickets to disabled accounts.
kdestroy -q 2>/dev/null || true
if printf '%s\n' 'Carol!Lab2026' | kinit carol@LAB.TEST >/dev/null 2>&1; then fail "Disabled account got a ticket"; fi
echo "carol: disabled account gets no ticket"

echo "PASS: Kerberos SSO through Keycloak"
