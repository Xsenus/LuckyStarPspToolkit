import { React, createRoot } from './vendor.js';
import { makeKey, integer, issueRequest, reserveStatus, pageItems, dateText, downloadText, requestApi, mutationApi, clearPendingMutations } from './model.mjs';
const h = React.createElement;
const { useState, useEffect, useRef } = React;
const labels = { active: 'Активна', pending: 'Ждёт активации', expired: 'Истекла', suspended: 'Приостановлена', revoked: 'Отозвана' };
/** Labelled native control with accessible name; all strings are React text nodes, never HTML. */
function Field({ label, ...props }) { return h('label', { className: 'field' }, h('span', null, label), h('input', { 'aria-label': label, ...props })); }
/** Select options use explicit known values; no dynamic code or markup is evaluated. */
function Select({ label, value, onChange, options }) { return h('label', { className: 'field' }, h('span', null, label), h('select', { value, onChange, 'aria-label': label }, ...options.map(([v, t]) => h('option', { key: v, value: v }, t)))); }
/** Shared button prevents accidental form submission unless type is explicitly provided. */
function Button({ children, ...props }) { return h('button', { type: 'button', ...props }, children); }
/** Login and step-up form clear password/code after every attempt, including failure. */
function Credentials({ session, onSuccess, reauth = false, onCancel }) {
    const [error, setError] = useState('');
    const [busy, setBusy] = useState(false);
    /** Validate the form through server-side MFA and clear credential inputs on completion. */
    async function submit(event) { event.preventDefault(); if (busy)
        return; setBusy(true); setError(''); const form = event.currentTarget; const data = new FormData(form); const body = { username: reauth ? session.username : String(data.get('username')), password: String(data.get('password')), code: String(data.get('code')).trim() }; try {
        const result = await requestApi(reauth ? '/reauth' : '/login', body, session?.csrfToken);
        onSuccess(result);
    }
    catch (e) {
        setError(e.code === 'WEB_AUTH_FAILED' ? 'Неверный пароль или одноразовый код. Повторно использованный код тоже отклоняется.' : e.message);
    }
    finally {
        form.elements.password.value = '';
        form.elements.code.value = '';
        setBusy(false);
    } }
    return h('form', { onSubmit: submit, className: reauth ? 'card' : 'card login' }, h('div', { className: 'eyebrow' }, 'LSP / OWNER CONSOLE'), h('h1', null, reauth ? 'Подтвердите действие' : 'Управление лицензиями'), h('p', { className: 'muted' }, reauth ? 'Введите пароль и новый одноразовый код.' : 'Пароль и второй фактор. Доступ только владельцу.'), !reauth && h(Field, { label: 'Логин', name: 'username', autoComplete: 'username', required: true, maxLength: 64, defaultValue: 'owner' }), h(Field, { label: 'Пароль', name: 'password', type: 'password', autoComplete: 'current-password', required: true, maxLength: 256 }), h(Field, { label: 'Код TOTP или код восстановления', name: 'code', autoComplete: 'one-time-code', required: true, maxLength: 64 }), error && h('p', { role: 'alert', className: 'error' }, error), h(Button, { type: 'submit', disabled: busy, className: 'primary' }, busy ? 'Проверка…' : reauth ? 'Подтвердить' : 'Войти'), reauth && h(Button, { onClick: onCancel }, 'Отмена'), h('p', { className: 'muted small' }, 'Коды восстановления одноразовые и не заменяют пароль.'));
}
/** Issue flow generates the key once and keeps ambiguous retries bound to the same request in memory. */
function IssuePanel({ session, run, refresh }) {
    const [values, setValues] = useState({ label: '', unit: 'days', amount: '7', starts: 'activation', devices: '1' });
    const [pending, setPending] = useState(null);
    const [issued, setIssued] = useState(false);
    const set = (name) => event => setValues({ ...values, [name]: event.target.value });
    /** Retain one generated credential and request UUID until the backend has confirmed issuance. */
    async function issue(event) { event.preventDefault(); await run(async () => { const data = pending ?? issueRequest(values); if (!pending) {
        setPending(data);
        downloadText('lsp-owner-pending-' + data.id + '.json', JSON.stringify(data, null, 2));
    } await requestApi('/issue', data, session.csrfToken); setIssued(true); await refresh(); }); }
    return h('section', { className: 'card' }, h('h2', null, 'Выдать ключ'), h('p', { className: 'muted small' }, 'Сначала сохраняется закрытая квитанция владельца. Если ответ потерян, повтор использует тот же ключ и ID.'), h('form', { onSubmit: issue }, h('fieldset', { disabled: !!pending }, h(Field, { label: 'Клиент / примечание', value: values.label, onChange: set('label'), maxLength: 120 }), h('div', { className: 'grid2' }, h(Select, { label: 'Срок', value: values.unit, onChange: set('unit'), options: [['hours', 'Часы'], ['days', 'Дни'], ['years', 'Годы'], ['permanent', 'Бессрочно']] }), h(Field, { label: 'Количество', type: 'number', min: 1, max: 876000, value: values.unit === 'permanent' ? '0' : values.amount, onChange: set('amount'), disabled: values.unit === 'permanent' })), h('div', { className: 'grid2' }, h(Select, { label: 'Начало отсчёта', value: values.starts, onChange: set('starts'), options: [['activation', 'Первая активация'], ['issue', 'Выдача']] }), h(Field, { label: 'Установки', type: 'number', min: 1, max: 100, value: values.devices, onChange: set('devices') }))), !issued && h(Button, { type: 'submit', className: 'primary' }, pending ? 'Повторить тот же запрос' : 'Создать лицензию')), pending && h('div', { className: 'key-box' }, h('strong', null, issued ? 'Ключ создан' : 'Запрос сохранён — результат пока не подтверждён'), h('p', { className: 'small' }, 'ID: ' + pending.id), issued && h('code', { 'data-testid': 'issued-key' }, pending.accessKey), issued && h(Button, { onClick: () => downloadText('access-key.txt', pending.accessKey + '\n') }, 'Скачать ключ клиенту'), h(Button, { onClick: () => downloadText('lsp-owner-pending-' + pending.id + '.json', JSON.stringify(pending, null, 2)) }, 'Сохранить квитанцию владельца'), issued && h(Button, { onClick: () => { setPending(null); setIssued(false); } }, 'Следующая лицензия')), h('p', { className: 'muted small' }, 'Полный ключ хранится только в текущей вкладке и скачанном файле. В базе его нет. Не отправляйте квитанцию клиенту.'));
}
/** Exact request retry after lost reply; accepts only the strict issue schema, not executable content. */
function RetryIssue({ session, run, refresh }) { /** Read a bounded saved issuance receipt and explicitly repeat the exact operation. */
    async function load(e) { const file = e.target.files?.[0]; e.target.value = ''; if (!file)
    return; await run(async () => { if (file.size > 32768)
    throw new Error('Файл слишком большой'); const value = JSON.parse(await file.text()); if (!window.confirm('Повторить выдачу из закрытой квитанции?'))
    return; await requestApi('/issue', value, session.csrfToken); await refresh(); downloadText('access-key.txt', String(value.accessKey) + '\n'); }); } return h('label', { className: 'file-button' }, 'Повторить выдачу из квитанции', h('input', { type: 'file', accept: '.json', onChange: load })); }
