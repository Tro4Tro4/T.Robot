#!/usr/bin/python3
"""T.Robot release on the Google Cloud VM (x64): staging, coordinated activation and verification.

Runs as root through sudo. Never prints configuration, chat data, tokens or session files.
Database helpers ("db-snapshot", "db-migrate") run as the database owner through runuser.

  stage    --name N --staging DIR [--web-sha H] [--gateway-sha H] [--sql-sha H]
  activate --name N [--web-sha H] [--gateway-sha H] [--sql-sha H] [--allow-changes T1,T2]
  verify   --name N
"""
import argparse
import contextlib
import hashlib
import importlib.util
import json
import os
import pathlib
import re
import shutil
import signal
import sqlite3
import subprocess
import sys
import tarfile

APP = pathlib.Path('/opt/trobot/app')
GATEWAY = pathlib.Path('/opt/trobot/gateway')
OPS = pathlib.Path('/opt/trobot/ops')
SCRIPT = OPS / 'release.py'
MIGRATIONS = OPS / 'migrations'
PROVISIONING = pathlib.Path('/root/trobot-provisioning')
DB = pathlib.Path('/var/lib/trobot/trobot.db')
BACKUPS = pathlib.Path('/var/backups/trobot')
NODE = pathlib.Path('/opt/node/node-v24.22.0-linux-x64/bin/node')
CREDS = pathlib.Path('/var/lib/trobot-whatsapp/state/auth/creds.json')
PROTECTED = ('/etc/trobot', '/etc/trobot-whatsapp', '/var/lib/trobot/keys')
SERVICES = ('trobot.service', 'trobot-whatsapp.service', 'caddy.service', 'trobot-backup.timer')
LOCK = pathlib.Path('/run/lock/trobot-release.lock')
REMOVED_SCENARIO = 'ce014f6d-1a90-4388-b187-771235bcb918'
NAME_PATTERN = re.compile(r'[a-z0-9][a-z0-9.-]{2,62}')
BLOCKED_PARTS = {'.local', 'state', 'auth', 'keys', 'node_modules'}
BLOCKED_SUFFIXES = ('.db', '.sqlite', '.sqlite3', '.pfx', '.pem', '.key', '.env')
HISTORY_INSERT = re.compile(
    r'INSERT\s+INTO\s+"__EFMigrationsHistory"\s*\(\s*"MigrationId"\s*,\s*"ProductVersion"\s*\)\s*'
    r"VALUES\s*\(\s*'([0-9]{14}_[A-Za-z0-9_]+)'\s*,\s*'([0-9][0-9A-Za-z.+-]*)'\s*\)\s*;?\s*$", re.S)


# ---------------------------------------------------------------- generic helpers

def run(*args, timeout=180):
    return subprocess.run(args, check=True, capture_output=True, text=True, timeout=timeout).stdout.strip()


def active(unit):
    return subprocess.run(['systemctl', 'is-active', '--quiet', unit], capture_output=True, timeout=10).returncode == 0


def digest(path):
    return hashlib.sha256(pathlib.Path(path).read_bytes()).hexdigest()


def quote(name):
    return '"' + name.replace('"', '""') + '"'


def check_hash(value):
    if value is not None and not re.fullmatch(r'[0-9a-f]{64}', value):
        raise ValueError('invalid_hash')
    return value


def check_name(value):
    if not NAME_PATTERN.fullmatch(value) or '..' in value:
        raise ValueError('invalid_release_name')
    return value


def safe_member(name):
    path = pathlib.PurePosixPath(name)
    lower = path.name.lower()
    return (not path.is_absolute() and '..' not in path.parts and path.parts
            and path.suffix.lower() not in BLOCKED_SUFFIXES
            and not lower.endswith(('.db-wal', '.db-shm'))
            and not lower.startswith(('appsettings.production', '.env'))
            and lower not in ('gateway.json', 'creds.json')
            and not any(part.lower() in BLOCKED_PARTS for part in path.parts))


# ---------------------------------------------------------------- database helpers (database owner)

