#!/bin/bash
# Run as root on the dedicated Oracle VM. Does not expose any public HTTP port.
set -euo pipefail
umask 0077
install -d -o root -g trobot -m 750 /etc/trobot /etc/trobot/tls
install -d -o trobot -g trobot -m 700 /var/lib/trobot/keys
install -d -o root -g root -m 700 /var/backups/trobot
install -d -o root -g trobot -m 750 /opt/trobot/ops /opt/trobot/app/releases

if [[ ! -e /etc/trobot/trobot.env ]]; then
    # Never replace existing configuration or certificates silently.
    test ! -e /etc/trobot/tls/localhost.pfx
    python3 - <<'PY'
import pathlib, secrets
path = pathlib.Path('/root/trobot-provisioning/tls-password')
with path.open('x') as target:
    target.write(secrets.token_urlsafe(48))
PY
    openssl req -x509 -newkey rsa:3072 -nodes -days 90 \
        -subj /CN=localhost \
        -addext 'subjectAltName=DNS:localhost,IP:127.0.0.1' \
        -addext 'basicConstraints=critical,CA:FALSE' \
        -addext 'extendedKeyUsage=serverAuth' \
        -keyout /etc/trobot/tls/localhost.key \
        -out /etc/trobot/tls/localhost.crt 2>/dev/null
    openssl pkcs12 -export -inkey /etc/trobot/tls/localhost.key \
        -in /etc/trobot/tls/localhost.crt -out /etc/trobot/tls/localhost.pfx \
        -passout file:/root/trobot-provisioning/tls-password
    python3 - <<'PY'
import pathlib
password = pathlib.Path('/root/trobot-provisioning/tls-password').read_text()
configuration = {
    'Hosting__Enabled': 'true',
    'Hosting__ListenAddress': '127.0.0.1',
    'Hosting__HttpsPort': '5443',
    'Hosting__CertificatePath': '/etc/trobot/tls/localhost.pfx',
    'Hosting__CertificatePassword': password,
    'Hosting__DataProtectionKeysPath': '/var/lib/trobot/keys',
    'ConnectionStrings__TRobot': 'Data Source=/var/lib/trobot/trobot.db;Mode=ReadWrite;Foreign Keys=True',
    'AllowedHosts': 'localhost;127.0.0.1',
    'WhatsAppGateway__Enabled': 'false',
    'LocalScheduler__Enabled': 'false',
    'Logging__LogLevel__Default': 'Warning',
    'Logging__LogLevel__Microsoft.AspNetCore': 'Warning',
}
with pathlib.Path('/etc/trobot/trobot.env').open('x') as target:
    for key, value in configuration.items():
        target.write(key + '="' + value + '"\n')
PY
fi
chown root:trobot /etc/trobot/tls/localhost.pfx
chmod 640 /etc/trobot/tls/localhost.pfx
chmod 600 /etc/trobot/trobot.env /etc/trobot/tls/localhost.key
printf '%s\n' 'Private HTTPS configuration prepared; no public listener enabled'
