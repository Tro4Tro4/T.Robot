import { join } from 'node:path';
import { normalizeSender, validGroup } from './gateway.js';

async function dependencies() {
  // libsignal 6 logs entire session objects through direct console calls. This isolated
  // process deliberately suppresses dependency console output before importing it.
  // Our HTTP host diagnostics use only fixed process.stdout/stderr.write messages.
  for (const method of ['log', 'info', 'warn', 'error', 'debug', 'trace', 'table', 'dir']) console[method] = () => {};
  const [baileys, qrcode, pino, defaultLogger] = await Promise.all([
    import('@whiskeysockets/baileys'), import('qrcode'), import('pino'),
    import('@whiskeysockets/baileys/lib/Utils/logger.js')
  ]);
  defaultLogger.default.level = 'silent';
  return { ...baileys, qrcode: qrcode.default, logger: pino.default({ level: 'silent' }) };
}

export async function createBaileysTransport(stateDirectory, handlers, injected) {
  const lib = injected ?? await dependencies();
  const { state, saveCreds } = await lib.useMultiFileAuthState(join(stateDirectory, 'auth'));
  const socket = lib.makeWASocket({
    auth: { creds: state.creds, keys: lib.makeCacheableSignalKeyStore(state.keys, lib.logger) },
    logger: lib.logger,
    browser: lib.Browsers.windows('T.Robot'),
    printQRInTerminal: false,
    syncFullHistory: false,
    shouldSyncHistoryMessage: () => false,
    markOnlineOnConnect: false,
    emitOwnEvents: false,
    enableRecentMessageCache: false,
    enableAutoSessionRecreation: false,
    shouldIgnoreJid: jid => typeof jid === 'string' && !validGroup(jid),
    getMessage: async () => undefined,
    connectTimeoutMs: 20000,
    defaultQueryTimeoutMs: 15000
  });
  let closed = false; let sequence = 0; let saves = Promise.resolve();
  const canonical = value => {
    if (typeof value !== 'string') return null;
    return normalizeSender(lib.jidNormalizedUser(value));
  };
  const onCredentials = () => {
    if (closed) return;
    saves = saves.then(saveCreds).catch(() => {
      if (!closed) handlers.update({ connection: 'close', fatalCode: 'auth_state_unavailable' });
    });
  };
  const onMessages = batch => {
    if (!closed && batch?.type === 'notify') handlers.messages(batch);
  };
  const onUpdate = update => {
    if (closed) return;
    const current = ++sequence;
    if (update.qr) {
      void lib.qrcode.toDataURL(update.qr, { type: 'image/png', errorCorrectionLevel: 'M', margin: 2, width: 320 })
        .then(qrDataUrl => { if (!closed && current === sequence) handlers.update({ qrDataUrl }); })
        .catch(() => { if (!closed && current === sequence) handlers.update({ connection: 'close' }); });
    }
    if (update.connection) {
      handlers.update({ connection: update.connection,
        loggedOut: update.lastDisconnect?.error?.output?.statusCode === lib.DisconnectReason.loggedOut });
    }
  };
  socket.ev.on('connection.update', onUpdate);
  socket.ev.on('creds.update', onCredentials);
  socket.ev.on('messages.upsert', onMessages);
  return {
    async groups() {
      if (closed) throw new Error('transport_closed');
      const metadata = await socket.groupFetchAllParticipating();
      return Object.values(metadata).filter(item => validGroup(item.id))
        .map(item => ({ id: item.id, name: String(item.subject ?? 'Gruppo').slice(0, 200) }));
    },
    async members(groupId) {
      if (closed || !validGroup(groupId)) throw new Error('transport_closed');
      const metadata = await socket.groupMetadata(groupId);
      return metadata.participants.map(participant => {
        const aliases = [...new Set([participant.id, participant.lid, participant.phoneNumber].map(canonical).filter(Boolean))];
        if (!aliases.length) return null;
        // rc14 group metadata normally has no contact name. Provide a distinguishable
        // identity only in this authenticated response; never log or persist the label.
        const phone = aliases.find(id => id.endsWith('@s.whatsapp.net'))?.split('@')[0];
        return { id: aliases[0], alternateIds: aliases.slice(1),
          label: String(participant.name || participant.notify || participant.username
            || (phone ? 'Numero WhatsApp ' + phone : 'Identità WhatsApp ' + aliases[0])).slice(0, 150) };
      }).filter(Boolean);
    },
    async send(groupId, text) {
      if (closed || !validGroup(groupId)) throw new Error('transport_closed');
      const result = await socket.sendMessage(groupId, { text, linkPreview: null });
      if (typeof result?.key?.id !== 'string' || !result.key.id) throw new Error('send_uncertain');
      return result.key.id;
    },
    async react(groupId, key, text) {
      if (closed || !validGroup(groupId) || key.remoteJid !== groupId || text !== '✅') return;
      await socket.sendMessage(groupId, { react: { text: '✅', key } });
    },
    close() {
      if (closed) return saves;
      closed = true; sequence++;
      socket.ev.off('connection.update', onUpdate);
      socket.ev.off('creds.update', onCredentials);
      socket.ev.off('messages.upsert', onMessages);
      // Preserve auth state. Calling logout would invalidate the user's linked device.
      return Promise.all([saves, socket.end(undefined)]).then(() => {}).catch(() => {});
    }
  };
}