def rows_digest(db, table, columns):
    rows = sorted(repr(row) for row in db.execute(
        'select ' + ','.join(map(quote, columns)) + ' from ' + quote(table)))
    return len(rows), hashlib.sha256('\n'.join(rows).encode()).hexdigest()


def state(db):
    if db.execute('pragma integrity_check').fetchall() != [('ok',)]:
        raise RuntimeError('integrity_failed')
    if db.execute('pragma foreign_key_check').fetchall():
        raise RuntimeError('foreign_keys_failed')
    tables = {}
    for name, schema in db.execute("select name, sql from sqlite_master where type='table' "
                                   "and name not like 'sqlite_%' order by name").fetchall():
        columns = sorted(list(row[1:]) for row in db.execute('pragma table_xinfo(' + quote(name) + ')'))
        count, value = rows_digest(db, name, [column[0] for column in columns])
        foreign_keys = sorted(list(row[2:7]) for row in db.execute('pragma foreign_key_list(' + quote(name) + ')'))
        tables[name] = {'count': count, 'hash': value, 'schema': schema, 'columns': columns,
                        'foreign_keys': foreign_keys}
    # Indexes, triggers and views; automatic index names follow table renames, so they are excluded.
    objects = sorted(list(row) for row in db.execute(
        "select type, name, tbl_name, sql from sqlite_master where type in ('index','trigger','view') "
        "and name not like 'sqlite_%'"))
    history = sorted(list(row) for row in db.execute('select MigrationId, ProductVersion from __EFMigrationsHistory'))
    if db.execute('select count(*) from Events where lower(Id)=?', (REMOVED_SCENARIO,)).fetchone()[0]:
        raise RuntimeError('removed_scenario_present')
    return {'tables': tables, 'objects': objects, 'history': history}


def structure(snapshot):
    """Schema-only view: running services may legitimately write rows (logins, sessions)."""
    return {'history': snapshot['history'], 'objects': snapshot['objects'],
            'tables': {name: [table['schema'], table['columns'], table['foreign_keys']]
                       for name, table in snapshot['tables'].items()}}


def statements(text):
    """Split an EF Core SQLite script; transaction and foreign-key pragmas are handled here."""
    result, pending = [], ''
    for line in text.splitlines(True):
        pending += line
        if sqlite3.complete_statement(pending):
            statement, pending = pending.strip(), ''
            if statement.startswith(('--', '/*')):
                raise ValueError('leading_comment_not_supported')
            normalized = ''.join(statement.upper().split())
            if normalized in ('BEGINTRANSACTION;', 'COMMIT;', 'PRAGMAFOREIGN_KEYS=0;', 'PRAGMAFOREIGN_KEYS=1;'):
                continue
            if normalized.startswith(('PRAGMA', 'BEGIN', 'COMMIT', 'ROLLBACK', 'ATTACH', 'DETACH', 'VACUUM')):
                raise ValueError('unexpected_statement')
            result.append(statement)
    if pending.strip() or not result:
        raise ValueError('incomplete_script')
    return result


def planned_migrations(text):
    found = []
    for statement in statements(text):
        match = HISTORY_INSERT.match(statement)
        if match:
            found.append([match.group(1), match.group(2)])
        elif '__EFMIGRATIONSHISTORY' in statement.upper():
            raise ValueError('unexpected_history_statement')
    if not found or len({item[0] for item in found}) != len(found):
        raise ValueError('missing_history_insert')
    return found


def compare_preserved(before, after, allow_changes=()):
    """Every pre-existing table and column must survive with its rows unchanged on the old columns."""
    if after['history'] != sorted(before['history'] + after.get('planned', [])):
        raise RuntimeError('history_mismatch')
    for name, table in before['tables'].items():
        if name == '__EFMigrationsHistory':
            continue
        updated = after['tables'].get(name)
        if updated is None:
            raise RuntimeError('table_removed:' + name)
        old_names = [column[0] for column in table['columns']]
        if not set(old_names) <= {column[0] for column in updated['columns']}:
            raise RuntimeError('column_removed:' + name)
        if not all(key in updated['foreign_keys'] for key in table['foreign_keys']):
            raise RuntimeError('foreign_key_removed:' + name)
        if updated['count'] != table['count']:
            raise RuntimeError('row_count_changed:' + name)
        if name not in allow_changes and after['projected'].get(name) != table['hash']:
            raise RuntimeError('rows_changed:' + name)
    remaining = {(item[0], item[1]) for item in after['objects']}
    for item in before['objects']:
        if (item[0], item[1]) not in remaining:
            raise RuntimeError('schema_object_removed:' + item[1])


