"""Offline recovery checks: no WhatsApp connections or real systemd actions."""
import base64
import contextlib
import importlib.util
import io
import json
import pathlib
import sqlite3
import subprocess
import tempfile
import unittest
from unittest.mock import patch

ROOT = pathlib.Path(__file__).resolve().parent
SPEC = importlib.util.spec_from_file_location('gateway_resume', ROOT / 'resume-gateway.py')
ops = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(ops)
BINDING = ('connection', 'event', '12345@g.us', 'actor', 3, '2026-10-19')
SECRET = 'a' * 64


class BindingTests(unittest.TestCase):
    def test_database_reader_runs_as_owner_readonly_and_captures_output(self):
        with patch.object(ops.subprocess, 'run', return_value=subprocess.CompletedProcess([], 0, json.dumps([BINDING]))) as run:
            self.assertEqual(BINDING, ops.binding())
        args, kwargs = run.call_args
        self.assertEqual(['/usr/sbin/runuser', '-u', 'trobot', '--', '/usr/bin/python3', '-c'], args[0][:6])
        self.assertTrue(kwargs['capture_output'])
        self.assertIn('?mode=ro', args[0][6])
        self.assertIn('query_only=ON', args[0][6])

    def test_query_requires_enabled_administrator_and_current_event_assignment(self):
        with tempfile.TemporaryDirectory() as directory:
            with contextlib.closing(sqlite3.connect(pathlib.Path(directory) / 'test.db')) as db:
                db.executescript('''
                    CREATE TABLE WhatsAppConnections(Id,EventId,GroupId,ActorId,Revision,ActivatedAtUtc,Enabled);
                    CREATE TABLE AspNetUsers(PersonId,IsEnabled,IsSystemAdministrator);
                    CREATE TABLE EventOrganizers(EventId,PersonId,IsActive);
                ''')
                db.execute('INSERT INTO WhatsAppConnections VALUES(?,?,?,?,?,?,1)', BINDING)
                db.execute("INSERT INTO AspNetUsers VALUES('actor',1,1)")
                db.execute("INSERT INTO EventOrganizers VALUES('event','actor',1)")
                self.assertEqual([BINDING], db.execute(ops.QUERY).fetchall())
                for table, field in [('AspNetUsers', 'IsEnabled'), ('AspNetUsers', 'IsSystemAdministrator'),
                                     ('EventOrganizers', 'IsActive'), ('WhatsAppConnections', 'Enabled')]:
                    db.execute(f'UPDATE {table} SET {field}=0')
                    self.assertEqual([], db.execute(ops.QUERY).fetchall())
                    db.execute(f'UPDATE {table} SET {field}=1')
                db.execute("UPDATE EventOrganizers SET EventId='other-event'")
                self.assertEqual([], db.execute(ops.QUERY).fetchall())

    def test_missing_and_ambiguous_bindings(self):
        for rows, expected in [([], None), ([BINDING, BINDING], ValueError),
                               ([(*BINDING[:2], 'private@s.whatsapp.net', *BINDING[3:])], ValueError)]:
            with self.subTest(rows=rows), patch.object(ops.subprocess, 'run',
                    return_value=subprocess.CompletedProcess([], 0, json.dumps(rows))):
                if expected is ValueError:
                    with self.assertRaises(ValueError):
                        ops.binding()
                else:
                    self.assertIsNone(ops.binding())

    def test_only_existing_paired_identity_can_resume_regardless_of_registered_flag(self):
        account = {field: {'type': 'Buffer', 'data': base64.b64encode(b'x' * size).decode('ascii')}
                   for field, size in [('details', 4), ('accountSignatureKey', 32),
                                       ('accountSignature', 64), ('deviceSignature', 64)]}
        paired = {'me': {'id': '123456789:17@s.whatsapp.net'}, 'account': account}
        protobuf_account = {field: value['data'] for field, value in account.items()}
        protobuf_paired = {**paired, 'account': protobuf_account, 'registered': False}
        with tempfile.TemporaryDirectory() as directory:
            creds = pathlib.Path(directory) / 'creds.json'
            with patch.object(ops, 'CREDS', creds):
                self.assertFalse(ops.registered())
                for value, expected in [({}, False), ({'registered': False}, False),
                                        ({'registered': 'true'}, False), ({'registered': True}, False),
                                        ({**paired, 'registered': False}, True),
                                        ({**paired, 'registered': True}, True), (paired, True),
                                        (protobuf_paired, True),
                                        ({**protobuf_paired, 'account': {**protobuf_account, 'accountSignature': 'bad!'}}, False),
                                        ({**protobuf_paired, 'account': {**protobuf_account, 'deviceSignature': protobuf_account['details']}}, False),
                                        ({**paired, 'me': {'id': '123@g.us'}}, False),
                                        ({**paired, 'me': {'id': ''}}, False),
                                        ({**paired, 'me': []}, False),
                                        ({**paired, 'me': {'id': 123}}, False),
                                        ({**paired, 'account': {}}, False),
                                        ({**paired, 'account': None}, False),
                                        ({**paired, 'account': {**account, 'details': {'type': 'Buffer', 'data': ''}}}, False),
                                        ({**paired, 'account': {**account, 'deviceSignature': {'type': 'Buffer', 'data': 'bad!'}}}, False),
                                        ({**paired, 'account': {**account, 'accountSignatureKey': account['details']}}, False)]:
                    creds.write_text(json.dumps(value), encoding='utf-8')
                    self.assertEqual(expected, ops.registered())
                creds.write_text('{malformed', encoding='utf-8')
                self.assertFalse(ops.registered())


