import { createHmac } from 'node:crypto';
import { GatewayError } from './journal.js';

export const validGroup = id => typeof id === 'string' && /^\d{1,30}(?:-\d{1,30})?@g\.us$/.test(id);
const validOperation = id => typeof id === 'string' && /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(id);
export function validateCallback(result) {
  if (!result || typeof result !== 'object' || Array.isArray(result)
    || Object.keys(result).some(key => !['handled', 'duplicate', 'reaction', 'replyText', 'replyOperationId', 'replyPrivately'].includes(key))
    || typeof result.handled !== 'boolean' || typeof result.duplicate !== 'boolean'
    || (result.reaction != null && typeof result.reaction !== 'string')
    || (result.replyPrivately != null && typeof result.replyPrivately !== 'boolean')) throw new Error('callback_invalid');
  const replyText = result.replyText ?? null; const replyOperationId = result.replyOperationId ?? null;
  if ((replyText === null) !== (replyOperationId === null)
    || (replyText !== null && (typeof replyText !== 'string' || !replyText.trim() || replyText.length > 2000
      || !validOperation(replyOperationId) || (result.reaction != null && !result.replyPrivately)))
    || (result.replyPrivately === true && replyText === null)) throw new Error('callback_invalid');
  return { handled: result.handled, duplicate: result.duplicate,
    reaction: result.reaction === '✅' ? '✅' : null, replyText, replyOperationId,
    replyPrivately: result.replyPrivately === true };
}
export const normalizeSender = jid => {
  if (typeof jid !== 'string') return null;
  const match = /^(\d{1,30})(?::\d+)?@(s\.whatsapp\.net|lid)$/.exec(jid);
  return match ? match[1] + '@' + match[2] : null;
};

// Baileys keeps group-key distribution metadata next to the decrypted message.
// Disappearing text is also a normal live message. Do not use its broad normalizer:
// that would unwrap edited messages, view-once media and other unsupported content.
export function extractPlainText(content) {
  const metadata = ['senderKeyDistributionMessage', 'messageContextInfo'];
  for (let depth = 0; depth <= 5; depth++) {
    if (!content || typeof content !== 'object' || Array.isArray(content)) return null;
    const keys = Object.keys(content).filter(key => !metadata.includes(key));
    if (keys.length !== 1) return null;
    if (keys[0] === 'ephemeralMessage') {
      if (depth === 5) return null;
      content = content.ephemeralMessage?.message;
      continue;
    }
    if (keys[0] === 'conversation') return typeof content.conversation === 'string' ? content.conversation : null;
    if (keys[0] === 'extendedTextMessage')
      return typeof content.extendedTextMessage?.text === 'string' ? content.extendedTextMessage.text : null;
    return null;
  }
  return null;
}

