"""Local driver for a T.Robot release on the Google Cloud VM (run from the repository root on the PC).

  python deploy/google/publish.py prepare  NAME [--gateway]   build, package, migration SQL from the VM state
  python deploy/google/publish.py upload   NAME               copy packages and release.py to the VM
  python deploy/google/publish.py stage    NAME               extract releases on the VM (no service change)
  python deploy/google/publish.py activate NAME [--allow-changes T1,T2]
  python deploy/google/publish.py verify   NAME
  python deploy/google/publish.py release  NAME [--allow-changes ...]  upload + stage + activate + verify + smoke

Private host settings live in .local/gcp/host.json (not versioned); material in .local/gcp/releases/NAME.
"""
import argparse
import hashlib
import json
import pathlib
import re
import shlex
import subprocess
import sys
import tarfile

ROOT = pathlib.Path(__file__).resolve().parents[2]
LOCAL = ROOT / '.local' / 'gcp'
HOST_FILE = LOCAL / 'host.json'
MIGRATIONS = ROOT / 'src/TRobot.Infrastructure/Persistence/Migrations'
GATEWAY_SOURCE = ROOT / 'gateway/TRobot.WhatsApp'
BLOCKED_PARTS = {'.local', 'state', 'auth', 'keys', 'node_modules'}
BLOCKED_SUFFIXES = ('.db', '.sqlite', '.sqlite3', '.pfx', '.pem', '.key', '.env')
NAME_PATTERN = re.compile(r'[a-z0-9][a-z0-9.-]{2,62}')


def sha(path):
    return hashlib.sha256(pathlib.Path(path).read_bytes()).hexdigest()


def host():
    if not HOST_FILE.is_file():
        raise SystemExit('Missing ' + str(HOST_FILE) + ': {"host": "...", "user": "...", '
                         '"key": "~/.ssh/google_compute_engine", "known_hosts": ".local/gcp/known_hosts"}')
    config = json.loads(HOST_FILE.read_text(encoding='utf-8-sig'))
    key = pathlib.Path(config['key']).expanduser()
    known = (ROOT / config['known_hosts']).resolve()
    options = ['-i', str(key), '-o', 'UserKnownHostsFile=' + str(known), '-o', 'StrictHostKeyChecking=yes',
               '-o', 'BatchMode=yes', '-o', 'ConnectTimeout=20']
    return config['user'] + '@' + config['host'], options, config['user']


def remote(command, timeout=900, quiet=False):
    target, options, _ = host()
    result = subprocess.run(['ssh', *options, target, command], capture_output=True, text=True, timeout=timeout)
    if result.returncode:
        # The remote script prints fixed labels only; no configuration or chat data.
        print(result.stdout[-3000:])
        print(result.stderr[-3000:], file=sys.stderr)
        raise SystemExit(result.returncode)
    if result.stdout.strip() and not quiet:
        print(result.stdout.strip())
    return result.stdout.strip()


def release_dir(name):
    if not NAME_PATTERN.fullmatch(name):
        raise SystemExit('Release name: lowercase letters, digits, dots and dashes (es. feature-20261126-x64).')
    return LOCAL / 'releases' / name


def material(name):
    return json.loads((release_dir(name) / 'material.json').read_text(encoding='utf-8'))


def safe(relative):
    name = relative.name.lower()
    return (relative.suffix.lower() not in BLOCKED_SUFFIXES and not name.endswith(('.db-wal', '.db-shm'))
            and not name.startswith(('appsettings.production', '.env')) and name not in ('gateway.json', 'creds.json')
            and not any(part.lower() in BLOCKED_PARTS for part in relative.parts))


def package(destination, base, paths):
    with tarfile.open(destination, 'x:gz') as archive:
        for path in paths:
            relative = path.relative_to(base)
            if path.is_symlink() or not path.is_file() or not safe(relative):
                raise SystemExit('File not allowed in package: ' + relative.as_posix())
            archive.add(path, arcname=relative.as_posix(), recursive=False)
    with tarfile.open(destination, 'r:gz') as archive:
        names = [member.name for member in archive.getmembers()]
    if sorted(names) != sorted(path.relative_to(base).as_posix() for path in paths):
        raise SystemExit('Package verification failed')
    return {'file': destination.name, 'sha256': sha(destination), 'bytes': destination.stat().st_size, 'files': len(paths)}


def latest_local_migration():
    names = sorted(path.stem for path in MIGRATIONS.glob('[0-9]*_*.cs') if not path.stem.endswith('.Designer'))
    return names[-1]


def git(*args):
    return subprocess.run(['git', '-c', 'safe.directory=' + ROOT.as_posix(), *args], cwd=ROOT,
                          capture_output=True, text=True, check=True).stdout.strip()