/** Detail panel separates reversible actions, irreversible revocation and installation reset with explicit confirmation. */
function LicenseDetail({ item, session, run, refresh }) {
    const [amount, setAmount] = useState('30');
    const [unit, setUnit] = useState('days');
    /** Request confirmation and send one idempotent lifecycle mutation. */
    async function change(action, deviceId = '') { if (!window.confirm(`Подтвердить: ${action}. Лицензия ${item.label || item.id}${action === 'revoke' ? ' — отзыв необратим' : ''}?`))
        return; const body = { requestId: crypto.randomUUID(), licenseId: item.id, action, unit: action === 'extend' ? unit : '', amount: action === 'extend' ? integer(amount, 1, 876000) : 0, deviceId }; await run(async () => { await mutationApi('/change', body, session.csrfToken); await refresh(); }); }
    return h('section', { className: 'card detail' }, h('h2', null, item.label || 'Лицензия'), h('code', null, item.id), h('p', null, 'Статус: ' + labels[item.status]), h('p', null, 'Действует до: ' + (item.permanent ? 'Бессрочно' : dateText(item.expiresAt))), h('div', { className: 'actions' }, h(Button, { onClick: () => change('suspend'), disabled: item.status === 'revoked' }, 'Приостановить'), h(Button, { onClick: () => change('resume'), disabled: item.status !== 'suspended' }, 'Возобновить'), h(Button, { onClick: () => change('permanent'), disabled: item.status === 'revoked' || item.permanent }, 'Сделать бессрочной'), h(Button, { onClick: () => change('revoke'), disabled: item.status === 'revoked', className: 'danger' }, 'Отозвать навсегда')), h('div', { className: 'grid2' }, h(Field, { label: 'Продлить на', type: 'number', min: 1, value: amount, onChange: e => setAmount(e.target.value) }), h(Select, { label: 'Единица', value: unit, onChange: e => setUnit(e.target.value), options: [['hours', 'Часы'], ['days', 'Дни'], ['years', 'Годы']] })), h(Button, { onClick: () => change('extend'), disabled: item.permanent || item.status === 'revoked' }, 'Продлить'), h('h3', null, `Установки ${item.devices.length} / ${item.maxDevices}`), item.devices.length === 0 ? h('p', { className: 'muted' }, 'Для резервного доступа сначала активируйте программу онлайн.') : item.devices.map(d => h('div', { className: 'device', key: d.id }, h('code', null, d.id), h('span', null, dateText(d.activatedAt)), h(Button, { onClick: () => change('reset-device', d.id), className: 'danger' }, 'Освободить установку'))));
}
/** Reserve control never promises instantaneous revocation of a disconnected computer. */
function ReservePanel({ policy, licenses, session, run, refresh }) {
    const [licenseId, setLicense] = useState('');
    const [deviceId, setDevice] = useState('');
    const [hours, setHours] = useState('168');
    const [reason, setReason] = useState('Резерв при недоступности сервера');
    const [grant, setGrant] = useState(null);
    const selected = licenses.find(x => x.id === licenseId);
    const devices = selected?.devices ?? [];
    /** Rotate reserve policy only after owner confirmation and fresh-MFA authorization. */
    async function toggle() { if (!window.confirm(policy.enabled ? 'Отключить резерв? Уже выданный офлайн-допуск нельзя отозвать без связи; он истечёт не позднее своего срока. После включения старые допуски не оживут.' : 'Включить выдачу резервных допусков?'))
        return; await run(async () => { await mutationApi('/reserve/policy', { requestId: crypto.randomUUID(), enabled: !policy.enabled }, session.csrfToken); await refresh(); }); }
    /** Retain one generated credential and request UUID until the backend has confirmed issuance. */
    async function issue(event) { event.preventDefault(); await run(async () => { if (!licenseId || !deviceId)
        throw new Error('Выберите активированную установку'); const value = await mutationApi('/reserve/issue', { id: crypto.randomUUID(), licenseId, deviceId, hours: integer(hours, 1, 168), reason }, session.csrfToken); setGrant(value); await refresh(); }); }
    /** Irreversibly retire the selected grant after explicit confirmation. */
    async function revoke(value) { if (!window.confirm('Отозвать резервный допуск? При следующей успешной связи он заблокируется; без связи действует только до подписанного срока.'))
        return; await run(async () => { await mutationApi('/reserve/revoke', { requestId: crypto.randomUUID(), grantId: value.id }, session.csrfToken); await refresh(); }); }
    return h('div', null, h('section', { className: 'card warning' }, h('h2', null, 'Резервный доступ — до 7 дней'), h('p', null, 'Все функции программы доступны на разрешённой установке. Это не мастер-ключ: требуется действующая лицензия и предварительная онлайн-активация.'), h('p', null, 'Без связи немедленный отзыв невозможен. Через 168 часов без обновления (или раньше, если срок короче) программа блокируется.'), h('div', { className: 'actions' }, h('strong', null, policy.enabled ? 'Выдача включена' : 'Выдача отключена'), h('span', { className: 'muted' }, 'Поколение ' + policy.epoch), h(Button, { onClick: toggle, className: policy.enabled ? 'danger' : 'primary' }, policy.enabled ? 'Отключить резервную систему' : 'Включить резервную систему'))), h('section', { className: 'card' }, h('h2', null, 'Разрешить резерв для установки'), h('form', { onSubmit: issue }, h('div', { className: 'grid2' }, h(Select, { label: 'Лицензия (из загруженного списка)', value: licenseId, onChange: e => { setLicense(e.target.value); setDevice(''); }, options: [['', 'Выберите лицензию'], ...licenses.filter(x => x.status === 'active' && x.devices.length).map(x => [x.id, x.label || x.id])] }), h(Select, { label: 'Установка', value: deviceId, onChange: e => setDevice(e.target.value), options: [['', 'Выберите установку'], ...devices.map(x => [x.id, x.id.slice(0, 20) + '…'])] })), h('div', { className: 'grid2' }, h(Field, { label: 'Окно без обновления, часы', type: 'number', min: 1, max: 168, value: hours, onChange: e => setHours(e.target.value) }), h(Field, { label: 'Причина', value: reason, maxLength: 200, required: true, onChange: e => setReason(e.target.value) })), h(Button, { type: 'submit', disabled: !policy.enabled, className: 'primary' }, 'Выдать резервный допуск')), grant && h('div', { className: 'key-box' }, h('strong', null, 'Команда на нужной установке клиента'), h('code', { 'data-testid': 'reserve-command' }, 'lsptool license reserve-enable --id ' + grant.id), h(Button, { onClick: () => downloadText('reserve-access.txt', 'lsptool license reserve-enable --id ' + grant.id + '\n') }, 'Скачать инструкцию'))), h('section', { className: 'card' }, h('h2', null, 'Выданные допуски'), h('div', { className: 'table-wrap' }, h('table', null, h('thead', null, h('tr', null, ...['ID / причина', 'Окно', 'Состояние', ''].map(x => h('th', { key: x }, x)))), h('tbody', null, ...policy.grants.slice(0, 200).map(g => h('tr', { key: g.id }, h('td', null, h('strong', null, g.reason), h('code', null, g.id)), h('td', null, g.windowSeconds / 3600 + ' ч'), h('td', null, reserveStatus(g, policy)), h('td', null, h(Button, { onClick: () => revoke(g), disabled: g.revokedAt !== null }, 'Отозвать'))))))), policy.grants.length > 200 && h('p', { className: 'muted' }, 'Показаны 200 последних допусков. Полная история доступна владельцу через приватный API.')));
}
/** Paginated license list renders at most 25 rows and passes selection without exposing any raw key. */
function LicenseTable({ filtered, current, pages, search, status, setSearch, setStatus, setPage, setSelected, session, run, refresh }) {
    const rows = pageItems(filtered, current).map(x => h('tr', { key: x.id }, h('td', null, h('strong', null, x.label || 'Без метки'), h('code', null, x.id)), h('td', null, h('span', { className: 'badge ' + x.status }, labels[x.status])), h('td', null, x.permanent ? 'Бессрочно' : dateText(x.expiresAt)), h('td', null, `${x.devices.length} / ${x.maxDevices}`), h('td', null, h(Button, { onClick: () => setSelected(x.id) }, 'Открыть'))));
    return h('section', { className: 'card' }, h('div', { className: 'grid2' }, h(Field, { label: 'Поиск по клиенту или ID', value: search, onChange: e => { setSearch(e.target.value); setPage(0); }, maxLength: 120 }), h(Select, { label: 'Статус', value: status, onChange: e => { setStatus(e.target.value); setPage(0); }, options: [['', 'Все статусы'], ...Object.entries(labels)] })), h(RetryIssue, { session, run, refresh }), h('div', { className: 'table-wrap' }, h('table', null, h('thead', null, h('tr', null, ...['Клиент / ID', 'Статус', 'Окончание', 'Установки', ''].map(x => h('th', { key: x }, x)))), h('tbody', null, ...rows))), !filtered.length && h('p', { className: 'muted' }, 'Лицензий нет. Создайте первый ключ во вкладке «Выдать ключ».'), h('div', { className: 'pager' }, h(Button, { disabled: current === 0, onClick: () => setPage(current - 1) }, 'Назад'), h('span', null, `${current + 1} / ${pages}`), h(Button, { disabled: current >= pages - 1, onClick: () => setPage(current + 1) }, 'Далее')));
}
/** Last bounded audit entries contain timestamps and IDs, never passwords or complete keys. */
function AuditTable({ audit }) {
    return h('section', { className: 'card' }, h('h2', null, 'Последние 500 событий'), h('div', { className: 'table-wrap' }, h('table', null, h('thead', null, h('tr', null, ...['Время', 'Действие', 'Лицензия'].map(x => h('th', { key: x }, x)))), h('tbody', null, ...audit.map((x, i) => h('tr', { key: i }, h('td', null, dateText(x.at)), h('td', null, x.action), h('td', null, h('code', null, x.licenseId || 'Глобальная настройка'))))))));
}
/** Review independent session handles and close other browsers; no authentication cookies are exposed. */
function SessionsPanel({ sessions, session, run, refresh }) {
    /** Close selected/all-other sessions after explicit confirmation; repeated revocation is harmless. */
    async function close(target, others = false) {
        if (!confirm(others ? 'Завершить все другие сессии владельца?' : 'Завершить выбранную сессию?')) return;
        await run(async () => {
            await requestApi('/sessions/revoke', { sessionId: target, others }, session.csrfToken);
            await refresh();
        });
    }
    return h('section', { className: 'card' }, h('h2', null, 'Активные сессии'),
        h('p', { className: 'muted' }, 'Только браузеры владельца. Это не установки клиентов. Отзыв требует свежего подтверждения входа.'),
        h(Button, { onClick: () => close('', true), disabled: sessions.length < 2 }, 'Завершить остальные'),
        h('div', { className: 'table-wrap' }, h('table', null,
            h('thead', null, h('tr', null, ...['Сессия', 'Возраст', 'Без активности', ''].map(x => h('th', { key: x }, x)))),
            h('tbody', null, ...sessions.map(x => h('tr', { key: x.id },
                h('td', null, h('strong', null, x.current ? 'Текущий браузер' : 'Другой браузер'), h('code', null, x.id)),
                h('td', null, Math.floor(x.ageSeconds / 60) + ' мин'), h('td', null, x.idleSeconds + ' с'),
                h('td', null, h(Button, { onClick: () => close(x.id), disabled: x.current }, 'Завершить'))))))));
}
/** Main authenticated shell. No credentials in URL/localStorage; session failures remove the authenticated subtree. */
function ConsoleApp({ session, onLogout }) {
    const [tab, setTab] = useState('licenses');
    const [licenses, setLicenses] = useState([]);
    const [policy, setPolicy] = useState({ enabled: false, epoch: 0, grants: [] });
    const [audit, setAudit] = useState([]);
    const [sessions, setSessions] = useState([]);
    const [selected, setSelected] = useState('');
    const [search, setSearch] = useState('');
    const [status, setStatus] = useState('');
    const [page, setPage] = useState(0);
    const [busy, setBusy] = useState(false);
    const [error, setError] = useState('');
    const [reauth, setReauth] = useState(false);
    const [last, setLast] = useState(null);
    const lock = useRef(false);
    const mounted = useRef(true);
    /** Load current server state only on user activity, not a heartbeat that would defeat idle expiry. */
    /** Refresh bounded owner lists without silently renewing browser activity in a timer. */
    async function refresh() { const [a, b, c, d] = await Promise.all([requestApi('/licenses'), requestApi('/reserve'), requestApi('/audit'), requestApi('/sessions')]); if (mounted.current) {
        setLicenses(a);
        setPolicy(b);
        setAudit(c);
        setSessions(d);
        setLast(new Date());
    } }
    /** Serialize foreground operations and translate session/fresh-MFA failures without repeating side effects. */
    /** Serialize UI actions and surface authorization failures without retrying mutations automatically. */
    async function run(action) { if (lock.current)
        return; lock.current = true; setBusy(true); setError(''); try {
        await action();
    }
    catch (e) {
        if (e.code === 'WEB_UNAUTHORIZED') {
            clearPendingMutations();
            onLogout();
        }
        else {
            setError(e.code === 'WEB_REAUTH_REQUIRED' ? 'Нужно повторное подтверждение. После него нажмите действие ещё раз.' : e.message);
            if (e.code === 'WEB_REAUTH_REQUIRED')
                setReauth(true);
        }
    }
    finally {
        lock.current = false;
        if (mounted.current)
            setBusy(false);
    } }
    useEffect(() => { mounted.current = true; run(refresh); return () => { mounted.current = false; }; }, []);
    const filtered = licenses.filter(x => (!status || x.status === status) && (!search || (x.label + ' ' + x.id).toLowerCase().includes(search.toLowerCase())));
    const pages = Math.max(1, Math.ceil(filtered.length / 25));
    const current = Math.min(page, pages - 1);
    const item = licenses.find(x => x.id === selected);
    const aside = h('aside', null, h('div', { className: 'brand' }, h('span', { className: 'brand-mark' }, 'L'), h('div', null, h('strong', null, 'LSP LICENSES'), h('small', null, 'Панель владельца'))), h('nav', null, ...[['licenses', 'Лицензии'], ['issue', 'Выдать ключ'], ['reserve', 'Резервный доступ'], ['audit', 'Журнал действий'], ['sessions', 'Сессии владельца']].map(([key, text]) => h(Button, { key, className: tab === key ? 'nav active' : 'nav', disabled: busy, onClick: () => setTab(key) }, text))), h('div', { className: 'aside-note' }, 'Нет вечного мастер-ключа.', h('br'), 'Резерв: максимум 168 часов.', h('br'), 'Не публикуйте каталог authority.'));
    const header = h('header', null, h('div', null, h('div', { className: 'eyebrow' }, 'OWNER / CONTROL CENTER'), h('h1', null, { licenses: 'Лицензии', issue: 'Новый доступ', reserve: 'Резервный доступ', audit: 'Журнал действий', sessions: 'Сессии владельца' }[tab])), h('div', { className: 'actions' }, h('span', { className: 'muted' }, session.username), h(Button, { onClick: () => setReauth(true) }, 'Подтвердить вход'), h(Button, { onClick: () => run(async () => { await requestApi('/logout', {}, session.csrfToken); clearPendingMutations(); onLogout(); }) }, 'Выйти')));
    const stats = h('div', { className: 'stats' }, ...[[licenses.length, 'Всего'], [licenses.filter(x => x.status === 'active').length, 'Активны'], [licenses.filter(x => x.status === 'pending').length, 'Ожидают'], [policy.enabled ? 'ON' : 'OFF', 'Резервная система']].map(([value, label]) => h('div', { className: 'stat', key: label }, h('strong', null, value), h('span', null, label))));
    let content;
    if (tab === 'licenses')
        content = h(React.Fragment, null, h(LicenseTable, { filtered, current, pages, search, status, setSearch, setStatus, setPage, setSelected, session, run, refresh }), item && h(LicenseDetail, { key: item.id, item, session, run, refresh }));
    else if (tab === 'issue')
        content = h(IssuePanel, { session, run, refresh });
    else if (tab === 'reserve')
        content = h(ReservePanel, { policy, licenses, session, run, refresh });
    else if (tab === 'sessions')
        content = h(SessionsPanel, { sessions, session, run, refresh });
    else
        content = h(AuditTable, { audit });
    return h('div', { className: 'shell' }, aside, h('main', null, header, stats, h('div', { className: 'toolbar' }, h('span', { className: 'muted small' }, last ? 'Обновлено ' + last.toLocaleTimeString('ru-RU') : 'Загрузка…'), h(Button, { onClick: () => run(refresh), disabled: busy }, 'Обновить')), error && h('p', { className: 'error', role: 'alert' }, error), busy && h('p', { role: 'status', className: 'muted' }, 'Операция выполняется…'), reauth && h('div', { className: 'modal', role: 'dialog', 'aria-modal': 'true', 'aria-label': 'Повторное подтверждение' }, h(Credentials, { session, reauth: true, onSuccess: () => { setReauth(false); setError(''); }, onCancel: () => setReauth(false) })), h('fieldset', { className: 'content', disabled: busy }, content), h('footer', null, '0.17.0 · Только владелец · Ключи передаются отдельно · Время в часовом поясе браузера')));
}
/** Session bootstrap exposes no API token and removes authenticated UI after logout. */
function App() { const [session, setSession] = useState(null); const [loading, setLoading] = useState(true); useEffect(() => { requestApi('/session').then(setSession).catch(() => { }).finally(() => setLoading(false)); }, []); if (loading)
    return h('div', { className: 'loading' }, 'Проверка сессии…'); return session ? h(ConsoleApp, { session, onLogout: () => setSession(null) }) : h('div', { className: 'login-page' }, h(Credentials, { onSuccess: setSession })); }
createRoot(document.getElementById('root')).render(h(App));
