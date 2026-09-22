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
/** Abort bounded requests; cookies are HttpOnly and CSRF is separate. Never retry mutations automatically. */
export async function requestApi(path, body, csrf, fetcher = globalThis.fetch) { if (!/^\/[a-z/-]+$/.test(path))
    throw new Error('Неправильный путь API'); const controller = new AbortController(); const timer = setTimeout(() => controller.abort(), 15000); try {
    const response = await fetcher('/api' + path, { method: body === undefined ? 'GET' : 'POST', credentials: 'same-origin', cache: 'no-store', signal: controller.signal, headers: body === undefined ? {} : { 'Content-Type': 'application/json', 'X-CSRF-Token': csrf ?? '' }, body: body === undefined ? undefined : JSON.stringify(body) });
    if (!response.headers.get('content-type')?.includes('application/json'))
        throw new Error('Сервер недоступен. Не создавайте повторный ключ: повторите сохранённый запрос.');
    const text = await response.text();
    if (text.length > 16 * 1024 * 1024)
        throw new Error('Слишком большой ответ');
    const result = JSON.parse(text);
    if (!response.ok) {
        const error = new Error(result.message ?? 'Операция отклонена');
        error.code = result.code;
        throw error;
    }
    return result;
}
finally {
    clearTimeout(timer);
} }
/** Bounded pending request memory: a lost response cannot double-extend a license on explicit retry. */
const pendingMutations = new Map();
/** Clear only when leaving the authenticated owner context; no browser persistence is used. */
export function clearPendingMutations() { pendingMutations.clear(); }
/** Reuse the exact mutation UUID for the same parameters after failure; successful completion starts a new operation next time. */
export async function mutationApi(path, body, csrf, sender = requestApi) { const canonical = { ...body }; delete canonical.id; delete canonical.requestId; const key = path + JSON.stringify(canonical); if (!pendingMutations.has(key)) {
    if (pendingMutations.size >= 128)
        throw new Error('Слишком много незавершённых запросов: проверьте журнал сервера.');
    pendingMutations.set(key, { ...body });
} const result = await sender(path, pendingMutations.get(key), csrf); pendingMutations.delete(key); return result; }
