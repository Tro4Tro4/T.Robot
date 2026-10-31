#!/bin/bash
set -euo pipefail
umask 0077
test "$(uname -m)" = aarch64
cd /home/trobotadmin/trobot-staging
sha256sum -c ARM-SHA256SUMS
sha256sum -c gateway-SHA256SUMS
test ! -e /opt/dotnet
test ! -e /opt/trobot/app/current
for account in trobot trobot-whatsapp trobot-build caddy; do
    if ! id "$account" >/dev/null 2>&1; then
        useradd --system --user-group --no-create-home --shell /usr/sbin/nologin "$account"
    fi
done
usermod -a -G trobot-whatsapp trobot-build
install -d -o root -g root -m 755 /opt/dotnet /opt/node /opt/trobot
tar -xzf aspnetcore-runtime-10.0.13-linux-arm64.tar.gz -C /opt/dotnet
tar -xJf node-v24.22.0-linux-arm64.tar.xz -C /opt/node
tar -xOf caddy-linux-arm64.tar.gz caddy > /usr/local/bin/caddy
chmod 755 /usr/local/bin/caddy
install -d -o root -g trobot -m 750 /opt/trobot/app /opt/trobot/app/releases /opt/trobot/app/releases/m32-arm64 /opt/trobot/ops
tar -xzf release-m32-arm64.tar.gz -C /opt/trobot/app/releases/m32-arm64
chown -R root:trobot /opt/trobot/app
chmod -R u=rwX,g=rX,o= /opt/trobot/app
ln -s releases/m32-arm64 /opt/trobot/app/current
install -d -o root -g trobot-whatsapp -m 750 /opt/trobot/gateway /opt/trobot/gateway/releases
install -d -o trobot-build -g trobot-whatsapp -m 750 /opt/trobot/gateway/releases/m30-arm64
tar -xzf gateway-m30.tar.gz -C /opt/trobot/gateway/releases/m30-arm64
chown -R trobot-build:trobot-whatsapp /opt/trobot/gateway/releases/m30-arm64
install -d -o trobot-build -g trobot-build -m 700 /var/cache/trobot-build
systemd-run --unit=trobot-arm-npm --wait --collect --pipe \
    -p User=trobot-build -p Group=trobot-whatsapp \
    -p WorkingDirectory=/opt/trobot/gateway/releases/m30-arm64 \
    -p MemoryMax=256M -p MemorySwapMax=512M -p CPUQuota=50% \
    -p NoNewPrivileges=true -p ProtectHome=true -p ProtectSystem=strict \
    -p ReadWritePaths='/opt/trobot/gateway/releases/m30-arm64 /var/cache/trobot-build' \
    --setenv=HOME=/var/cache/trobot-build \
    --setenv=PATH=/opt/node/node-v24.22.0-linux-arm64/bin:/usr/bin:/bin \
    /opt/node/node-v24.22.0-linux-arm64/bin/npm ci --omit=dev --no-audit --no-fund
chown -R root:trobot-whatsapp /opt/trobot/gateway/releases/m30-arm64
chmod -R u=rwX,g=rX,o= /opt/trobot/gateway/releases/m30-arm64
ln -s releases/m30-arm64 /opt/trobot/gateway/current
install -d -o trobot -g trobot -m 700 /var/lib/trobot
install -d -o trobot-whatsapp -g trobot-whatsapp -m 700 /var/lib/trobot-whatsapp
install -d -o caddy -g caddy -m 700 /var/lib/caddy
install -d -o root -g root -m 700 /var/backups/trobot /root/trobot-provisioning
/opt/dotnet/dotnet --list-runtimes
/opt/node/node-v24.22.0-linux-arm64/bin/node --version
/usr/local/bin/caddy version
echo 'ARM host prepared; no application service or WhatsApp session started.'