def helper_snapshot(path):
    with contextlib.closing(sqlite3.connect('file:' + str(path) + '?mode=ro', uri=True, timeout=30)) as db:
        db.execute('pragma query_only=on')
        print(json.dumps(state(db)))


def helper_migrate(path, sql_path, sql_hash, allow_changes):
    if digest(sql_path) != sql_hash:
        raise RuntimeError('sql_hash_mismatch')
    text = pathlib.Path(sql_path).read_text(encoding='utf-8-sig')
    planned = planned_migrations(text)
    with contextlib.closing(sqlite3.connect('file:' + str(path) + '?mode=rw', uri=True, timeout=30,
                                            isolation_level=None)) as db:
        before = state(db)
        applied = {item[0] for item in before['history']}
        if any(item[0] in applied for item in planned):
            raise RuntimeError('migration_already_applied')
        if before['history'] and max(item[0] for item in planned) <= max(applied):
            raise RuntimeError('migration_out_of_order')
        # SQLite schema changes: foreign keys off outside the transaction, verified before commit.
        db.execute('pragma foreign_keys=off')
        db.execute('begin immediate')
        try:
            for statement in statements(text):
                db.execute(statement)
            after = state(db)
            after['planned'] = planned
            after['projected'] = {}
            for name, table in before['tables'].items():
                if name in after['tables'] and name != '__EFMigrationsHistory':
                    names = [column[0] for column in table['columns']]
                    if set(names) <= {column[0] for column in after['tables'][name]['columns']}:
                        after['projected'][name] = rows_digest(db, name, names)[1]
            compare_preserved(before, after, allow_changes)
            db.execute('commit')
        except BaseException:
            db.execute('rollback')
            raise
        finally:
            db.execute('pragma foreign_keys=on')
    with contextlib.closing(sqlite3.connect('file:' + str(path) + '?mode=ro', uri=True, timeout=30)) as db:
        print(json.dumps(state(db)))


# ---------------------------------------------------------------- root operations

def database(path=DB):
    return json.loads(run('/usr/sbin/runuser', '-u', 'trobot', '--', '/usr/bin/python3', str(SCRIPT),
                          'db-snapshot', str(path)))


def migrate(path, sql_path, sql_hash, allow_changes):
    return json.loads(run('/usr/sbin/runuser', '-u', 'trobot', '--', '/usr/bin/python3', str(SCRIPT),
                          'db-migrate', str(path), str(sql_path), sql_hash, ','.join(allow_changes), timeout=300))


def recovery():
    spec = importlib.util.spec_from_file_location('resume', OPS / 'resume-gateway.py')
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def identity():
    value = json.loads(CREDS.read_text(encoding='utf-8-sig'))
    return hashlib.sha256(value['me']['id'].encode()).hexdigest()


def protected_files():
    result = {}
    for directory in PROTECTED:
        for path in sorted(pathlib.Path(directory).rglob('*')):
            if path.is_symlink():
                raise RuntimeError('protected_link')
            if path.is_file():
                result[str(path)] = digest(path)
    return result


def protected_preserved(original, now):
    """Existing files unchanged; only new Data Protection keys may appear (rotation)."""
    if any(now.get(path) != value for path, value in original.items()):
        return False
    return all(path.startswith('/var/lib/trobot/keys/') for path in set(now) - set(original))


def current(root):
    return os.readlink(root / 'current')


def switch(root, target, name):
    if not (root / target).is_dir():
        raise RuntimeError('release_missing')
    pending = root / ('current-' + name + '-switch')
    if pending.exists() or pending.is_symlink():
        raise RuntimeError('pending_switch_exists')
    pending.symlink_to(target)
    os.replace(pending, root / 'current')