export class WhatsAppGateway {
  constructor({ transportFactory, journal, token, callback, now = () => Date.now(), timers = globalThis }) {
    Object.assign(this, { transportFactory, journal, token, callback, now, timers });
    this.status = 'disconnected'; this.errorCode = null; this.qrDataUrl = null;
    this.activeGroupId = null; this.activation = 0; this.socket = null;
    this.epoch = 0; this.requested = false; this.retries = 0; this.reconnect = null;
    this.startTail = Promise.resolve(); this.closing = Promise.resolve();
    this.sendTail = Promise.resolve(); this.callbackTail = Promise.resolve(); this.queued = 0;
    this.seen = new Map(); this.rates = new Map();
    // Fixed, aggregate counters only. Never retain IDs, JIDs, text, QR or exception payloads.
    this.diagnostics = { notifyBatches: 0, received: 0, contextRejected: 0, envelopeRejected: 0,
      commandRejected: 0, senderRejected: 0, duplicateOrBusy: 0, rateRejected: 0, queued: 0,
      callbackAttempts: 0, callbackHandled: 0, callbackIgnored: 0, callbackDuplicate: 0,
      callbackFailed: 0, reactionSent: 0, reactionFailed: 0 };
  }
  health() { return { status: this.status, qrDataUrl: this.qrDataUrl, activeGroupId: this.activeGroupId,
    errorCode: this.errorCode, diagnostics: { ...this.diagnostics } }; }
  count(name) { this.diagnostics[name] = Math.min(Number.MAX_SAFE_INTEGER, this.diagnostics[name] + 1); }
  async connect() {
    if (this.requested && ['connecting', 'qr', 'connected'].includes(this.status)) return this.health();
    this.requested = true; this.retries = 0;
    if (this.reconnect) this.timers.clearTimeout(this.reconnect);
    this.reconnect = null;
    await this.startSocket();
    return this.health();
  }
  startSocket() {
    const task = this.startTail.then(() => this.openSocket());
    this.startTail = task.catch(() => {});
    return task;
  }
  closeTransport(socket) {
    const close = Promise.resolve().then(() => socket?.close()).catch(() => {});
    this.closing = Promise.all([this.closing, close]).then(() => {});
  }
  async openSocket() {
    if (!this.requested) return;
    const epoch = ++this.epoch;
    this.closeTransport(this.socket); this.socket = null;
    this.status = 'connecting'; this.errorCode = null; this.qrDataUrl = null;
    await this.closing;
    if (!this.requested || epoch !== this.epoch) return;
    this.socketStarted = this.now();
    try {
      const socket = await this.transportFactory({
        update: update => this.connectionUpdate(epoch, update),
        messages: batch => this.incoming(epoch, batch)
      });
      if (!this.requested || epoch !== this.epoch) { await socket.close(); return; }
      this.socket = socket;
    } catch {
      if (epoch === this.epoch && this.requested) this.connectionUpdate(epoch, { connection: 'close' });
    }
  }
  connectionUpdate(epoch, update) {
    if (epoch !== this.epoch || !this.requested) return;
    if (update.qrDataUrl) { this.status = 'qr'; this.qrDataUrl = update.qrDataUrl; }
    if (update.connection === 'open') {
      this.status = 'connected'; this.qrDataUrl = null; this.errorCode = null; this.retries = 0;
    }
    if (update.connection === 'close') {
      // Invalidate every pending callback/reaction from the closed socket immediately.
      this.epoch++;
      this.closeTransport(this.socket); this.socket = null; this.qrDataUrl = null; this.status = 'disconnected';
      if (update.loggedOut || update.fatalCode === 'auth_state_unavailable') {
        this.requested = false; this.status = 'error';
        this.errorCode = update.loggedOut ? 'session_logged_out' : 'auth_state_unavailable'; return;
      }
      if (++this.retries > 5) {
        this.requested = false; this.status = 'error'; this.errorCode = 'reconnect_exhausted'; return;
      }
      this.reconnect = this.timers.setTimeout(() => {
        this.reconnect = null;
        if (this.requested) void this.startSocket();
      }, Math.min(1000 * 2 ** (this.retries - 1), 16000));
      this.reconnect.unref?.();
    }
  }
  disconnect() {
    this.requested = false; this.epoch++;
    if (this.reconnect) this.timers.clearTimeout(this.reconnect);
    this.reconnect = null;
    this.closeTransport(this.socket); this.socket = null;
    this.status = 'disconnected'; this.errorCode = null; this.qrDataUrl = null;
    this.activeGroupId = null; this.activation = this.now();
    return this.health();
  }
  connected() {
    if (!this.socket || this.status !== 'connected') throw new GatewayError('not_connected', 409);
    return this.socket;
  }
  async groups() { return { groups: await this.connected().groups() }; }
  async members(groupId) {
    if (!validGroup(groupId)) throw new GatewayError('invalid_group');
    if (groupId !== this.activeGroupId) throw new GatewayError('inactive_group', 403);
    const socket = this.connected();
    const epoch = this.epoch; const pinEpoch = this.pinEpoch;
    if (!(await socket.groups()).some(g => g.id === groupId)) throw new GatewayError('group_not_available', 403);
    const members = await socket.members(groupId);
    if (socket !== this.socket || epoch !== this.epoch || pinEpoch !== this.pinEpoch || groupId !== this.activeGroupId)
      throw new GatewayError('connection_changed', 409);
    return { members };
  }
  async group(groupId) {
    if (groupId !== null) {
      if (!validGroup(groupId)) throw new GatewayError('invalid_group');
      const socket = this.connected(); const epoch = this.epoch;
      if (!(await socket.groups()).some(g => g.id === groupId)) throw new GatewayError('group_not_available', 403);
      if (socket !== this.socket || epoch !== this.epoch) throw new GatewayError('connection_changed', 409);
    }
    this.activeGroupId = groupId; this.activation = this.now();
    // Pin changes also invalidate already queued callbacks, even if the same group is reselected.
    this.pinEpoch = (this.pinEpoch ?? 0) + 1;
    return this.health();
  }
  send(request) {
    const task = this.sendTail.then(() => this.sendOnce(request));
    this.sendTail = task.catch(() => {});
    return task;
  }
  async sendOnce({ operationId, groupId, text }, original = null, maximum = 1000, privateRecipient = null) {
    if (!validOperation(operationId) || !validGroup(groupId) || typeof text !== 'string'
      || !text.trim() || text.length > maximum) throw new GatewayError('invalid_send');
    const { socket, epoch, pinEpoch } = original ?? { socket: this.socket, epoch: this.epoch, pinEpoch: this.pinEpoch };
    operationId = operationId.toLowerCase();
    if (privateRecipient !== null && (!original || normalizeSender(privateRecipient) !== privateRecipient))
      throw new GatewayError('invalid_send');
    const digest = this.journal.fingerprint(privateRecipient === null ? groupId : groupId + ':private:' + privateRecipient, text);
    const prior = await this.journal.get(operationId, digest);
    if (prior) return prior;
    this.connected();
    if (groupId !== this.activeGroupId) throw new GatewayError('inactive_group', 403);
    await this.journal.pending(operationId, digest);
    let result = { status: 'uncertain', messageId: null };
    try {
      if (socket === this.socket && epoch === this.epoch && pinEpoch === this.pinEpoch
        && groupId === this.activeGroupId && this.status === 'connected') {
        const messageId = privateRecipient === null ? await socket.send(groupId, text) : await socket.sendPrivate(privateRecipient, text);
        result = { status: 'sent', messageId: typeof messageId === 'string' ? messageId : null };
      }
    } catch { /* An external send failure can be ambiguous. Never retry it automatically. */ }
    try { await this.journal.complete(operationId, digest, result); }
    catch { return { status: 'uncertain', messageId: null }; }
    return result;
  }
  digest(value) { return createHmac('sha256', this.token).update(value).digest('hex'); }
  trimCaches(now) {
    for (const [key, expiry] of this.seen) if (expiry <= now) this.seen.delete(key);
    for (const [key, entry] of this.rates) if (entry.until <= now) this.rates.delete(key);
    while (this.seen.size >= 2000) this.seen.delete(this.seen.keys().next().value);
    while (this.rates.size >= 1000) this.rates.delete(this.rates.keys().next().value);
  }
  incoming(epoch, batch) {
    if (epoch !== this.epoch || this.status !== 'connected' || batch?.type !== 'notify' || !this.activeGroupId) return;
    this.count('notifyBatches');
    for (const message of batch.messages ?? []) {
      this.count('received');
      const key = message.key;
      const remoteJid = key?.remoteJid;
      const isPrivate = normalizeSender(remoteJid) !== null;
      const groupId = isPrivate ? this.activeGroupId : remoteJid;
      const timestamp = Number(message.messageTimestamp);
      const now = this.now();
      if (key?.fromMe || !validGroup(groupId) || groupId !== this.activeGroupId || (!isPrivate && !key?.participant)
        || typeof key.id !== 'string' || !key.id || key.id.length > 200 || !Number.isSafeInteger(timestamp) || timestamp <= 0
        || timestamp * 1000 < Math.max(this.activation, this.socketStarted) - 999
        || timestamp * 1000 < now - 120000 || timestamp * 1000 > now + 30000) { this.count('contextRejected'); continue; }
      const text = extractPlainText(message.message);
      if (text === null) { this.count('envelopeRejected'); continue; }
      // Only Application knows whether this sender has an authorized attendance dialog.
      // Trim solely for candidate recognition; preserve the exact text in the callback.
      if (isPrivate ? (!text.trim() || text.length > 300) : (text.length > 100 || (!/^!(?:presenza|aiuto|menu|todo|riepilogo|porto|prendo|fatto|conferma|annulla)(?:\s|$)/i.test(text)
        && !/^!(?:proponi|preferenze)$/i.test(text.trim())
        && !/^(?:\d{1,2}[/\.\-]\d{1,2}(?:[/\.\-]\d{4})?|\d{1,3}|s[iì]|no)$/i.test(text.trim())))) { this.count('commandRejected'); continue; }
      const senderIds = [...new Set((isPrivate ? [remoteJid, key.remoteJidAlt] : [key.participant, key.participantAlt]).map(normalizeSender).filter(Boolean))];
      if (!senderIds.length) { this.count('senderRejected'); continue; }
      this.trimCaches(now);
      const seenId = this.digest('incoming:' + remoteJid + ':' + key.id);
      if (this.seen.has(seenId) || this.queued >= 50) { this.count('duplicateOrBusy'); continue; }
      // Keep the group budget unchanged while allowing a private dialog correction.
      const sender = this.digest((isPrivate ? 'private-sender:' : 'sender:') + senderIds[0]);
      const rate = this.rates.get(sender) ?? { count: 0, until: now + 60000 };
      if (rate.count >= (isPrivate ? 15 : 6)) { this.count('rateRejected'); continue; }
      rate.count++; this.rates.set(sender, rate); this.seen.set(seenId, now + 600000);
      this.queued++;
      this.count('queued');
      const pinEpoch = this.pinEpoch; const socket = this.socket;
      const payload = { groupId, senderIds, messageId: key.id, text, timestampUnixSeconds: timestamp, isPrivate };
      this.callbackTail = this.callbackTail.then(async () => {
        if (epoch !== this.epoch || pinEpoch !== this.pinEpoch || groupId !== this.activeGroupId || socket !== this.socket) return;
        this.count('callbackAttempts');
        let result;
        try { result = validateCallback(await this.callback(payload)); }
        catch { this.count('callbackFailed'); return; }
        // Private text can only go back to the verified author of this message.
        // Application owns persisted dialog authorization and its five-minute expiry.
        if ((isPrivate && !result.replyPrivately && result.replyText !== null)
          || (result.replyPrivately && !isPrivate && !/^!(?:proponi|preferenze)$/i.test(text.trim()))) {
          this.count('callbackFailed'); return;
        }
        this.count(result?.handled ? 'callbackHandled' : 'callbackIgnored');
        if (result?.duplicate) this.count('callbackDuplicate');
        if (result.handled && !result.duplicate && result.replyText !== null) {
          // Preserve the originating socket and pin through both the send queue and
          // every journal await. A new socket or same-group repin cannot adopt it.
          const task = this.sendTail.then(() => this.sendOnce({ operationId: result.replyOperationId,
            groupId, text: result.replyText }, { socket, epoch, pinEpoch }, 2000,
          result.replyPrivately ? senderIds[0] : null));
          this.sendTail = task.catch(() => {});
          await task.catch(() => {});
        }
        if (!isPrivate && result?.handled && !result.duplicate && result.reaction
          && epoch === this.epoch && pinEpoch === this.pinEpoch && groupId === this.activeGroupId
          && socket === this.socket && this.status === 'connected') {
          try { await socket.react(groupId, key, '✅'); this.count('reactionSent'); }
          catch { this.count('reactionFailed'); }
        }
      }).finally(() => { this.queued--; });
    }
  }
  idle() { return this.callbackTail; }
}
