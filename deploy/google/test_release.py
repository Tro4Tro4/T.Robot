"""Offline checks of deploy/google/release.py database helpers; temporary SQLite only."""
import contextlib
import importlib.util
import io
import json
import pathlib
import sqlite3
import tempfile
import unittest

ROOT = pathlib.Path(__file__).resolve().parent
SPEC = importlib.util.spec_from_file_location('google_release', ROOT / 'release.py')
release = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(release)

BASE_SCHEMA = '''
CREATE TABLE "__EFMigrationsHistory" ("MigrationId" TEXT NOT NULL PRIMARY KEY, "ProductVersion" TEXT NOT NULL);
CREATE TABLE "Events" ("Id" TEXT NOT NULL PRIMARY KEY, "Name" TEXT NOT NULL);
CREATE TABLE "Dialogues" ("Id" INTEGER NOT NULL PRIMARY KEY, "EventId" TEXT NOT NULL REFERENCES "Events" ("Id") ON DELETE CASCADE, "Text" TEXT NOT NULL);
INSERT INTO "__EFMigrationsHistory" VALUES ('20261031205555_AddMealPreferences', '10.0.6');
INSERT INTO "Events" VALUES ('fc3dfb11-fbcf-46e9-b677-9521e1c07f03', 'Capodanno 2027');
INSERT INTO "Dialogues" VALUES (1, 'fc3dfb11-fbcf-46e9-b677-9521e1c07f03', 'uno'), (2, 'fc3dfb11-fbcf-46e9-b677-9521e1c07f03', 'due');
'''
HISTORY = ('INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")\n'
           "VALUES ('{0}', '10.0.6');\n")


def script(body, migration='20261108152657_AddFamilyStatus'):
    return 'BEGIN TRANSACTION;\n' + body + '\n' + HISTORY.format(migration) + '\nCOMMIT;\n'