def extract(package, destination, expected, group):
    if digest(package) != expected:
        raise RuntimeError('package_hash_mismatch')
    if destination.exists() or destination.is_symlink():
        raise RuntimeError('release_exists')
    with tarfile.open(package, 'r:gz') as archive:
        members = archive.getmembers()
        if not members or len({member.name for member in members}) != len(members):
            raise RuntimeError('invalid_archive')
        for member in members:
            if not member.isfile() or not safe_member(member.name):
                raise RuntimeError('archive_member_rejected')
        destination.mkdir(mode=0o750)
        try:
            archive.extractall(destination, filter='data')
        except BaseException:
            shutil.rmtree(destination)
            raise
    for path in [destination, *destination.rglob('*')]:
        shutil.chown(path, user='root', group=group)
        path.chmod(0o750 if path.is_dir() else 0o640)
    (destination / 'release.sha256').write_text(expected + '\n')


def gateway_health(module, binding):
    try:
        health = module.request(module.token(), '/health')
    except (RuntimeError, ValueError):
        return False
    return health.get('status') == 'connected' and health.get('activeGroupId') == binding[2]


def exclusive_lock():
    import fcntl
    handle = open(LOCK, 'w')
    try:
        fcntl.flock(handle, fcntl.LOCK_EX | fcntl.LOCK_NB)
    except OSError:
        raise RuntimeError('another_release_running')
    return handle


class Interrupted(Exception):
    pass


def interrupt(signum, _frame):
    raise Interrupted('signal_' + str(signum))


def staged_files(staging, name):
    return (staging / ('web-' + name + '.tar.gz'), staging / ('gateway-' + name + '.tar.gz'),
            staging / ('migration-' + name + '.sql'))


def stage(args):
    name = check_name(args.name)
    staging = pathlib.Path(args.staging).resolve()
    if not staging.is_dir():
        raise RuntimeError('staging_missing')
    web_package, gateway_package, sql_file = staged_files(staging, name)
    if not (args.web_sha or args.gateway_sha):
        raise RuntimeError('nothing_to_stage')
    target = 'releases/' + name
    result = {'release': name, 'services_changed': False}
    # Validate everything before writing, then remove partial output on any failure.
    for destination, package_file, expected in ((APP / target, web_package, args.web_sha),
                                                 (GATEWAY / target, gateway_package, args.gateway_sha)):
        if expected and (destination.exists() or destination.is_symlink() or digest(package_file) != expected):
            raise RuntimeError('release_exists_or_package_mismatch')
    sql_destination = MIGRATIONS / (name + '.sql')
    if args.sql_sha:
        if sql_destination.exists() or digest(sql_file) != args.sql_sha:
            raise RuntimeError('migration_exists_or_mismatch')
        result['migrations'] = [item[0] for item in planned_migrations(sql_file.read_text(encoding='utf-8-sig'))]
    created = []
    try:
        stage_files(args, target, web_package, gateway_package, sql_file, sql_destination, result, created)
    except BaseException:
        for path in created:
            if path.is_dir():
                shutil.rmtree(path, ignore_errors=True)
            else:
                path.unlink(missing_ok=True)
        raise
    print(json.dumps(result))


