/** Generate a 256-bit activation key in the owner browser; keys are never kept in localStorage. */
export function makeKey(cryptoProvider = globalThis.crypto) { const bytes = cryptoProvider.getRandomValues(new Uint8Array(32)); return 'LSP-' + btoa(String.fromCharCode(...bytes)).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, ''); }
/** Validate bounded positive integers before sending a mutation; the server independently revalidates them. */
export function integer(value, min, max) { const n = Number(value); if (!Number.isInteger(n) || n < min || n > max)
    throw new Error(`Введите целое число от ${min} до ${max}`); return n; }
/** Create one immutable issue request. Keep its ID and key for an explicit retry after an ambiguous network failure. */
export function issueRequest(values, cryptoProvider = globalThis.crypto) { const unit = values.unit; if (!['hours', 'days', 'years', 'permanent'].includes(unit))
    throw new Error('Неизвестный срок'); if (!['activation', 'issue'].includes(values.starts))
    throw new Error('Неизвестное начало срока'); const label = String(values.label ?? '').trim(); if (label.length > 120 || /[\x00-\x1f]/.test(label))
    throw new Error('Метка: до 120 символов без управляющих знаков'); return { id: cryptoProvider.randomUUID(), accessKey: makeKey(cryptoProvider), label, unit, amount: unit === 'permanent' ? 0 : integer(values.amount, 1, unit === 'years' ? 100 : 876000), starts: values.starts, maxDevices: integer(values.devices, 1, 100) }; }
/** A reservation is retired by global epoch changes even if it has no individual revocation timestamp. */
export function reserveStatus(grant, policy) { return grant.revokedAt !== null ? 'Отозван' : grant.epoch !== policy.epoch ? 'Старое поколение' : !policy.enabled ? 'Отключён' : 'Действует'; }
/** Bounded client-side page after local filtering of the size-limited server list. */
export function pageItems(items, page, size = 25) { return items.slice(Math.max(0, page) * size, (Math.max(0, page) + 1) * size); }
/** Display UTC-derived server dates in local timezone with an explicit tooltip in the UI. */
export function dateText(seconds) { return seconds == null ? '—' : new Date(seconds * 1000).toLocaleString('ru-RU'); }
/** Browser-safe download without markup injection; filename is code-controlled, never supplied by an API response. */
export function downloadText(name, text) { const url = URL.createObjectURL(new Blob([text], { type: 'text/plain;charset=utf-8' })); const a = document.createElement('a'); a.href = url; a.download = name; a.click(); setTimeout(() => URL.revokeObjectURL(url), 1000); }
/** Read at most the configured UTF-8 byte budget, cancelling an oversized response before buffering its remainder. */
export async function readJsonBounded(response, maximumBytes = 16 * 1024 * 1024) {
    if (!Number.isSafeInteger(maximumBytes) || maximumBytes < 1) throw new Error('Неправильный лимит ответа');
    const declared = response.headers.get('content-length');
    if (declared !== null && (!/^\d+$/.test(declared) || Number(declared) > maximumBytes)) {
        await response.body?.cancel().catch(() => {});
        throw new Error('Слишком большой или некорректный ответ');
    }
    if (!response.body) throw new Error('Пустой ответ');
    const reader = response.body.getReader();
    const decoder = new TextDecoder('utf-8', { fatal: true });
    let count = 0;
    const parts = [];
    // Coalesce tiny HTTP chunks: at most ceil(maximumBytes / 64 KiB) text fragments are retained.
    const block = new Uint8Array(Math.min(65536, maximumBytes));
    let used = 0;
    try {
        while (true) {
            const { done, value } = await reader.read();
            if (done) break;
            count += value.byteLength;
            if (count > maximumBytes) throw new Error('Слишком большой ответ');
            for (let at = 0; at < value.byteLength;) {
                const take = Math.min(block.length - used, value.byteLength - at);
                block.set(value.subarray(at, at + take), used); used += take; at += take;
                if (used === block.length) { parts.push(decoder.decode(block, { stream: true })); used = 0; }
            }
        }
        parts.push(decoder.decode(block.subarray(0, used), { stream: true }));
        parts.push(decoder.decode());
        return JSON.parse(parts.join(''));
    } catch (error) {
        await reader.cancel().catch(() => {});
        throw error;
    } finally { reader.releaseLock(); }
}
/** Abort bounded requests; cookies are HttpOnly and CSRF is separate. Never retry mutations automatically. */
export async function requestApi(path, body, csrf, fetcher = globalThis.fetch) {
    if (!/^\/[a-z/-]+$/.test(path)) throw new Error('Неправильный путь API');
    const controller = new AbortController();
    const timer = setTimeout(() => controller.abort(), 15000);
    try {
        const response = await fetcher('/api' + path, { method: body === undefined ? 'GET' : 'POST',
            credentials: 'same-origin', cache: 'no-store', signal: controller.signal,
            headers: body === undefined ? {} : { 'Content-Type': 'application/json', 'X-CSRF-Token': csrf ?? '' },
            body: body === undefined ? undefined : JSON.stringify(body) });
        if (response.headers.get('content-type')?.split(';')[0].trim().toLowerCase() !== 'application/json') {
            await response.body?.cancel().catch(() => {});
            throw new Error('Сервер недоступен. Не создавайте повторный ключ: повторите сохранённый запрос.');
        }
        const result = await readJsonBounded(response);
        if (!response.ok) {
            const error = new Error(typeof result?.message === 'string' ? result.message : 'Операция отклонена');
            error.code = result?.code;
            throw error;
        }
        return result;
    } finally { clearTimeout(timer); }
}
/** Bounded pending request memory: a lost response cannot double-extend a license on explicit retry. */
const pendingMutations = new Map();
/** Clear only when leaving the authenticated owner context; no browser persistence is used. */
export function clearPendingMutations() { pendingMutations.clear(); }
/** Serialize data in stable property order so field insertion order cannot create a second administrative operation. */
export function canonicalMutation(value) {
    if (Array.isArray(value)) return value.map(canonicalMutation);
    if (value !== null && typeof value === 'object') return Object.fromEntries(Object.keys(value).sort().map(k => [k, canonicalMutation(value[k])]));
    return value;
}
/** Reuse one immutable UUID/body after failure and coalesce simultaneous clicks. A stale session response cannot clear a new session's retry. */
export async function mutationApi(path, body, csrf, sender = requestApi) {
    const canonical = { ...body };
    delete canonical.id;
    delete canonical.requestId;
    const key = path + JSON.stringify(canonicalMutation(canonical));
    let entry = pendingMutations.get(key);
    if (!entry) {
        if (pendingMutations.size >= 128) throw new Error('Слишком много незавершённых запросов: проверьте журнал сервера.');
        entry = { body: structuredClone(body), inflight: null };
        pendingMutations.set(key, entry);
    }
    if (entry.inflight) return entry.inflight;
    const call = Promise.resolve().then(() => sender(path, structuredClone(entry.body), csrf));
    entry.inflight = call;
    try {
        const result = await call;
        if (pendingMutations.get(key) === entry) pendingMutations.delete(key);
        return result;
    } finally {
        if (entry.inflight === call) entry.inflight = null;
    }
}