class ReleaseDatabaseTests(unittest.TestCase):
    def setUp(self):
        temp = tempfile.TemporaryDirectory()
        self.addCleanup(temp.cleanup)
        self.root = pathlib.Path(temp.name)
        self.db = self.root / 'trobot.db'
        with contextlib.closing(sqlite3.connect(self.db)) as db:
            db.executescript(BASE_SCHEMA)

    def snapshot(self):
        output = io.StringIO()
        with contextlib.redirect_stdout(output):
            release.helper_snapshot(self.db)
        return json.loads(output.getvalue())

    def migrate(self, text, allow=()):
        sql = self.root / 'migration.sql'
        sql.write_text(text, encoding='utf-8')
        output = io.StringIO()
        with contextlib.redirect_stdout(output):
            release.helper_migrate(self.db, sql, release.digest(sql), allow)
        return json.loads(output.getvalue())

    def test_additive_column_preserves_rows_and_appends_history(self):
        before = self.snapshot()
        after = self.migrate(script('ALTER TABLE "Dialogues" ADD "FamilyStatus" INTEGER NULL;'))
        self.assertEqual(after['history'][-1], ['20261108152657_AddFamilyStatus', '10.0.6'])
        self.assertEqual(after['tables']['Dialogues']['count'], 2)
        self.assertEqual(after['tables']['Events'], before['tables']['Events'])
        self.assertEqual(after, self.snapshot())

    def test_ef_table_rebuild_of_referenced_parent_keeps_children(self):
        rebuild = '''PRAGMA foreign_keys = 0;
CREATE TABLE "ef_temp_Events" ("Id" TEXT NOT NULL PRIMARY KEY, "Name" TEXT NOT NULL, "Notes" TEXT NULL);
INSERT INTO "ef_temp_Events" ("Id", "Name") SELECT "Id", "Name" FROM "Events";
DROP TABLE "Events";
ALTER TABLE "ef_temp_Events" RENAME TO "Events";
PRAGMA foreign_keys = 1;'''
        after = self.migrate(script(rebuild))
        self.assertEqual(after['tables']['Dialogues']['count'], 2)
        self.assertEqual(after['tables']['Events']['count'], 1)

    def test_data_update_is_rejected_unless_allowed(self):
        text = script('UPDATE "Dialogues" SET "Text" = upper("Text");')
        before = self.snapshot()
        with self.assertRaisesRegex(RuntimeError, 'rows_changed:Dialogues'):
            self.migrate(text)
        self.assertEqual(self.snapshot(), before)
        after = self.migrate(text, allow=('Dialogues',))
        self.assertEqual(after['tables']['Dialogues']['count'], 2)

    def test_dropped_column_and_deleted_rows_are_rejected(self):
        before = self.snapshot()
        with self.assertRaisesRegex(RuntimeError, 'column_removed:Dialogues'):
            self.migrate(script('ALTER TABLE "Dialogues" DROP COLUMN "Text";'))
        with self.assertRaisesRegex(RuntimeError, 'row_count_changed:Dialogues'):
            self.migrate(script('DELETE FROM "Dialogues" WHERE "Id" = 2;'), allow=('Dialogues',))
        self.assertEqual(self.snapshot(), before)

    def test_foreign_key_violation_is_rolled_back(self):
        before = self.snapshot()
        with self.assertRaisesRegex(RuntimeError, 'foreign_keys_failed'):
            self.migrate(script('CREATE TABLE "Notes" ("Id" INTEGER PRIMARY KEY, "EventId" TEXT NOT NULL '
                                'REFERENCES "Events" ("Id"));\nINSERT INTO "Notes" VALUES (1, \'missing\');'))
        self.assertEqual(self.snapshot(), before)

    def test_repeated_or_older_migration_is_rejected(self):
        text = script('ALTER TABLE "Dialogues" ADD "FamilyStatus" INTEGER NULL;')
        self.migrate(text)
        with self.assertRaisesRegex(RuntimeError, 'migration_already_applied'):
            self.migrate(text)
        with self.assertRaisesRegex(RuntimeError, 'migration_out_of_order'):
            self.migrate(script('SELECT 1;', '20261001000000_Older'))

    def test_hash_mismatch_is_rejected(self):
        sql = self.root / 'migration.sql'
        sql.write_text(script('SELECT 1;'), encoding='utf-8')
        with self.assertRaisesRegex(RuntimeError, 'sql_hash_mismatch'):
            release.helper_migrate(self.db, sql, '0' * 64, ())

    def test_removed_scenario_is_refused(self):
        with contextlib.closing(sqlite3.connect(self.db)) as db:
            db.execute("INSERT INTO Events VALUES ('CE014F6D-1A90-4388-B187-771235BCB918', 'SIMULAZIONE')")
            db.commit()
        with self.assertRaisesRegex(RuntimeError, 'removed_scenario_present'):
            self.snapshot()

    def test_dropped_index_or_foreign_key_is_rejected(self):
        with contextlib.closing(sqlite3.connect(self.db)) as db:
            db.execute('CREATE INDEX "IX_Dialogues_EventId" ON "Dialogues" ("EventId")')
            db.commit()
        before = self.snapshot()
        with self.assertRaisesRegex(RuntimeError, 'schema_object_removed:IX_Dialogues_EventId'):
            self.migrate(script('DROP INDEX "IX_Dialogues_EventId";'))
        rebuild_without_fk = (
            'PRAGMA foreign_keys = 0;\n'
            'CREATE TABLE "ef_temp_Dialogues" ("Id" INTEGER NOT NULL PRIMARY KEY, "EventId" TEXT NOT NULL, "Text" TEXT NOT NULL);\n'
            'INSERT INTO "ef_temp_Dialogues" SELECT "Id", "EventId", "Text" FROM "Dialogues";\n'
            'DROP TABLE "Dialogues";\n'
            'ALTER TABLE "ef_temp_Dialogues" RENAME TO "Dialogues";\n'
            'CREATE INDEX "IX_Dialogues_EventId" ON "Dialogues" ("EventId");\n'
            'PRAGMA foreign_keys = 1;')
        with self.assertRaisesRegex(RuntimeError, 'foreign_key_removed:Dialogues'):
            self.migrate(script(rebuild_without_fk))
        self.assertEqual(self.snapshot(), before)

    def test_structure_ignores_rows_written_by_running_services(self):
        before = self.snapshot()
        with contextlib.closing(sqlite3.connect(self.db)) as db:
            db.execute("INSERT INTO Dialogues VALUES (3, 'fc3dfb11-fbcf-46e9-b677-9521e1c07f03', 'login')")
            db.commit()
        after = self.snapshot()
        self.assertNotEqual(after, before)
        self.assertEqual(release.structure(after), release.structure(before))


