"""Cold export from the current operational Oracle host. No chat or secret output."""
import hashlib
import importlib.util
import json
import os
import pathlib
import shutil
import subprocess
import tarfile

os.umask(0o077)
work = pathlib.Path('/root/trobot-provisioning/azure-cutover')
archive = pathlib.Path('/home/opc/trobot-staging/azure-cutover.tar.gz')
assert not work.exists() and not archive.exists(), 'Existing cutover must be reviewed first'
assert pathlib.Path('/opt/trobot/app/current').resolve().name == 'm32'
services = ['trobot.service', 'caddy.service', 'trobot-whatsapp.service', 'trobot-backup.timer']
def systemctl(*args):
    return subprocess.run(['systemctl', *args], check=True, capture_output=True, text=True)
states = {s: {'active': subprocess.run(['systemctl','is-active','--quiet',s]).returncode == 0,
              'enabled': subprocess.run(['systemctl','is-enabled','--quiet',s]).returncode == 0} for s in services}
work.mkdir(mode=0o700)
(work/'context.json').write_text(json.dumps({'original_states': states, 'phase': 'exporting'}))
try:
    # Timer first, then wait for any already-running backup before freezing all writers.
    systemctl('stop', 'trobot-backup.timer', 'trobot-backup.service')
    systemctl('stop', 'trobot-whatsapp.service', 'trobot.service', 'caddy.service')
    for service in services:
        assert subprocess.run(['systemctl','is-active','--quiet',service]).returncode != 0
    spec = importlib.util.spec_from_file_location('backup','/opt/trobot/ops/backup.py')
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    destination = work/'backup'
    module.backup(destination=destination)
    backup = next(destination.iterdir())
    tree = work/'transfer'
    tree.mkdir(mode=0o700)
    shutil.copytree(backup, tree/'backup')
    paths = {
        'web-config': pathlib.Path('/etc/trobot'),
        'caddy-config': pathlib.Path('/etc/caddy'),
        'caddy-state': pathlib.Path('/var/lib/caddy'),
    }
    for name, path in paths.items():
        module.reject_links(path)
        shutil.copytree(path, tree/name)
    (tree/'units').mkdir()
    for name in [*services, 'trobot-whatsapp-resume.service', 'trobot-backup.service']:
        shutil.copyfile(pathlib.Path('/etc/systemd/system')/name, tree/'units'/name)
    (tree/'ops').mkdir()
    for name in ['backup.py','backup-with-gateway.sh','resume-gateway.py']:
        shutil.copyfile(pathlib.Path('/opt/trobot/ops')/name, tree/'ops'/name)
    # Tie all private material and the DB snapshot to one immutable export.
    manifest = {p.relative_to(tree).as_posix(): hashlib.sha256(p.read_bytes()).hexdigest()
                for p in sorted(tree.rglob('*')) if p.is_file()}
    (tree/'manifest.json').write_text(json.dumps(manifest, sort_keys=True))
    module.private_permissions(tree)
    with tarfile.open(archive, 'x:gz') as package:
        for path in sorted(tree.rglob('*')):
            if path.is_file(): package.add(path, arcname=path.relative_to(tree), recursive=False)
    archive.chmod(0o600)
    shutil.chown(archive, user='opc', group='opc')
    # Prevent accidental concurrent writers or gateway reconnection on an Oracle reboot.
    systemctl('disable', *services)
    (work/'context.json').write_text(json.dumps({'original_states': states, 'phase': 'frozen',
        'archive_sha256': hashlib.sha256(archive.read_bytes()).hexdigest()}))
    print('Oracle cold export verified; original services stopped and disabled.')
    print('archive_sha256', hashlib.sha256(archive.read_bytes()).hexdigest())
except BaseException:
    # No target activation is possible until this export succeeds.
    for service, state in states.items():
        if state['enabled']: subprocess.run(['systemctl','enable',service], capture_output=True)
        if state['active'] and service != 'trobot-whatsapp.service':
            subprocess.run(['systemctl','start',service], capture_output=True)
    raise
