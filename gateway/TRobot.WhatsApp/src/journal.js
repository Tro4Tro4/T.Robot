import { createHmac } from 'node:crypto';
import { mkdir, open, readFile, rename, unlink } from 'node:fs/promises';
import { join } from 'node:path';

export class GatewayError extends Error {
  constructor(code, status = 400) { super(code); this.code = code; this.status = status; }
}

export class SendJournal {
  constructor(directory, token) { this.directory = directory; this.token = token; }
  digest(value) { return createHmac('sha256', this.token).update(value).digest('hex'); }
  path(operationId) { return join(this.directory, this.digest('operation-v1:' + operationId) + '.json'); }
  fingerprint(groupId, text) { return this.digest('payload-v1:' + JSON.stringify([groupId, text])); }
  async get(operationId, digest) {
    let record;
    try { record = JSON.parse(await readFile(this.path(operationId), 'utf8')); }
    catch (error) { if (error.code === 'ENOENT') return null; throw new GatewayError('journal_unavailable', 503); }
    if (record.payloadDigest !== digest) throw new GatewayError('operation_conflict', 409);
    if (record.version !== 1 || !['pending', 'sent', 'uncertain'].includes(record.status))
      throw new GatewayError('journal_unavailable', 503);
    return { status: record.status === 'sent' ? 'sent' : 'uncertain', messageId: record.messageId ?? null };
  }
  async write(operationId, record, first = false) {
    await mkdir(this.directory, { recursive: true, mode: 0o700 });
    const target = this.path(operationId);
    const temporary = target + '.tmp';
    const path = first ? target : temporary;
    let handle;
    try {
      handle = await open(path, first ? 'wx' : 'w', 0o600);
      await handle.writeFile(JSON.stringify({ version: 1, ...record }), 'utf8');
      await handle.sync();
      await handle.close(); handle = null;
      if (!first) await rename(temporary, target);
    } catch {
      throw new GatewayError('journal_unavailable', 503);
    } finally {
      if (handle) await handle.close().catch(() => {});
      if (!first) await unlink(temporary).catch(() => {});
    }
  }
  pending(operationId, payloadDigest) { return this.write(operationId, { payloadDigest, status: 'pending', messageId: null }, true); }
  complete(operationId, payloadDigest, result) { return this.write(operationId, { payloadDigest, ...result }); }
}
