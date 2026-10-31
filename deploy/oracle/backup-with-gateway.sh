#!/bin/sh
# Keep the previous active state, including on backup failure or interruption.
set -eu
restart_gateway=0
cleanup() {
    result=$?
    trap - EXIT HUP INT TERM
    if [ "$restart_gateway" -eq 1 ]; then
        if ! /usr/bin/systemctl start trobot-whatsapp.service; then
            echo 'T.Robot backup: gateway restart failed.' >&2
            result=1
        fi
    fi
    exit "$result"
}
trap cleanup EXIT
trap 'exit 1' HUP INT TERM
if /usr/bin/systemctl is-active --quiet trobot-whatsapp.service; then
    restart_gateway=1
    /usr/bin/systemctl stop trobot-whatsapp.service
fi
/usr/bin/python3 /opt/trobot/ops/backup.py
