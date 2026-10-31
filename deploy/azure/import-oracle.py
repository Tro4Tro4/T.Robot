"""Verify a private cold export before installing it on an empty Azure host."""
import hashlib
import json
import os
import pathlib
import shutil
import sqlite3
import subprocess
import sys
import tarfile

os.umask(0o077)
source = pathlib.Path('/home/trobotadmin/trobot-staging/azure-cutover.tar.gz')
expected_hash = sys.argv[1]
assert len(expected_hash) == 64 and hashlib.sha256(source.read_bytes()).hexdigest() == expected_hash
assert not pathlib.Path('/var/lib/trobot/trobot.db').exists()
assert not pathlib.Path('/var/lib/trobot-whatsapp/state').exists()
assert not pathlib.Path('/etc/trobot').exists()
tree = pathlib.Path('/root/trobot-provisioning/oracle-import')
tree.mkdir(mode=0o700)
allowed = {'backup','web-config','caddy-config','caddy-state','units','ops','manifest.json'}
with tarfile.open(source) as package:
    names = set()
    for member in package.getmembers():
        path = pathlib.PurePosixPath(member.name)
        assert not path.is_absolute() and '..' not in path.parts and path.parts[0] in allowed
        assert member.isfile() and member.name not in names
        names.add(member.name)
    package.extractall(tree, filter='data')
expected = json.loads((tree/'manifest.json').read_text())
actual = {p.relative_to(tree).as_posix(): hashlib.sha256(p.read_bytes()).hexdigest()
          for p in tree.rglob('*') if p.is_file() and p != tree/'manifest.json'}
assert actual == expected, 'Cold export manifest mismatch'
with sqlite3.connect('file:' + str(tree/'backup/trobot.db') + '?mode=ro', uri=True) as db:
    assert db.execute('PRAGMA integrity_check').fetchall() == [('ok',)]
    assert db.execute('PRAGMA foreign_key_check').fetchall() == []
    assert db.execute('SELECT MigrationId FROM __EFMigrationsHistory ORDER BY MigrationId DESC LIMIT 1').fetchone()[0] == '20261023092207_AddHouseholdRenameFingerprint'
assert json.loads((tree/'backup/whatsapp-state/auth/creds.json').read_text(encoding='utf-8-sig')).get('me',{}).get('id')
for name in ['trobot.service','caddy.service','trobot-whatsapp.service','trobot-whatsapp-resume.service','trobot-backup.service','trobot-backup.timer']:
    path = tree/'units'/name
    contents = path.read_text()
    if name == 'trobot-whatsapp.service':
        assert 'node-v24.22.0-linux-x64' in contents
        contents = contents.replace('node-v24.22.0-linux-x64','node-v24.22.0-linux-arm64')
    target = pathlib.Path('/etc/systemd/system')/name
    assert not target.exists()
    target.write_text(contents)
    target.chmod(0o644)
def copy_tree(source_path, target_path, user, group, dir_mode=0o700, file_mode=0o600):
    shutil.copytree(source_path, target_path, dirs_exist_ok=True)
    for p in [target_path, *target_path.rglob('*')]:
        assert not p.is_symlink()
        shutil.chown(p,user=user,group=group)
        p.chmod(dir_mode if p.is_dir() else file_mode)
copy_tree(tree/'web-config', pathlib.Path('/etc/trobot'), 'root','trobot',0o750,0o640)
(pathlib.Path('/etc/trobot')/'trobot.env').chmod(0o600)
(pathlib.Path('/etc/trobot/tls')/'localhost.key').chmod(0o600)
shutil.copyfile(tree/'backup/trobot.db','/var/lib/trobot/trobot.db')
shutil.chown('/var/lib/trobot/trobot.db',user='trobot',group='trobot')
pathlib.Path('/var/lib/trobot/trobot.db').chmod(0o600)
copy_tree(tree/'backup/keys', pathlib.Path('/var/lib/trobot/keys'),'trobot','trobot')
copy_tree(tree/'backup/whatsapp-state', pathlib.Path('/var/lib/trobot-whatsapp/state'),'trobot-whatsapp','trobot-whatsapp')
copy_tree(tree/'backup/whatsapp-config', pathlib.Path('/etc/trobot-whatsapp'),'root','trobot-whatsapp',0o750,0o640)
copy_tree(tree/'caddy-config', pathlib.Path('/etc/caddy'),'root','caddy',0o750,0o640)
copy_tree(tree/'caddy-state', pathlib.Path('/var/lib/caddy'),'caddy','caddy')
copy_tree(tree/'ops', pathlib.Path('/opt/trobot/ops'),'root','trobot',0o750,0o750)
assert hashlib.sha256(pathlib.Path('/var/lib/trobot/trobot.db').read_bytes()).hexdigest() == expected['backup/trobot.db']
subprocess.run(['systemctl','daemon-reload'],check=True)
subprocess.run(['systemd-analyze','verify',*[str(pathlib.Path('/etc/systemd/system')/name) for name in ['trobot.service','caddy.service','trobot-whatsapp.service','trobot-whatsapp-resume.service','trobot-backup.service','trobot-backup.timer']]],check=True)
print('Cold export, SQLite, private keys and WhatsApp session verified and installed; services remain stopped.')