def stage_files(args, target, web_package, gateway_package, sql_file, sql_destination, result, created):
    if args.web_sha:
        destination = APP / target
        created.append(destination)
        extract(web_package, destination, args.web_sha, 'trobot')
        deps = json.loads((destination / 'TRobot.Web.deps.json').read_text(encoding='utf-8-sig'))
        if not deps['runtimeTarget']['name'].endswith('/linux-x64') or not (destination / 'TRobot.Web.dll').is_file():
            shutil.rmtree(destination)
            raise RuntimeError('web_runtime_mismatch')
        result['web'] = target
    if args.gateway_sha:
        destination = GATEWAY / target
        old = GATEWAY / current(GATEWAY)
        created.append(destination)
        extract(gateway_package, destination, args.gateway_sha, 'trobot-whatsapp')
        try:
            for file in ('package.json', 'package-lock.json'):
                if digest(destination / file) != digest(old / file):
                    raise RuntimeError('gateway_dependencies_changed')
            shutil.copytree(old / 'node_modules', destination / 'node_modules', symlinks=True)
            import grp
            group = grp.getgrnam('trobot-whatsapp').gr_gid
            for path in [destination / 'node_modules', *(destination / 'node_modules').rglob('*')]:
                os.chown(path, 0, group, follow_symlinks=False)
            for path in sorted((destination / 'src').glob('*.js')):
                run(str(NODE), '--check', str(path))
            code = ("import {createRequire} from 'node:module'; const require=createRequire("
                    + json.dumps(str(destination / 'package.json')) + "); "
                    "await import(require.resolve('@whiskeysockets/baileys')); "
                    "await import(require.resolve('qrcode')); await import(require.resolve('pino'));")
            run('/usr/sbin/runuser', '-u', 'trobot-whatsapp', '--', str(NODE), '--input-type=module', '-e', code)
        except BaseException:
            shutil.rmtree(destination)
            raise
        result['gateway'] = target
    if args.sql_sha:
        MIGRATIONS.mkdir(mode=0o750, exist_ok=True)
        shutil.chown(MIGRATIONS, user='root', group='trobot')
        created.append(sql_destination)
        shutil.copyfile(sql_file, sql_destination)
        shutil.chown(sql_destination, user='root', group='trobot')
        sql_destination.chmod(0o640)
        if digest(sql_destination) != args.sql_sha:
            raise RuntimeError('sql_copy_mismatch')