class RecoveryTests(unittest.TestCase):
    def setUp(self):
        self.stack = contextlib.ExitStack()
        self.addCleanup(self.stack.close)
        self.stack.enter_context(patch.object(ops, 'token', return_value=SECRET))
        self.binding = self.stack.enter_context(patch.object(ops, 'binding', return_value=BINDING))
        self.registered = self.stack.enter_context(patch.object(ops, 'registered', return_value=True))
        self.stack.enter_context(patch.object(ops, 'web_ready', return_value=True))
        self.stack.enter_context(patch.object(ops.time, 'sleep'))
        self.calls = []
        self.health = 'disconnected'
        self.request = self.stack.enter_context(patch.object(ops, 'request', side_effect=self.gateway))
        self.output = self.stack.enter_context(contextlib.redirect_stdout(io.StringIO()))

    def gateway(self, secret, path, payload=None):
        self.calls.append((path, payload))
        self.assertEqual(SECRET, secret)
        if path == '/connect':
            self.health = 'connected'
        if path == '/disconnect':
            self.health = 'disconnected'
        return {'status': self.health, 'activeGroupId': BINDING[2] if path == '/group' else None}

    def test_restores_existing_group_without_sending(self):
        self.assertEqual(0, ops.main())
        self.assertEqual(['/health', '/connect', '/health', '/group'], [x[0] for x in self.calls])
        self.assertEqual({'groupId': BINDING[2]}, self.calls[-1][1])
        self.assertNotIn(BINDING[2], self.output.getvalue())
        self.assertNotIn(SECRET, self.output.getvalue())

    def test_missing_binding_or_session_skips_successfully(self):
        self.binding.return_value = None
        self.assertEqual(0, ops.main())
        self.request.assert_not_called()
        self.binding.return_value = BINDING
        self.registered.return_value = False
        self.assertEqual(0, ops.main())
        self.request.assert_not_called()

    def test_binding_change_after_connect_disconnects_without_pinning(self):
        self.binding.side_effect = [BINDING, BINDING, None]
        self.assertEqual(1, ops.main())
        self.assertEqual(['/health', '/connect', '/health', '/disconnect'], [x[0] for x in self.calls])

    def test_binding_change_after_pin_disconnects(self):
        self.binding.side_effect = [BINDING, BINDING, BINDING, (*BINDING[:4], 4, BINDING[5])]
        self.assertEqual(1, ops.main())
        self.assertEqual('/disconnect', self.calls[-1][0])

    def test_revoked_session_after_connect_disconnects(self):
        self.registered.side_effect = [True, True, False]
        self.assertEqual(1, ops.main())
        self.assertEqual('/disconnect', self.calls[-1][0])

    def test_qr_never_pins_or_logs_response(self):
        def qr(secret, path, payload=None):
            result = self.gateway(secret, path, payload)
            if path == '/connect':
                self.health = 'qr'
            return result
        self.request.side_effect = qr
        self.assertEqual(1, ops.main())
        self.assertEqual('/disconnect', self.calls[-1][0])
        self.assertFalse(any(path == '/group' for path, _ in self.calls))

    def test_disconnect_failure_stops_gateway_without_exception_details(self):
        self.binding.side_effect = [BINDING, BINDING, None]
        def failed(secret, path, payload=None):
            if path == '/disconnect':
                raise RuntimeError(SECRET + BINDING[2])
            return self.gateway(secret, path, payload)
        self.request.side_effect = failed
        with patch.object(ops.subprocess, 'run') as run:
            self.assertEqual(1, ops.main())
        self.assertEqual(['/usr/bin/systemctl', '--no-block', 'stop', 'trobot-whatsapp.service'], run.call_args.args[0])
        self.assertNotIn(SECRET, self.output.getvalue())
        self.assertNotIn(BINDING[2], self.output.getvalue())

    def test_readiness_timeout_is_bounded(self):
        with patch.object(ops.time, 'monotonic', side_effect=[0, 0, 46]):
            with self.assertRaises(RuntimeError):
                ops.wait_ready(lambda: False)

    def test_web_failure_does_not_open_socket(self):
        with patch.object(ops, 'wait_ready', side_effect=RuntimeError('certificate_failure')):
            self.assertEqual(1, ops.main())
        self.request.assert_not_called()


class ServiceTests(unittest.TestCase):
    def test_node_starts_without_connect_flag_and_recovery_is_transient(self):
        service = (ROOT / 'trobot-whatsapp.service').read_text()
        resume = (ROOT / 'trobot-whatsapp-resume.service').read_text()
        self.assertIn('Wants=network-online.target trobot-whatsapp-resume.service', service)
        self.assertIn('User=trobot-whatsapp', service)
        self.assertIn('MemoryMax=256M', service)
        self.assertIn('ReadWritePaths=/var/lib/trobot-whatsapp', service)
        self.assertIn('PartOf=trobot-whatsapp.service', resume)
        self.assertIn('Type=oneshot', resume)
        self.assertNotIn('RemainAfterExit', resume)


if __name__ == '__main__':
    unittest.main()
