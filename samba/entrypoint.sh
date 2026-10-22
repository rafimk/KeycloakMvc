#!/bin/bash
# Provisions the lab.test domain on first start, then runs Samba in the foreground.
set -euo pipefail

REALM=LAB.TEST
DOMAIN=LAB
HOST=dc1
FQDN=dc1.lab.test
TLS=/var/lib/samba/private/tls

if [ ! -f /var/lib/samba/private/sam.ldb ]; then
    echo "Provisioning $REALM"
    rm -f /etc/samba/smb.conf

    # A private CA and an LDAPS certificate whose name matches the DC host name.
    mkdir -p "$TLS"
    openssl req -x509 -newkey rsa:2048 -nodes -days 3650 -subj "/CN=Lab Test Root CA" \
        -keyout "$TLS/ca-key.pem" -out "$TLS/ca.pem" 2>/dev/null
    openssl req -newkey rsa:2048 -nodes -subj "/CN=$FQDN" \
        -keyout "$TLS/key.pem" -out "$TLS/server.csr" 2>/dev/null
    printf 'subjectAltName=DNS:%s\nextendedKeyUsage=serverAuth\n' "$FQDN" > "$TLS/ext.cnf"
    openssl x509 -req -in "$TLS/server.csr" -CA "$TLS/ca.pem" -CAkey "$TLS/ca-key.pem" -CAcreateserial \
        -days 825 -extfile "$TLS/ext.cnf" -out "$TLS/cert.pem" 2>/dev/null
    chmod 600 "$TLS/key.pem" "$TLS/ca-key.pem"

    samba-tool domain provision --realm="$REALM" --domain="$DOMAIN" --host-name="$HOST" \
        --server-role=dc --dns-backend=SAMBA_INTERNAL --adminpass="$SAMBA_ADMIN_PASSWORD" \
        --option="tls enabled = yes" \
        --option="tls keyfile = $TLS/key.pem" \
        --option="tls certfile = $TLS/cert.pem" \
        --option="tls cafile = $TLS/ca.pem" \
        --option="dns forwarder = 127.0.0.11"

    # Lock accounts after 3 bad passwords for 5 minutes, so lockout can be tested.
    samba-tool domain passwordsettings set --account-lockout-threshold=3 \
        --account-lockout-duration=5 --reset-account-lockout-after=5

    samba-tool group add "App Admins"
    samba-tool user create alice 'Alice!Lab2026' --given-name=Alice --surname=Anderson --mail-address=alice@lab.test
    samba-tool user create bob 'Bob!Lab2026' --given-name=Bob --surname=Brown --mail-address=bob@lab.test
    samba-tool user create carol 'Carol!Lab2026' --given-name=Carol --surname=Clark --mail-address=carol@lab.test
    samba-tool user create dave 'Dave!Lab2026' --given-name=Dave --surname=Davis --mail-address=dave@lab.test
    for user in alice bob carol dave; do samba-tool user setexpiry "$user" --noexpiry; done
    samba-tool group addmembers "App Admins" alice
    samba-tool user disable carol

    # Service account for Keycloak: LDAP user federation bind and the Kerberos (SPNEGO) service principal.
    samba-tool user create keycloak-svc 'Keycloak!Lab2026' --description="Keycloak service account"
    samba-tool user setexpiry keycloak-svc --noexpiry
    samba-tool spn add HTTP/keycloak.lab.test keycloak-svc
    # AES only (8 + 16). Without this the KDC issues RC4 service tickets, which Java rejects.
    ldbmodify -H /var/lib/samba/private/sam.ldb <<'LDIF'
dn: CN=keycloak-svc,CN=Users,DC=lab,DC=test
changetype: modify
replace: msDS-SupportedEncryptionTypes
msDS-SupportedEncryptionTypes: 24
LDIF
fi

# Share the Keycloak keytab with the Keycloak container. Its keys stay valid until the password changes.
if [ -d /keytab-export ]; then
    rm -f /keytab-export/keycloak.keytab
    samba-tool domain exportkeytab /keytab-export/keycloak.keytab --principal="HTTP/keycloak.lab.test"
    # Keycloak runs as a non-root user. Development only: a real keytab is readable by the service alone.
    chmod 644 /keytab-export/keycloak.keytab
fi

# Share the CA certificate with the MVC container so it can validate LDAPS.
if [ -d /tls-export ]; then
    cp "$TLS/ca.pem" /tls-export/lab-ca.pem
fi

exec samba --foreground --no-process-group
