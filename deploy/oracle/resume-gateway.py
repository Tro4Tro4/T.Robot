#!/usr/bin/python3
"""Restore only an existing approved binding; never send or alter application data."""
import base64
import http.client
import json
import pathlib
import re
import ssl
import stat
import subprocess
import time

CONFIG = pathlib.Path('/etc/trobot-whatsapp/gateway.json')
CREDS = pathlib.Path('/var/lib/trobot-whatsapp/state/auth/creds.json')
CA = '/etc/trobot-whatsapp/localhost.crt'
DATABASE = '/var/lib/trobot/trobot.db'
# Executed as the database owner: even a read-only WAL connection can create sidecars.
QUERY = '''SELECT c.Id, c.EventId, c.GroupId, c.ActorId, c.Revision, c.ActivatedAtUtc
FROM WhatsAppConnections c
WHERE c.Enabled = 1 AND c.GroupId IS NOT NULL AND c.ActivatedAtUtc IS NOT NULL
AND EXISTS (SELECT 1 FROM AspNetUsers u WHERE u.PersonId = c.ActorId
            AND u.IsEnabled = 1 AND u.IsSystemAdministrator = 1)
AND EXISTS (SELECT 1 FROM EventOrganizers o WHERE o.EventId = c.EventId
            AND o.PersonId = c.ActorId AND o.IsActive = 1)'''
READ_BINDING = '''import contextlib, json, sqlite3, sys
with contextlib.closing(sqlite3.connect('file:' + sys.argv[1] + '?mode=ro', uri=True, timeout=5)) as db:
    db.execute('PRAGMA query_only=ON')
    print(json.dumps(db.execute(sys.argv[2]).fetchall()))
'''


def binding():
    result = subprocess.run(['/usr/sbin/runuser', '-u', 'trobot', '--',
                             '/usr/bin/python3', '-c', READ_BINDING, DATABASE, QUERY],
                            check=True, capture_output=True, text=True, timeout=10)
    rows = json.loads(result.stdout)
    if not rows:
        return None
    if len(rows) != 1 or len(rows[0]) != 6:
        raise ValueError('invalid_binding')
    row = rows[0]
    if not isinstance(row[2], str) or not re.fullmatch(r'[0-9-]+@g\.us', row[2]):
        raise ValueError('invalid_binding')
    return tuple(row)


def registered():
    try:
        if CREDS.is_symlink() or CREDS.parent.is_symlink():
            return False
        credentials = json.loads(CREDS.read_text(encoding='utf-8-sig'))
        me, account = credentials.get('me'), credentials.get('account')
        # Baileys rc14 chooses login from creds.me, not the historical registered flag.
        # Successful pairing also writes this signed account identity; never resume defaults.
        if (not isinstance(me, dict) or not isinstance(me.get('id'), str)
                or not re.fullmatch(r'[0-9]+(?::[0-9]+)?@s\.whatsapp\.net', me['id'])
                or not isinstance(account, dict)):
            return False
        for field, length in (('details', None), ('accountSignatureKey', 32),
                              ('accountSignature', 64), ('deviceSignature', 64)):
            value = account.get(field)
            # Protobuf account.toJSON emits base64 strings; BufferJSON emits wrappers.
            data = value if isinstance(value, str) else (
                value.get('data') if isinstance(value, dict) and value.get('type') == 'Buffer' else None)
            if not isinstance(data, str):
                return False
            decoded = base64.b64decode(data, validate=True)
            if not decoded or (length is not None and len(decoded) != length):
                return False
        return True
    except (OSError, ValueError, AttributeError, TypeError):
        return False


def token():
    info = CONFIG.lstat()
    if not stat.S_ISREG(info.st_mode) or info.st_uid != 0 or info.st_mode & 0o027:
        raise ValueError('invalid_configuration')
    config = json.loads(CONFIG.read_text(encoding='utf-8-sig'))['WhatsAppGateway']
    value = config['Token']
    if not isinstance(value, str) or not re.fullmatch(r'[a-fA-F0-9]{64}', value):
        raise ValueError('invalid_configuration')
    return value


def request(secret, path, payload=None):
    client = http.client.HTTPConnection('127.0.0.1', 5240, timeout=5)
    try:
        body = None if payload is None else json.dumps(payload)
        client.request('GET' if payload is None else 'POST', path, body,
                       {'Authorization': 'Bearer ' + secret, 'Content-Type': 'application/json'})
        response = client.getresponse()
        if response.status != 200:
            raise RuntimeError('gateway_unavailable')
        return json.loads(response.read(262145))
    finally:
        client.close()


def web_ready():
    client = http.client.HTTPSConnection('localhost', 5443, timeout=5,
                                        context=ssl.create_default_context(cafile=CA))
    try:
        client.request('GET', '/Access/Login')
        response = client.getresponse()
        return response.status == 200
    finally:
        client.close()


def wait_ready(check, seconds=45):
    deadline = time.monotonic() + seconds
    while time.monotonic() < deadline:
        try:
            if check():
                return
        except (OSError, http.client.HTTPException):
            pass
        time.sleep(1)
    raise RuntimeError('readiness_timeout')


def resume():
    secret = token()
    initial = binding()
    if initial is None or not registered():
        print('T.Robot gateway recovery: manual setup required; skipped.')
        return
    attempted = False
    try:
        wait_ready(web_ready)
        wait_ready(lambda: request(secret, '/health').get('status') == 'disconnected')
        if binding() != initial or not registered():
            raise RuntimeError('binding_changed')
        attempted = True
        request(secret, '/connect', {})
        def connected():
            health = request(secret, '/health')
            if health.get('status') in ('qr', 'error'):
                raise RuntimeError('manual_setup_required')
            return health.get('status') == 'connected' and health.get('activeGroupId') is None
        wait_ready(connected)
        if binding() != initial or not registered():
            raise RuntimeError('binding_changed')
        health = request(secret, '/group', {'groupId': initial[2]})
        if (health.get('status') != 'connected' or health.get('activeGroupId') != initial[2]
                or binding() != initial or not registered()):
            raise RuntimeError('binding_changed')
        print('T.Robot gateway recovery: approved connection restored.')
    except Exception:
        if attempted:
            try:
                request(secret, '/disconnect', {})
            except Exception:
                # A dead HTTP host must not leave an unverified socket running.
                subprocess.run(['/usr/bin/systemctl', '--no-block', 'stop', 'trobot-whatsapp.service'],
                               stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, timeout=10, check=False)
        raise


def main():
    try:
        resume()
        return 0
    except Exception:
        print('T.Robot gateway recovery: unavailable; manual intervention required.')
        return 1


if __name__ == '__main__':
    raise SystemExit(main())