def prepare(args):
    folder = release_dir(args.name)
    if folder.exists():
        raise SystemExit('Release folder already exists: ' + str(folder))
    if git('status', '--porcelain', '--', 'src', 'gateway') and not args.allow_dirty:
        raise SystemExit('Uncommitted changes in src/ or gateway/: commit first or pass --allow-dirty.')
    folder.mkdir(parents=True)
    publish = folder / 'publish'
    subprocess.run(['dotnet', 'publish', str(ROOT / 'src/TRobot.Web/TRobot.Web.csproj'), '-c', 'Release',
                    '-r', 'linux-x64', '--self-contained', 'false', '-o', str(publish)], check=True, cwd=ROOT)
    deps = json.loads((publish / 'TRobot.Web.deps.json').read_text(encoding='utf-8-sig'))
    if not deps['runtimeTarget']['name'].endswith('/linux-x64'):
        raise SystemExit('Unexpected runtime target')
    data = {'release': args.name, 'commit': git('rev-parse', 'HEAD'),
            'web': package(folder / ('web-' + args.name + '.tar.gz'), publish,
                           sorted(path for path in publish.rglob('*') if path.is_file()))}
    if args.gateway:
        paths = sorted((GATEWAY_SOURCE / 'src').glob('*.js')) + [GATEWAY_SOURCE / 'package.json',
                                                                 GATEWAY_SOURCE / 'package-lock.json']
        data['gateway'] = package(folder / ('gateway-' + args.name + '.tar.gz'), GATEWAY_SOURCE, paths)
    remote_last = remote('sudo -n runuser -u trobot -- python3 -c ' + shlex.quote(
        "import sqlite3; db=sqlite3.connect('file:/var/lib/trobot/trobot.db?mode=ro',uri=True); "
        "print(db.execute('select max(MigrationId) from __EFMigrationsHistory').fetchone()[0])"), quiet=True)
    local_last = latest_local_migration()
    data.update(remote_migration=remote_last, local_migration=local_last)
    if remote_last > local_last:
        raise SystemExit('The VM database is ahead of this code (' + remote_last + ')')
    if remote_last != local_last:
        sql = folder / ('migration-' + args.name + '.sql')
        subprocess.run(['dotnet', 'ef', 'migrations', 'script', remote_last, local_last,
                        '--project', 'src/TRobot.Infrastructure', '--startup-project', 'src/TRobot.Infrastructure',
                        '-o', str(sql)], check=True, cwd=ROOT)
        data['migration'] = {'file': sql.name, 'sha256': sha(sql), 'from': remote_last, 'to': local_last}
    (folder / 'material.json').write_text(json.dumps(data, indent=2), encoding='utf-8')
    print(json.dumps({key: value for key, value in data.items()}, indent=2))


def hash_args(data):
    result = ['--web-sha', data['web']['sha256']]
    if 'gateway' in data:
        result += ['--gateway-sha', data['gateway']['sha256']]
    if 'migration' in data:
        result += ['--sql-sha', data['migration']['sha256']]
    return result


def upload(args):
    data, folder = material(args.name), release_dir(args.name)
    target, options, user = host()
    staging = '/home/' + user + '/trobot-staging/' + args.name
    files = []
    for key in ('web', 'gateway', 'migration'):
        if key in data:
            path = folder / data[key]['file']
            if sha(path) != data[key]['sha256']:
                raise SystemExit('Local file changed after prepare: ' + path.name)
            files.append(path)
    script = ROOT / 'deploy/google/release.py'
    remote('mkdir -p -m 700 ' + shlex.quote(staging))
    subprocess.run(['scp', '-q', *options, *map(str, files), str(script), target + ':' + staging + '/'],
                   check=True, timeout=600)
    installed = remote('sudo -n install -o root -g trobot -m 0640 ' + shlex.quote(staging + '/release.py')
                       + ' /opt/trobot/ops/release.py && sudo -n sha256sum /opt/trobot/ops/release.py', quiet=True)
    if installed.split()[0] != sha(script):
        raise SystemExit('release.py hash mismatch on the VM')
    print(json.dumps({'uploaded': [path.name for path in files], 'services_changed': False}))


def remote_release(command, name, extra=()):
    remote(shlex.join(['sudo', '-n', '/usr/bin/python3', '/opt/trobot/ops/release.py', command, '--name', name, *extra]))


def main():
    parser = argparse.ArgumentParser(description='T.Robot Google Cloud release driver')
    parser.add_argument('command', choices=['prepare', 'upload', 'stage', 'activate', 'verify', 'release'])
    parser.add_argument('name')
    parser.add_argument('--gateway', action='store_true', help='prepare: include the gateway sources')
    parser.add_argument('--allow-dirty', action='store_true', help='prepare: allow uncommitted src/gateway changes')
    parser.add_argument('--allow-changes', default='', help='activate: tables whose rows a migration may update')
    args = parser.parse_args()
    release_dir(args.name)
    if args.command == 'prepare':
        return prepare(args)
    if args.command in ('upload', 'release'):
        upload(args)
    _, _, user = host()
    if args.command in ('stage', 'release'):
        remote_release('stage', args.name, hash_args(material(args.name))
                       + ['--staging', '/home/' + user + '/trobot-staging/' + args.name])
    if args.command in ('activate', 'release'):
        extra = hash_args(material(args.name)) + (['--allow-changes', args.allow_changes] if args.allow_changes else [])
        remote_release('activate', args.name, extra)
    if args.command in ('verify', 'release'):
        remote_release('verify', args.name)
    if args.command == 'release':
        subprocess.run([sys.executable, str(ROOT / 'deploy/google/smoke.py')], check=True)


if __name__ == '__main__':
    main()