def activate(args):
    name = check_name(args.name)
    if not (args.web_sha or args.gateway_sha):
        raise RuntimeError('nothing_to_activate')
    context = PROVISIONING / (name + '-context.json')
    if context.exists():
        raise RuntimeError('context_exists')
    target = 'releases/' + name
    allow_changes = tuple(item for item in (args.allow_changes or '').split(',') if item)
    previous_web, previous_gateway = current(APP), current(GATEWAY)
    if args.web_sha and (APP / target / 'release.sha256').read_text().strip() != args.web_sha:
        raise RuntimeError('web_not_staged')
    if args.gateway_sha and (GATEWAY / target / 'release.sha256').read_text().strip() != args.gateway_sha:
        raise RuntimeError('gateway_not_staged')
    if target in (previous_web if args.web_sha else None, previous_gateway if args.gateway_sha else None):
        raise RuntimeError('release_already_current')
    sql_path = MIGRATIONS / (name + '.sql')
    if args.sql_sha and digest(sql_path) != args.sql_sha:
        raise RuntimeError('sql_not_staged')
    if not all(active(unit) for unit in SERVICES) or active('trobot-backup.service'):
        raise RuntimeError('services_not_ready')
    module = recovery()
    binding, identity_before, protected = module.binding(), identity(), protected_files()
    if not binding or not module.registered() or not gateway_health(module, binding):
        raise RuntimeError('gateway_not_connected')
    database()  # preflight: integrity and foreign keys checked while the site is still running

    PROVISIONING.mkdir(mode=0o700, parents=True, exist_ok=True)
    report = {'release': name, 'previous_web': previous_web, 'previous_gateway': previous_gateway,
              'web_sha256': args.web_sha, 'gateway_sha256': args.gateway_sha, 'sql_sha256': args.sql_sha,
              'allow_changes': list(allow_changes), 'completed': False}

    def save():
        context.write_text(json.dumps(report, indent=2))
        context.chmod(0o600)

    rehearsal = pathlib.Path('/var/lib/trobot') / (name + '-rehearsal.db')

    def remove_rehearsal():
        for suffix in ('', '-wal', '-shm', '-journal'):
            pathlib.Path(str(rehearsal) + suffix).unlink(missing_ok=True)

    stopped = False
    before = expected_after = expected_db = None

    def check_protected():
        if (not protected_preserved(protected, protected_files()) or identity() != identity_before
                or module.binding() != binding):
            raise RuntimeError('protected_state_changed')

    def start_and_verify():
        # Exact comparison only while nothing can write; afterwards the site serves requests.
        if expected_db is None or database() != expected_db:
            raise RuntimeError('database_changed_before_start')
        run('systemctl', 'start', 'trobot.service')
        module.wait_ready(module.web_ready, seconds=90)
        if structure(database()) != structure(expected_db):
            raise RuntimeError('schema_changed_on_start')
        check_protected()
        # trobot-whatsapp.service pulls trobot-whatsapp-resume.service through Wants=.
        run('systemctl', 'start', 'trobot-whatsapp.service')
        module.wait_ready(lambda: gateway_health(module, binding), seconds=240)
        check_protected()

    signal.signal(signal.SIGTERM, interrupt)
    signal.signal(signal.SIGHUP, interrupt)
    try:
        save()
        run('systemctl', 'stop', 'trobot-backup.timer')
        if active('trobot-backup.service'):
            raise RuntimeError('backup_running')
        stopped = True
        run('systemctl', 'stop', 'trobot-whatsapp-resume.service')
        run('systemctl', 'stop', 'trobot-whatsapp.service')
        run('systemctl', 'stop', 'trobot.service')
        if active('trobot.service') or active('trobot-whatsapp.service'):
            raise RuntimeError('services_still_active')
        before = expected_db = database()
        known = set(BACKUPS.iterdir()) if BACKUPS.exists() else set()
        run('/usr/bin/python3', str(OPS / 'backup.py'))
        created = set(BACKUPS.iterdir()) - known
        if len(created) != 1:
            raise RuntimeError('backup_not_created')
        backup = created.pop()
        if not (backup / 'trobot.db').is_file() or not (backup / 'whatsapp-state/auth/creds.json').is_file():
            raise RuntimeError('backup_incomplete')
        shutil.copytree('/etc/trobot', backup / 'web-config')
        for path in [backup, *backup.rglob('*')]:
            path.chmod(0o700 if path.is_dir() else 0o600)
        report.update(backup=str(backup), history_before=before['history'])
        save()
        if args.sql_sha:
            if rehearsal.exists():
                raise RuntimeError('rehearsal_exists')
            try:
                shutil.copyfile(backup / 'trobot.db', rehearsal)
                shutil.chown(rehearsal, user='trobot', group='trobot')
                rehearsal.chmod(0o600)
                if database(rehearsal) != before:
                    raise RuntimeError('rehearsal_copy_mismatch')
                expected_after = migrate(rehearsal, sql_path, args.sql_sha, allow_changes)
            finally:
                remove_rehearsal()
            if database() != before:
                raise RuntimeError('database_changed_during_rehearsal')
            report['rehearsal_passed'] = True
            save()
            after = migrate(DB, sql_path, args.sql_sha, allow_changes)
            expected_db = after
            if after != expected_after:
                raise RuntimeError('migration_differs_from_rehearsal')
            report.update(migration_applied=True, history_after=after['history'])
            save()
        if args.web_sha:
            switch(APP, target, name)
        if args.gateway_sha:
            switch(GATEWAY, target, name)
        start_and_verify()
        run('systemctl', 'start', 'trobot-backup.timer')
        if not all(active(unit) for unit in SERVICES):
            raise RuntimeError('services_not_active')
        report.update(completed=True, all_existing_rows_preserved=True, protected_files_preserved=True,
                      whatsapp_identity_preserved=True, original_group_connected=True)
        save()
    except BaseException as error:
        report['error'] = type(error).__name__ + ': ' + str(error)[:200]
        for number in (signal.SIGTERM, signal.SIGHUP, signal.SIGINT):
            signal.signal(number, signal.SIG_IGN)  # let the rollback finish
        try:
            if stopped:
                for unit in ('trobot-backup.timer', 'trobot-whatsapp-resume.service',
                             'trobot-whatsapp.service', 'trobot.service'):
                    run('systemctl', 'stop', unit)
                if current(APP) != previous_web:
                    switch(APP, previous_web, name + '-rollback')
                if current(GATEWAY) != previous_gateway:
                    switch(GATEWAY, previous_gateway, name + '-rollback')
                # Applied additive schema stays; never restore an old operational database automatically.
                state_now = database()
                accepted = [structure(value) for value in (before, expected_after, expected_db) if value is not None]
                if structure(state_now) not in accepted:
                    raise RuntimeError('unexpected_database_state')
                expected_db = state_now
                start_and_verify()
                report.update(rolled_back=True, gateway_connected=True)
            run('systemctl', 'start', 'trobot-backup.timer')
        except BaseException:
            # Fail closed: never leave an unverified gateway or backup cycle running.
            for unit in ('trobot-backup.timer', 'trobot-whatsapp-resume.service', 'trobot-whatsapp.service'):
                subprocess.run(['systemctl', 'stop', unit], capture_output=True, timeout=45, check=False)
            report['rollback_failed'] = True
            raise
        finally:
            save()
        raise
    print(json.dumps({key: value for key, value in report.items() if key not in ('backup', 'history_before', 'history_after')}))


