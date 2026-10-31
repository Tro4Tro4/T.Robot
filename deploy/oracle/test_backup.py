"""Offline backup verification; synthetic sessions and temporary SQLite only."""
import contextlib
import importlib.util
import io
import os
import pathlib
import shutil
import sqlite3
import subprocess
import tempfile
import unittest
from unittest.mock import patch

ROOT = pathlib.Path(__file__).resolve().parent
SPEC = importlib.util.spec_from_file_location('oracle_backup', ROOT / 'backup.py')
ops = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(ops)


class BackupTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        root = pathlib.Path(self.temp.name)
        self.data, self.destination = root / 'data', root / 'backups'
        self.state, self.config = root / 'state', root / 'config'
        self.data.mkdir()
        (self.data / 'keys').mkdir()
        (self.data / 'keys' / 'key.xml').write_bytes(b'synthetic-key')
        with contextlib.closing(sqlite3.connect(self.data / 'trobot.db')) as db:
            db.execute('CREATE TABLE test(Id INTEGER PRIMARY KEY, Value TEXT)')
            db.execute("INSERT INTO test VALUES(1,'preserved')")
            db.commit()
        self.output = io.StringIO()
        self.previous_umask = os.umask(0o077)
        self.addCleanup(os.umask, self.previous_umask)

    def gateway(self):
        (self.state / 'auth').mkdir(parents=True)
        (self.state / 'auth' / 'creds.json').write_bytes(b'{"registered":true,"synthetic":"session"}')
        (self.state / 'send-journal').mkdir()
        (self.state / 'send-journal' / 'receipt.json').write_bytes(b'{"status":"sent"}\n')
        self.config.mkdir()
        (self.config / 'gateway.json').write_bytes(b'{"WhatsAppGateway":{"Token":"synthetic-secret"}}\n')
        (self.config / 'localhost.crt').write_bytes(b'synthetic-ca\n')

    def backup(self):
        with contextlib.redirect_stdout(self.output):
            ops.backup(self.data, self.destination, self.state, self.config)
        return next(self.destination.iterdir())

    def test_m29_without_gateway_preserves_database_and_keys(self):
        with patch.object(ops.subprocess, 'run') as run:
            result = self.backup()
        run.assert_not_called()
        self.assertEqual(b'synthetic-key', (result / 'keys' / 'key.xml').read_bytes())
        self.assertFalse((result / 'whatsapp-state').exists())
        with contextlib.closing(sqlite3.connect(result / 'trobot.db')) as db:
            self.assertEqual([(1, 'preserved')], db.execute('SELECT * FROM test').fetchall())
            self.assertEqual([('ok',)], db.execute('pragma integrity_check').fetchall())

    def test_gateway_snapshot_is_byte_exact_and_no_secret_is_logged(self):
        self.gateway()
        with patch.object(ops.subprocess, 'run', return_value=subprocess.CompletedProcess([], 3, 'inactive\n')) as run:
            result = self.backup()
        self.assertEqual(3, run.call_count)
        for source in self.state.rglob('*'):
            if source.is_file():
                self.assertEqual(source.read_bytes(), (result / 'whatsapp-state' / source.relative_to(self.state)).read_bytes())
        for name in ('gateway.json', 'localhost.crt'):
            self.assertEqual((self.config / name).read_bytes(), (result / 'whatsapp-config' / name).read_bytes())
        self.assertNotIn('synthetic-secret', self.output.getvalue())
        self.assertNotIn('session', self.output.getvalue())
        if os.name != 'nt':
            for path in [result, *result.rglob('*')]:
                self.assertEqual(0o700 if path.is_dir() else 0o600, path.stat().st_mode & 0o777)

    def test_active_or_unknown_service_state_fails_before_backup(self):
        self.gateway()
        for code, state in [(0, 'active'), (3, 'activating'), (1, '')]:
            with self.subTest(state=state), patch.object(ops.subprocess, 'run',
                    return_value=subprocess.CompletedProcess([], code, state)):
                with self.assertRaises(RuntimeError):
                    self.backup()
        self.assertFalse(self.destination.exists())

    def test_consecutive_gateway_backups_have_distinct_timestamp_names(self):
        self.gateway()
        with patch.object(ops.subprocess, 'run', return_value=subprocess.CompletedProcess([], 3, 'inactive\n')):
            self.backup()
            self.backup()
        snapshots = list(self.destination.iterdir())
        self.assertEqual(2, len(snapshots))
        self.assertNotEqual(snapshots[0].name, snapshots[1].name)
        for snapshot in snapshots:
            self.assertRegex(snapshot.name, r'^\d{8}T\d{12}Z$')
            self.assertTrue((snapshot / 'trobot.db').is_file())
            self.assertTrue((snapshot / 'whatsapp-config' / 'localhost.crt').is_file())

    def test_gateway_reactivation_cleans_pending_backup(self):
        self.gateway()
        with patch.object(ops, 'require_gateway_stopped', side_effect=[None, None, RuntimeError('active')]):
            with self.assertRaises(RuntimeError):
                self.backup()
        self.assertEqual([], list(self.destination.iterdir()))

    def test_copy_failure_cleans_pending_backup(self):
        with patch.object(ops.shutil, 'copytree', side_effect=OSError('synthetic_failure')):
            with self.assertRaises(OSError):
                self.backup()
        self.assertEqual([], list(self.destination.iterdir()))

    def test_symlink_source_is_rejected(self):
        source = self.data / 'keys' / 'link'
        try:
            source.symlink_to(self.data / 'trobot.db')
        except OSError:
            self.skipTest('Symlink creation unavailable on this host')
        with self.assertRaises(RuntimeError):
            self.backup()
        self.assertFalse(self.destination.exists())

    def test_missing_gateway_configuration_prevents_incomplete_snapshot(self):
        self.gateway()
        (self.config / 'localhost.crt').unlink()
        with patch.object(ops, 'require_gateway_stopped'):
            with self.assertRaises(FileNotFoundError):
                self.backup()
        self.assertFalse(self.destination.exists())

    def test_production_snapshot_drops_database_privileges(self):
        target = pathlib.Path(self.temp.name) / 'snapshot.db'
        def child(command, **kwargs):
            self.assertEqual(['/usr/sbin/runuser', '-u', 'trobot', '--', '/usr/bin/python3', '-c'], command[:6])
            self.assertIn('?mode=ro', command[6])
            self.assertTrue(kwargs['capture_output'])
            shutil.copyfile(command[-2], command[-1])
        with patch.object(ops, 'PRODUCTION_DATA', self.data), patch.object(ops.os, 'fchown', create=True), \
                patch.object(ops.subprocess, 'run', side_effect=child) as run, \
                patch.object(ops.sqlite3, 'connect', side_effect=AssertionError('Root SQLite forbidden')):
            ops.snapshot(self.data / 'trobot.db', target)
        run.assert_called_once()
        self.assertEqual((self.data / 'trobot.db').read_bytes(), target.read_bytes())
        self.assertEqual([], list(self.data.glob('.backup-*')))

    def test_failed_production_snapshot_removes_temporary_database(self):
        target = pathlib.Path(self.temp.name) / 'snapshot.db'
        with patch.object(ops, 'PRODUCTION_DATA', self.data), patch.object(ops.os, 'fchown', create=True), \
                patch.object(ops.subprocess, 'run', side_effect=subprocess.CalledProcessError(1, [])):
            with self.assertRaises(subprocess.CalledProcessError):
                ops.snapshot(self.data / 'trobot.db', target)
        self.assertFalse(target.exists())
        self.assertEqual([], list(self.data.glob('.backup-*')))

    def test_backup_service_uses_gateway_wrapper(self):
        service = (ROOT / 'trobot-backup.service').read_text()
        self.assertIn('ExecStart=/bin/sh /opt/trobot/ops/backup-with-gateway.sh', service)


if __name__ == '__main__':
    unittest.main()
