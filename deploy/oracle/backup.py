#!/usr/bin/python3
"""Root-only consistent SQLite and authentication-key backup; no chat or secret output."""
import datetime
import contextlib
import os
import pathlib
import shutil
import sqlite3
import stat
import subprocess
import tempfile
import uuid

PRODUCTION_DATA = pathlib.Path('/var/lib/trobot')
GATEWAY_STATE = pathlib.Path('/var/lib/trobot-whatsapp/state')
GATEWAY_CONFIG = pathlib.Path('/etc/trobot-whatsapp')
SNAPSHOT_CODE = '''import contextlib, sqlite3, sys
with contextlib.closing(sqlite3.connect('file:' + sys.argv[1] + '?mode=ro', uri=True, timeout=30)) as source:
    with contextlib.closing(sqlite3.connect(sys.argv[2])) as target:
        source.backup(target)
        if target.execute('pragma integrity_check').fetchall() != [('ok',)]:
            raise RuntimeError('integrity_failed')
        if target.execute('pragma foreign_key_check').fetchall():
            raise RuntimeError('foreign_keys_failed')
'''


def reject_links(path):
    """Inspect parents and every descendant before copying private sources."""
    for candidate in [path, *path.parents]:
        if candidate.is_symlink():
            raise RuntimeError('Backup source link rejected')
    info = path.lstat()
    if stat.S_ISDIR(info.st_mode):
        for child in path.iterdir():
            reject_links(child)
    elif not stat.S_ISREG(info.st_mode):
        raise RuntimeError('Backup source type rejected')


def snapshot(database, destination):
    if database.parent == PRODUCTION_DATA:
        # Root never opens SQLite on the application database (including WAL/SHM).
        temporary = database.parent / ('.backup-' + uuid.uuid4().hex)
        descriptor = os.open(temporary, os.O_CREAT | os.O_EXCL | os.O_WRONLY, 0o600)
        try:
            try:
                owner = database.stat()
                os.fchown(descriptor, owner.st_uid, owner.st_gid)
            finally:
                os.close(descriptor)
            subprocess.run(['/usr/sbin/runuser', '-u', 'trobot', '--', '/usr/bin/python3',
                            '-c', SNAPSHOT_CODE, str(database), str(temporary)],
                           check=True, capture_output=True, timeout=90)
            shutil.copyfile(temporary, destination)
        finally:
            for suffix in ('', '-wal', '-shm', '-journal'):
                temporary.with_name(temporary.name + suffix).unlink(missing_ok=True)
    else:
        # Portable offline verification only; deployed backups always take the owner path.
        with contextlib.closing(sqlite3.connect(database.as_uri() + '?mode=ro', uri=True, timeout=30)) as source:
            with contextlib.closing(sqlite3.connect(destination)) as target:
                source.backup(target)
                if target.execute('pragma integrity_check').fetchall() != [('ok',)]:
                    raise RuntimeError('Backup integrity check failed')
                if target.execute('pragma foreign_key_check').fetchall():
                    raise RuntimeError('Backup foreign-key check failed')


def require_gateway_stopped():
    result = subprocess.run(['/usr/bin/systemctl', 'is-active', 'trobot-whatsapp.service'],
                            capture_output=True, text=True, timeout=10, check=False)
    if result.returncode not in (3, 4) or result.stdout.strip() not in ('inactive', 'failed', 'unknown'):
        raise RuntimeError('Gateway must be stopped before backup')


def private_permissions(directory):
    for path in [directory, *directory.rglob('*')]:
        path.chmod(0o700 if path.is_dir() else 0o600)


def backup(data=PRODUCTION_DATA, destination=pathlib.Path('/var/backups/trobot'),
           gateway_state=GATEWAY_STATE, gateway_config=GATEWAY_CONFIG):
    os.umask(0o077)
    database = data / 'trobot.db'
    if not database.is_file():
        raise RuntimeError('Application database missing; no backup created')
    reject_links(database)
    for suffix in ('-wal', '-shm', '-journal'):
        sidecar = database.with_name(database.name + suffix)
        if sidecar.exists() or sidecar.is_symlink():
            reject_links(sidecar)
    keys = data / 'keys'
    reject_links(keys)
    gateway_present = gateway_state.exists() or gateway_state.is_symlink()
    if gateway_present:
        require_gateway_stopped()
        reject_links(gateway_state)
        reject_links(gateway_config / 'gateway.json')
        reject_links(gateway_config / 'localhost.crt')
        if not gateway_state.is_dir():
            raise RuntimeError('Gateway state directory missing')
    for candidate in [destination, *destination.parents]:
        if candidate.is_symlink():
            raise RuntimeError('Backup destination link rejected')
    destination.mkdir(mode=0o700, parents=True, exist_ok=True)
    destination.chmod(0o700)
    name = datetime.datetime.now(datetime.timezone.utc).strftime('%Y%m%dT%H%M%S%fZ')
    temporary = pathlib.Path(tempfile.mkdtemp(prefix='.pending-', dir=destination))
    try:
        snapshot(database, temporary / 'trobot.db')
        if not keys.is_dir():
            raise RuntimeError('Persistent authentication-key directory missing')
        shutil.copytree(keys, temporary / 'keys')
        if gateway_present:
            require_gateway_stopped()
            shutil.copytree(gateway_state, temporary / 'whatsapp-state')
            config_target = temporary / 'whatsapp-config'
            config_target.mkdir(mode=0o700)
            for config_name in ('gateway.json', 'localhost.crt'):
                shutil.copyfile(gateway_config / config_name, config_target / config_name)
            require_gateway_stopped()
        private_permissions(temporary)
        temporary.rename(destination / name)
    except BaseException:
        shutil.rmtree(temporary)
        raise
    # Preserve backups by default: retention needs a separate, explicit policy.
    print('Consistent SQLite backup and persistent keys saved; integrity checks passed')


if __name__ == '__main__':
    try:
        backup()
    except Exception:
        print('T.Robot backup failed; no complete backup published.')
        raise SystemExit(1)
