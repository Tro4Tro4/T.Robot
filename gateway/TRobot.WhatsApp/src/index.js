import { readFile } from 'node:fs/promises';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { SendJournal } from './journal.js';
import { WhatsAppGateway } from './gateway.js';
import { createCallback, createGatewayServer } from './http.js';
import { parseConfiguration } from './config.js';

const root = resolve(dirname(fileURLToPath(import.meta.url)), '../../..');
try {
  const config = parseConfiguration(await readFile(join(root, '.local/whatsapp/gateway.json'), 'utf8'), root);
  const gateway = new WhatsAppGateway({ token: config.Token,
    journal: new SendJournal(join(config.StateDirectory, 'send-journal'), config.Token),
    callback: createCallback(config.CallbackUrl, config.Token),
    // Importing the HTTP host never opens a WhatsApp socket; only the explicit /connect calls this.
    transportFactory: async handlers => {
      const { createBaileysTransport } = await import('./transport.js');
      return createBaileysTransport(config.StateDirectory, handlers);
    }
  });
  const server = createGatewayServer(gateway, config.Token);
  server.on('error', () => { process.stderr.write('T.Robot gateway: local_listener_unavailable\n'); process.exitCode = 1; });
  server.listen(5240, '127.0.0.1', () => process.stdout.write('T.Robot gateway HTTP locale pronto; WhatsApp non collegato.\n'));
  const shutdown = () => { gateway.disconnect(); server.close(); };
  process.once('SIGINT', shutdown); process.once('SIGTERM', shutdown);
  const fatal = () => {
    process.stderr.write('T.Robot gateway: dependency_failure\n');
    shutdown(); process.exitCode = 1;
    setTimeout(() => process.exit(1), 5000).unref();
  };
  // Dependencies must never print unhandled exception payloads containing cryptographic state.
  process.once('uncaughtException', fatal); process.once('unhandledRejection', fatal);
} catch {
  process.stderr.write('T.Robot gateway: configuration_unavailable\n'); process.exitCode = 1;
}
