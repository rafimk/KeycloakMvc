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
    samba-tool user create alice 'Alice!Lab2026' --given-name=Alice --surname=Anderson
    samba-tool user create bob 'Bob!Lab2026' --given-name=Bob --surname=Brown
    samba-tool user create carol 'Carol!Lab2026' --given-name=Carol --surname=Clark
    for user in alice bob carol; do samba-tool user setexpiry "$user" --noexpiry; done
    samba-tool group addmembers "App Admins" alice
    samba-tool user disable carol
fi

# Share the CA certificate with the MVC container so it can validate LDAPS.
if [ -d /tls-export ]; then
    cp "$TLS/ca.pem" /tls-export/lab-ca.pem
fi

exec samba --foreground --no-process-group
