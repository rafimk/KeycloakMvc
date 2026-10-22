# Kerberos test client: gets a lab.test ticket with kinit and signs in to Keycloak with SPNEGO.
# Development only. Run: docker compose run --rm kerberos-client
FROM debian:bookworm-slim
RUN apt-get update && DEBIAN_FRONTEND=noninteractive apt-get install -y --no-install-recommends \
        krb5-user curl jq openssl ca-certificates \
    && rm -rf /var/lib/apt/lists/*
COPY krb5.conf /etc/krb5.conf
COPY sso_test.sh /usr/local/bin/sso_test.sh
RUN sed -i 's/\r$//' /usr/local/bin/sso_test.sh /etc/krb5.conf && chmod +x /usr/local/bin/sso_test.sh
ENTRYPOINT ["/usr/local/bin/sso_test.sh"]