class ProtectionTests(unittest.TestCase):
    def test_only_new_data_protection_keys_may_appear(self):
        original = {'/etc/trobot/trobot.env': 'a', '/var/lib/trobot/keys/key-1.xml': 'b'}
        self.assertTrue(release.protected_preserved(original, dict(original)))
        self.assertTrue(release.protected_preserved(original, {**original, '/var/lib/trobot/keys/key-2.xml': 'c'}))
        self.assertFalse(release.protected_preserved(original, {**original, '/etc/trobot/other.env': 'c'}))
        self.assertFalse(release.protected_preserved(original, {**original, '/var/lib/trobot/keys/key-1.xml': 'x'}))
        self.assertFalse(release.protected_preserved(original, {'/etc/trobot/trobot.env': 'a'}))

    def test_migrations_directory_is_made_readable_for_the_database_user(self):
        # umask 077 riduce mkdir(mode=0o750) a 700: senza chmod esplicito trobot non legge lo SQL.
        source = pathlib.Path(release.__file__).read_text(encoding='utf-8')
        self.assertIn('MIGRATIONS.chmod(0o750)', source)

    def test_gateway_errors_mean_not_ready(self):
        class Gateway:
            def __init__(self, error=None, health=None):
                self.error, self.health = error, health
            def token(self):
                return 'x'
            def request(self, _token, _path):
                if self.error:
                    raise self.error
                return self.health
        binding = [(1, 2, '123-456@g.us')]
        self.assertFalse(release.gateway_health(Gateway(RuntimeError('gateway_unavailable')), binding))
        self.assertFalse(release.gateway_health(Gateway(ValueError('json')), binding))
        self.assertFalse(release.gateway_health(Gateway(health={'status': 'connected', 'activeGroupId': None}), binding))
        self.assertTrue(release.gateway_health(Gateway(health={'status': 'connected', 'activeGroupId': '123-456@g.us'}), binding))
        two = [(1, 2, 'a-1@g.us'), (3, 4, 'b-2@g.us')]
        both = {'status': 'connected', 'activeGroupIds': ['a-1@g.us', 'b-2@g.us'], 'unavailableGroupIds': []}
        self.assertTrue(release.gateway_health(Gateway(health=both), two))
        one_down = {'status': 'connected', 'activeGroupIds': ['a-1@g.us'], 'unavailableGroupIds': ['b-2@g.us']}
        self.assertTrue(release.gateway_health(Gateway(health=one_down), two))
        self.assertFalse(release.gateway_health(Gateway(health={'status': 'connected', 'activeGroupIds': ['a-1@g.us'], 'unavailableGroupIds': []}), two))
        self.assertFalse(release.gateway_health(Gateway(health={'status': 'connected', 'activeGroupIds': [], 'unavailableGroupIds': ['a-1@g.us', 'b-2@g.us']}), two))
        self.assertFalse(release.gateway_health(Gateway(health={'status': 'connected', 'activeGroupId': 'a-1@g.us'}), two))

class ScriptParsingTests(unittest.TestCase):
    def test_transaction_and_foreign_key_pragmas_are_skipped(self):
        parsed = release.statements('BEGIN TRANSACTION;\nPRAGMA foreign_keys = 0;\nSELECT 1;\nPRAGMA foreign_keys = 1;\nCOMMIT;\n')
        self.assertEqual(parsed, ['SELECT 1;'])

    def test_unexpected_statements_are_rejected(self):
        for text in ('PRAGMA journal_mode = DELETE;\n', "ATTACH 'x.db' AS x;\n", 'VACUUM;\n', 'SELECT 1',
                     '-- comment\nPRAGMA journal_mode = DELETE;\n'):
            with self.subTest(text=text), self.assertRaises(ValueError):
                release.statements(text)

    def test_history_rows_are_required_and_only_inserted(self):
        self.assertEqual(release.planned_migrations(script('SELECT 1;')), [['20261108152657_AddFamilyStatus', '10.0.6']])
        with self.assertRaises(ValueError):
            release.planned_migrations('SELECT 1;\n')
        with self.assertRaises(ValueError):
            release.planned_migrations(script('DELETE FROM "__EFMigrationsHistory";'))

    def test_archive_member_filter(self):
        for name in ('TRobot.Web.dll', 'wwwroot/css/site.css', 'src/index.js'):
            self.assertTrue(release.safe_member(name), name)
        for name in ('/etc/passwd', '../x', 'trobot.db', 'a/trobot.db-wal', 'appsettings.Production.json',
                     'state/auth/creds.json', 'node_modules/x.js', 'gateway.json', 'keys/key.xml', 'tls.pem'):
            self.assertFalse(release.safe_member(name), name)

    def test_release_names(self):
        self.assertEqual(release.check_name('family-20261126-x64'), 'family-20261126-x64')
        for name in ('../x', 'A', 'x/y', 'a..b'):
            with self.subTest(name=name), self.assertRaises(ValueError):
                release.check_name(name)


if __name__ == '__main__':
    unittest.main()