def verify(args):
    name = check_name(args.name)
    report = json.loads((PROVISIONING / (name + '-context.json')).read_text())
    if not report.get('completed'):
        raise RuntimeError('release_not_completed')
    target = 'releases/' + name
    if report.get('web_sha256') and current(APP) != target:
        raise RuntimeError('web_not_current')
    if report.get('gateway_sha256') and current(GATEWAY) != target:
        raise RuntimeError('gateway_not_current')
    for unit in SERVICES:
        if not active(unit) or run('systemctl', 'is-enabled', unit) != 'enabled':
            raise RuntimeError('service_not_ready:' + unit)
    module = recovery()
    binding = module.binding()
    if not binding or not gateway_health(module, binding):
        raise RuntimeError('gateway_not_connected')
    snapshot = database()
    expected_history = report.get('history_after') or report.get('history_before')
    if snapshot['history'] != expected_history:
        raise RuntimeError('history_changed')
    restarts = {unit: int(run('systemctl', 'show', '-p', 'NRestarts', '--value', unit))
                for unit in ('trobot.service', 'trobot-whatsapp.service')}
    print(json.dumps({'release': name, 'web': current(APP), 'gateway': current(GATEWAY),
                      'services_active_and_enabled': True, 'original_group_connected': True,
                      'integrity_and_foreign_keys_ok': True, 'latest_migration': snapshot['history'][-1][0],
                      'tables': len(snapshot['tables']), 'restarts': restarts}))


def last_migration(_args):
    print(database()['history'][-1][0])


def main(argv):
    if argv[:1] == ['db-snapshot']:
        helper_snapshot(pathlib.Path(argv[1]))
        return
    if argv[:1] == ['db-migrate']:
        helper_migrate(pathlib.Path(argv[1]), pathlib.Path(argv[2]), check_hash(argv[3]),
                       tuple(item for item in argv[4].split(',') if item) if len(argv) > 4 else ())
        return
    if os.geteuid() != 0:
        raise SystemExit('Run as root through sudo.')
    os.umask(0o077)
    parser = argparse.ArgumentParser(description='T.Robot Google Cloud release')
    commands = parser.add_subparsers(dest='command', required=True)
    for command in ('stage', 'activate', 'verify', 'last-migration'):
        sub = commands.add_parser(command)
        if command != 'last-migration':
            sub.add_argument('--name', required=True)
        if command in ('stage', 'activate'):
            sub.add_argument('--web-sha', type=check_hash)
            sub.add_argument('--gateway-sha', type=check_hash)
            sub.add_argument('--sql-sha', type=check_hash)
        if command == 'stage':
            sub.add_argument('--staging', required=True)
        if command == 'activate':
            sub.add_argument('--allow-changes', default='')
    args = parser.parse_args(argv)
    lock = exclusive_lock() if args.command in ('stage', 'activate') else None  # noqa: F841 held until exit
    {'stage': stage, 'activate': activate, 'verify': verify, 'last-migration': last_migration}[args.command](args)


if __name__ == '__main__':
    main(sys.argv[1:])
