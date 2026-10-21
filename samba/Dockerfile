# Local Samba 4 Active Directory domain controller for testing the username/password sign-in.
# Development only: test users and passwords are created by entrypoint.sh.
FROM debian:bookworm-slim
RUN apt-get update && DEBIAN_FRONTEND=noninteractive apt-get install -y --no-install-recommends \
        samba samba-ad-dc samba-ad-provision samba-dsdb-modules samba-vfs-modules winbind \
        ldb-tools ldap-utils openssl ca-certificates \
    && rm -rf /var/lib/apt/lists/*
COPY entrypoint.sh /usr/local/bin/entrypoint.sh
RUN sed -i 's/\r$//' /usr/local/bin/entrypoint.sh && chmod +x /usr/local/bin/entrypoint.sh
EXPOSE 636
ENTRYPOINT ["/usr/local/bin/entrypoint.sh"]
