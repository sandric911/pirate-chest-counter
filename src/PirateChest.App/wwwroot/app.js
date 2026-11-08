'use strict';

// ---------------------------------------------------------------- language

const plRu = (k, one, few, many) => {
  const m10 = k % 10, m100 = k % 100;
  if (m10 === 1 && m100 !== 11) return one;
  if (m10 >= 2 && m10 <= 4 && (m100 < 12 || m100 > 14)) return few;
  return many;
};
const plEn = (k, one, many) => (k === 1 ? one : many);

const I18N = {
  ru: {
    locale: 'ru-RU',
    title: 'Счётчик сундуков',
    brand: 'Счётчик сундуков',
    tabHome: 'Главная', tabHistory: 'История', tabEfficiency: 'Эффективность', tabMap: 'Карта', tabUpdates: 'Обновления',
    loadFail: 'Не удалось загрузить данные: ',
    recHow: 'Как включить запись повторов — во вкладке <a href="#/updates">«Обновления»</a>.',
    counting: (d, t) => `Считаю матчи: ${d} из ${t}`,
    matches: (k) => plRu(k, 'матч', 'матча', 'матчей'),
    times: (k) => plRu(k, 'раз', 'раза', 'раз'),
    inMatches: (k) => `в ${k} ${plRu(k, 'матче', 'матчах', 'матчах')}`,
    justNow: 'только что', minAgo: (m) => `${m} мин назад`, hAgo: (h) => `${h} ч назад`, dAgo: (d) => `${d} дн назад`,
    min: 'мин',
    typeNormal: 'Обычные', typeRare: 'Редкие', typeSprite: 'Спрайтовые',
    oneNormal: 'Обычный сундук', oneRare: 'Редкий сундук', oneSprite: 'Спрайтовый сундук', chest: 'Сундук',
    myChests: 'Мои сундуки', inN: (n, w) => `за ${n} ${w}`, meSquad: 'Мои + отряд', meSquadSub: 'вместе с тиммейтами, не меньше',
    session: 'Текущая сессия', sessionOff: 'начнётся с первым матчем', sessionPlaying: 'идёт матч',
    sessionPause: (p, r) => `перерыв ${p} мин, сброс через ${r} мин`,
    today: 'Сегодня', lastMatch: 'Последний матч', mine: 'мои', squad: 'отряд', withSquad: 'с отрядом',
    lastLine: (m, s) => `+${m} <small>мои · +${s} с отрядом</small>`, noMatchesYet: 'матчей пока нет',
    byType: 'По типам сундуков', withSquadN: (n) => `с отрядом: ${n}`, bestMatch: 'лучший матч', avgPerMatch: 'в среднем за матч',
    matchesN: (n) => `матчей: ${n}`,
    history: 'История', perDay: 'Сундуки по дням', cumulative: 'Нарастающий итог', matchesTitle: 'Матчи', skippedTitle: 'Пропущено',
    colDate: 'Дата', colMode: 'Режим', colLength: 'Длительность', colPlace: 'Место', colMine: 'Мои', colSquad: 'Отряд', colReason: 'Причина',
    noData: 'Нет данных', noMatches: 'Матчей пока нет',
    efficiency: 'Эффективность фарма', perMinute: 'В минуту', perMinuteSub: 'сундуков за минуту игры', perMatch: 'За матч',
    onAverage: 'в среднем', record: 'Рекорд', ranking: 'Рейтинг мест высадки', colSpot: 'Место', colMatches: 'Матчей',
    colPerMinute: 'В минуту', colPerMatch: 'За матч', colBest: 'Лучший', byMatch: 'По матчам', colLanding: 'Высадка', colInGame: 'В игре',
    map: 'Карта', show: 'Что показать', allMatches: 'Все матчи', allTypes: 'Все типы сундуков',
    layerMe: 'открытые мной', layerSquad: 'открытые только отрядом', layerOther: 'остальные сундуки', layerHeat: 'тепловая карта',
    howMany: 'Сколько раз я открыл', once: '1 раз', sevenPlus: '7 и больше', squadOnly: 'только отряд', others: 'остальные',
    mapHint: 'Цифра на кружке — сколько раз. Колёсико мыши приближает, карту можно двигать.',
    zoomIn: 'Приблизить', zoomOut: 'Отдалить', mapCredit: 'Карта: Fortnite © Epic Games',
    openedByMe: 'Открыто мной', distinct: 'Разных сундуков', totalOpens: 'Всего открытий', reopened: 'Открывал повторно', mostOften: 'Чаще всего',
    nobodyOpened: 'ещё никто из отряда не открывал', notInMatch: 'в этом матче не открывали',
    iOpened: (n, w) => `я открыл: ${n} ${w}`, squadOpened: (n, w) => `отряд: ${n} ${w}`,
    updates: 'Обновления', settings: 'Настройки', language: 'Язык', program: 'Программа', version: 'Версия', updatesKey: 'Обновления',
    autostart: 'Автозапуск', on: 'включён', off: 'выключен', checkUpdates: 'Проверить обновления', checking: 'Проверяю…',
    replays: 'Реплеи', inFortnite: 'В папке Fortnite', noFolder: 'папки нет', copies: 'Сохранено копий', latest: 'Последний',
    fnRunning: 'запущен', fnStopped: 'не запущен', countingKey: 'Считаю', of: (a, b) => `${a} из ${b}`, recount: 'Пересчитать всё',
    howTo: 'Как включить запись повторов',
    steps: [
      'В Fortnite откройте меню → <b>Настройки</b>.',
      'Вкладка <b>«Игра»</b> → раздел <b>«Повторы»</b>.',
      'Включите <b>запись повторов</b> для Королевской битвы.',
      'Играйте — после каждого матча счётчик обновится сам.',
    ],
    mb: 'МБ', gb: 'ГБ',
  },
  en: {
    locale: 'en-GB',
    title: 'Chest Counter',
    brand: 'Chest Counter',
    tabHome: 'Home', tabHistory: 'History', tabEfficiency: 'Efficiency', tabMap: 'Map', tabUpdates: 'Updates',
    loadFail: 'Could not load data: ',
    recHow: 'How to turn on replay recording: see the <a href="#/updates">Updates</a> tab.',
    counting: (d, t) => `Counting matches: ${d} of ${t}`,
    matches: (k) => plEn(k, 'match', 'matches'),
    times: (k) => plEn(k, 'time', 'times'),
    inMatches: (k) => `in ${k} ${plEn(k, 'match', 'matches')}`,
    justNow: 'just now', minAgo: (m) => `${m} min ago`, hAgo: (h) => `${h} h ago`, dAgo: (d) => `${d} d ago`,
    min: 'min',
    typeNormal: 'Normal', typeRare: 'Rare', typeSprite: 'Sprite',
    oneNormal: 'Normal chest', oneRare: 'Rare chest', oneSprite: 'Sprite chest', chest: 'Chest',
    myChests: 'My chests', inN: (n, w) => `in ${n} ${w}`, meSquad: 'Me + squad', meSquadSub: 'with teammates, at least',
    session: 'Current session', sessionOff: 'starts with your first match', sessionPlaying: 'match in progress',
    sessionPause: (p, r) => `break ${p} min, resets in ${r} min`,
    today: 'Today', lastMatch: 'Last match', mine: 'mine', squad: 'squad', withSquad: 'with squad',
    lastLine: (m, s) => `+${m} <small>mine · +${s} with squad</small>`, noMatchesYet: 'no matches yet',
    byType: 'By chest type', withSquadN: (n) => `with squad: ${n}`, bestMatch: 'best match', avgPerMatch: 'average per match',
    matchesN: (n) => `matches: ${n}`,
    history: 'History', perDay: 'Chests per day', cumulative: 'Running total', matchesTitle: 'Matches', skippedTitle: 'Skipped',
    colDate: 'Date', colMode: 'Mode', colLength: 'Length', colPlace: 'Place', colMine: 'Mine', colSquad: 'Squad', colReason: 'Reason',
    noData: 'No data', noMatches: 'No matches yet',
    efficiency: 'Farming efficiency', perMinute: 'Per minute', perMinuteSub: 'chests per minute in game', perMatch: 'Per match',
    onAverage: 'on average', record: 'Record', ranking: 'Landing spots', colSpot: 'Spot', colMatches: 'Matches',
    colPerMinute: 'Per minute', colPerMatch: 'Per match', colBest: 'Best', byMatch: 'By match', colLanding: 'Landing', colInGame: 'In game',
    map: 'Map', show: 'Show', allMatches: 'All matches', allTypes: 'All chest types',
    layerMe: 'opened by me', layerSquad: 'opened only by squad', layerOther: 'other chests', layerHeat: 'heat map',
    howMany: 'How many times I opened', once: 'once', sevenPlus: '7 or more', squadOnly: 'squad only', others: 'others',
    mapHint: 'The number is how many times. Scroll to zoom, drag to move the map.',
    zoomIn: 'Zoom in', zoomOut: 'Zoom out', mapCredit: 'Map: Fortnite © Epic Games',
    openedByMe: 'Opened by me', distinct: 'Different chests', totalOpens: 'Total opens', reopened: 'Opened more than once', mostOften: 'Most often',
    nobodyOpened: 'nobody in the squad has opened it yet', notInMatch: 'not opened in this match',
    iOpened: (n, w) => `I opened it ${n} ${w}`, squadOpened: (n, w) => `squad: ${n} ${w}`,
    updates: 'Updates', settings: 'Settings', language: 'Language', program: 'Program', version: 'Version', updatesKey: 'Updates',
    autostart: 'Start with Windows', on: 'on', off: 'off', checkUpdates: 'Check for updates', checking: 'Checking…',
    replays: 'Replays', inFortnite: 'In the Fortnite folder', noFolder: 'no folder', copies: 'Copies saved', latest: 'Latest',
    fnRunning: 'running', fnStopped: 'not running', countingKey: 'Counting', of: (a, b) => `${a} of ${b}`, recount: 'Recount everything',
    howTo: 'How to turn on replay recording',
    steps: [
      'In Fortnite open the menu → <b>Settings</b>.',
      '<b>Game</b> tab → <b>Replays</b> section.',
      'Turn on <b>Record Replays</b> for Battle Royale.',
      'Play: the counter updates itself after every match.',
    ],
    mb: 'MB', gb: 'GB',
  },
};

let LANG = 'ru';
const t = (k, ...a) => {
  const v = I18N[LANG][k] ?? I18N.ru[k] ?? k;
  return typeof v === 'function' ? v(...a) : v;
};
const typeName = (type) => t(`type${type}`);
const typeOne = (type) => t(`one${type}`);

function applyLang() {
  document.documentElement.lang = LANG;
  document.title = t('title');
  document.querySelectorAll('[data-t]').forEach((el) => { el.textContent = t(el.dataset.t); });
}

// ---------------------------------------------------------------- helpers

const $ = (sel, root = document) => root.querySelector(sel);
const esc = (s) => String(s ?? '').replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
const n = (v) => new Intl.NumberFormat(t('locale')).format(v ?? 0);
const n1 = (v) => (v == null ? '—' : new Intl.NumberFormat(t('locale'), { maximumFractionDigits: 2 }).format(v));
/** Generic UI glyph (inline SVG symbol). */
const icon = (name, cls = '') => `<svg class="ico ${cls}"><use href="#i-${name}"/></svg>`;
/** Fortnite icon from img/ (white silhouette, tinted with the text colour). */
const wi = (name, cls = '') => `<span class="wi ${cls}" style="--src:url(/img/${name}.webp)"></span>`;

async function api(path) {
  const r = await fetch(path, { cache: 'no-store' });
  if (!r.ok) throw new Error(`${path}: ${r.status}`);
  return r.json();
}
async function post(path, body) {
  const r = await fetch(path, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', 'X-PCC': '1' },
    body: body == null ? '{}' : JSON.stringify(body),
  });
  if (!r.ok) throw new Error(`${path}: ${r.status}`);
  const txt = await r.text();
  return txt ? JSON.parse(txt) : null;
}

function dt(iso, withYear = false) {
  if (!iso) return '—';
  return new Date(iso).toLocaleString(t('locale'), { day: '2-digit', month: '2-digit', ...(withYear ? { year: 'numeric' } : {}), hour: '2-digit', minute: '2-digit' });
}
function ago(iso) {
  if (!iso) return '';
  const s = (Date.now() - new Date(iso).getTime()) / 1000;
  if (s < 90) return t('justNow');
  if (s < 3600) return t('minAgo', Math.round(s / 60));
  if (s < 86400 * 2) return t('hAgo', Math.round(s / 3600));
  return t('dAgo', Math.round(s / 86400));
}
/** 1:05:09 */
function clock(ms) {
  const v = Math.max(0, Math.floor(ms / 1000));
  const h = Math.floor(v / 3600), m = Math.floor((v % 3600) / 60), s = v % 60;
  return `${h}:${String(m).padStart(2, '0')}:${String(s).padStart(2, '0')}`;
}

const tip = $('#tip');
function showTip(e, html) {
  tip.innerHTML = html;
  tip.style.display = 'block';
  tip.style.left = `${e.clientX + 16}px`;
  tip.style.top = `${e.clientY + 16}px`;
}
function hideTip() { tip.style.display = 'none'; }

// ---------------------------------------------------------------- sortable tables

const sortState = {};
const tables = {};

/** cols: [{ label, value(row) for sorting, html(row) for the cell, num, cls, sort:false }] */
function tableHtml(id, cols, rows, { empty = t('noData'), sort = null } = {}) {
  if (!sortState[id] && sort) sortState[id] = sort;
  tables[id] = { cols, rows, empty };
  return `<div class="boardwrap"><table class="board" id="${id}"><thead><tr>${headHtml(id)}</tr></thead><tbody>${bodyHtml(id)}</tbody></table></div>`;
}
function headHtml(id) {
  const { cols } = tables[id];
  const st = sortState[id];
  return cols.map((c, i) => `<th class="${c.num ? 'n ' : ''}${c.sort === false ? '' : 'sort'} ${st && st.i === i ? st.dir : ''}" data-i="${i}">${c.label}</th>`).join('');
}
function bodyHtml(id) {
  const { cols, rows, empty } = tables[id];
  if (!rows.length) return `<tr><td colspan="${cols.length}" class="empty">${empty}</td></tr>`;
  const st = sortState[id];
  let list = rows;
  if (st) {
    const val = cols[st.i].value ?? (() => 0);
    const k = st.dir === 'asc' ? 1 : -1;
    list = [...rows].sort((a, b) => {
      const x = val(a), y = val(b);
      if (x == null && y == null) return 0;
      if (x == null) return 1;
      if (y == null) return -1;
      return (typeof x === 'string' ? x.localeCompare(y, LANG) : x - y) * k;
    });
  }
  return list.map((r) => `<tr>${cols.map((c) => `<td class="${c.num ? 'n ' : ''}${c.cls ?? ''}">${c.html(r)}</td>`).join('')}</tr>`).join('');
}
function bindTables(root = document) {
  root.querySelectorAll('table.board[id]').forEach((tb) => {
    if (!tables[tb.id]) return;
    tb.querySelector('thead').addEventListener('click', (e) => {
      const th = e.target.closest('th.sort');
      if (!th) return;
      const i = +th.dataset.i;
      const st = sortState[tb.id];
      const first = tables[tb.id].cols[i].num ? 'desc' : 'asc';
      sortState[tb.id] = { i, dir: st && st.i === i ? (st.dir === 'asc' ? 'desc' : 'asc') : first };
      tb.querySelector('thead tr').innerHTML = headHtml(tb.id);
      tb.querySelector('tbody').innerHTML = bodyHtml(tb.id);
    });
  });
}

/** Place badge: 1 gold (with the Victory Royale crown), 2 silver, 3 bronze. */
function rankBadge(r, crown = false) {
  if (r == null) return '<span class="muted">—</span>';
  const cls = r === 1 ? 'r1' : r === 2 ? 'r2' : r === 3 ? 'r3' : '';
  return `<span class="rank ${cls}">${crown && r === 1 ? wi('crown') : ''}#${r}</span>`;
}

// ---------------------------------------------------------------- router

const view = $('#view');
let timers = [];
let current = null;

const routes = {
  '': renderHome,
  history: renderHistory,
  efficiency: renderEfficiency,
  map: renderMap,
  updates: renderUpdates,
  status: renderUpdates,
};

function every(ms, fn) { timers.push(setInterval(fn, ms)); }

function route() {
  let name = location.hash.replace(/^#\/?/, '').split('?')[0];
  if (name === 'status') name = 'updates';
  const fn = routes[name] ?? renderHome;
  document.querySelectorAll('#tabs a').forEach((a) => a.classList.toggle('active', a.dataset.tab === name));
  timers.forEach(clearInterval);
  timers = [];
  if (current?.dispose) current.dispose();
  current = null;
  hideTip();
  fn().catch((e) => { view.innerHTML = `<div class="banner">${icon('warn')}<div>${t('loadFail')}${esc(e.message)}</div></div>`; });
}
window.addEventListener('hashchange', () => { window.scrollTo(0, 0); route(); });

function warnBanner(text) {
  return text ? `<div class="banner">${icon('warn')}<div><b>${esc(text)}</b><br>${t('recHow')}</div></div>` : '';
}
function progressBanner(p) {
  if (!p.busy) return '';
  const pct = p.total ? Math.round((100 * p.done) / p.total) : 5;
  return `<div class="banner info">${icon('refresh')}<div style="flex:1"><b>${t('counting', n(p.done), n(p.total || p.pending))}</b>
    <div class="progress"><div style="width:${pct}%"></div></div></div></div>`;
}

// ---------------------------------------------------------------- home

const TYPES = {
  Normal: { cls: 'normal', img: '/img/chest.webp' },
  Rare: { cls: 'rare', img: '/img/chest-rare.webp' },
  Sprite: { cls: 'sprite', img: '/img/chest-sprite.webp' },
};

/** Session clock state; the browser clock may differ from the PC clock the server uses. */
let session = null;

function sessionNote(s, now) {
  if (!s.active) return t('sessionOff');
  if (s.playing) return `<span class="live"></span>${t('sessionPlaying')}`;
  if (!s.lastEndUtc) return '';
  const pause = Math.max(0, Math.floor((now - new Date(s.lastEndUtc).getTime()) / 60000));
  return t('sessionPause', pause, Math.max(0, s.gapMin - pause));
}

function tickSession() {
  const el = $('#sessClock');
  if (!el || !session || !session.active) return;
  const now = Date.now() + session.offset;
  el.textContent = clock(now - new Date(session.startedUtc).getTime());
  $('#sessNote').innerHTML = sessionNote(session, now);
}

async function renderHome() {
  const draw = async () => {
    const [s, st] = await Promise.all([api('/api/summary'), api('/api/status')]);
    session = { ...s.session, offset: new Date(s.session.serverNowUtc).getTime() - Date.now() };
    const last = s.lastMatch;
    const ss = s.session;
    view.innerHTML = `
      ${warnBanner(st.recordingWarning)}
      ${progressBanner(s.progress)}
      <div class="grid g2">
        <div class="tile me hero">
          <img class="pic" src="/img/chest.webp" alt="">
          <div class="value">${n(s.mine)}</div>
          <div class="name">${t('myChests')}</div><div class="sub">${t('inN', n(s.matches), t('matches', s.matches))}</div>
        </div>
        <div class="tile squad hero">
          ${wi('squads', 'pic')}
          <div class="value">${n(s.squad)}</div>
          <div class="name">${t('meSquad')}</div><div class="sub">${t('meSquadSub')}</div>
        </div>
      </div>
      <div class="grid g3 section">
        <div class="panel"><h3>${t('session')}</h3>
          <div class="clock ${ss.active ? '' : 'off'}">${wi('timer')}<span id="sessClock">${ss.active ? clock(Date.now() + session.offset - new Date(ss.startedUtc).getTime()) : '—'}</span></div>
          <div class="clocknote" id="sessNote">${sessionNote(ss, Date.now() + session.offset)}</div>
          <div class="stats small">
            <div class="stat"><div class="v gold">${n(ss.mine)}</div><div class="k">${t('mine')}</div></div>
            <div class="stat"><div class="v sea">${n(ss.squad)}</div><div class="k">${t('squad')}</div></div>
            <div class="stat"><div class="v">${n(ss.matches)}</div><div class="k">${t('matches', ss.matches)}</div></div></div></div>
        <div class="panel"><h3>${t('today')}</h3><div class="stats">
          <div class="stat"><div class="v gold">${n(s.today.mine)}</div><div class="k">${t('mine')}</div></div>
          <div class="stat"><div class="v sea">${n(s.today.squad)}</div><div class="k">${t('squad')}</div></div>
          <div class="stat"><div class="v">${n(s.today.matches)}</div><div class="k">${t('matches', s.today.matches)}</div></div></div></div>
        <div class="panel"><h3>${t('lastMatch')}</h3>${last ? `
          <div class="stat"><div class="v gold">${t('lastLine', n(last.mine), n(last.squad))}</div>
          <div class="d">${esc(last.mode)} · ${dt(last.endedUtc)}</div></div>` : `<div class="stat"><div class="v muted">—</div><div class="d">${t('noMatchesYet')}</div></div>`}</div>
      </div>
      <div class="section">
        <h2>${t('byType')}</h2>
        <div class="grid g3">${s.byType.map((x) => `
          <div class="tile ${TYPES[x.type]?.cls ?? 'normal'} typetile">
            <img class="pic" src="${TYPES[x.type]?.img ?? TYPES.Normal.img}" alt="">
            <div class="count">${n(x.mine)}</div>
            <div class="name">${esc(typeName(x.type))}</div><div class="sub">${t('withSquadN', n(x.squad))}</div>
          </div>`).join('')}
        </div>
        <div class="ministats">
          <div class="ministat">${wi('crown')}<div><div class="v">${n(s.best)}</div><div class="k">${t('bestMatch')}</div></div></div>
          <div class="ministat">${wi('chest-icon')}<div><div class="v">${n1(s.perMatch)}</div><div class="k">${t('avgPerMatch')}</div></div></div>
        </div>
      </div>`;
  };
  await draw();
  every(5000, () => draw().catch(() => {}));
  every(1000, tickSession);
}

// ---------------------------------------------------------------- charts (tiny, offline)

const ME = '#d9b24c', SQUAD = '#5b9fd0';

function niceStep(max) {
  const raw = max / 5;
  const p = Math.pow(10, Math.floor(Math.log10(raw)));
  const m = raw / p;
  return (m <= 1 ? 1 : m <= 2 ? 2 : m <= 5 ? 5 : 10) * p;
}
const shortDate = (d) => { const [, m, day] = d.split('-'); return LANG === 'en' ? `${day}/${m}` : `${day}.${m}`; };
const longDate = (d) => new Date(`${d}T12:00:00`).toLocaleDateString(t('locale'), { weekday: 'short', day: 'numeric', month: 'long' });

function axes(parts, W, R, L, top, step, y) {
  for (let v = 0; v <= top; v += step) {
    parts.push(`<g class="grid"><line x1="${L}" x2="${W - R}" y1="${y(v)}" y2="${y(v)}"/></g><g class="axis"><text x="${L - 10}" y="${y(v) + 7}" text-anchor="end">${n(v)}</text></g>`);
  }
}

function barChart(el, rows) {
  const W = 1000, H = 300, L = 70, R = 30, T = 16, B = 44;
  const max = Math.max(1, ...rows.map((r) => r.squad));
  const step = niceStep(max), top = Math.ceil(max / step) * step;
  const bw = (W - L - R) / Math.max(rows.length, 1);
  const y = (v) => T + (H - T - B) * (1 - v / top);
  const parts = [`<svg viewBox="0 0 ${W} ${H}" role="img">`];
  axes(parts, W, R, L, top, step, y);
  rows.forEach((r, i) => {
    const x = L + i * bw, w = Math.max(4, bw * 0.66);
    const bar = (x0, ww, v, fill) => `<rect x="${x0}" y="${y(v)}" width="${ww}" height="${Math.max(0, y(0) - y(v))}" rx="2" fill="${fill}" data-i="${i}"/>`;
    parts.push(bar(x + (bw - w) / 2, w, r.squad, SQUAD));
    parts.push(bar(x + (bw - w) / 2 + w * 0.2, w * 0.6, r.mine, ME));
    if (rows.length <= 40 || i % Math.ceil(rows.length / 30) === 0) parts.push(`<g class="axis"><text x="${x + bw / 2}" y="${H - 12}" text-anchor="middle">${esc(shortDate(r.date))}</text></g>`);
  });
  parts.push('</svg>');
  el.innerHTML = `<div class="legend"><span><i style="background:${ME}"></i>${t('mine')}</span><span><i style="background:${SQUAD}"></i>${t('withSquad')}</span></div>${parts.join('')}`;
  el.querySelectorAll('rect[data-i]').forEach((p) => {
    p.addEventListener('mousemove', (e) => {
      const r = rows[+p.dataset.i];
      showTip(e, `<b>${esc(longDate(r.date))}</b><br>${t('mine')}: ${n(r.mine)}<br>${t('withSquad')}: ${n(r.squad)}<br>${t('matchesN', n(r.matches))}`);
    });
    p.addEventListener('mouseleave', hideTip);
  });
}

function lineChart(el, rows) {
  const series = [{ key: 'cumSquad', color: SQUAD, label: t('withSquad') }, { key: 'cumMine', color: ME, label: t('mine') }];
  const W = 1000, H = 300, L = 80, R = 34, T = 16, B = 44;
  const max = Math.max(1, ...rows.flatMap((r) => series.map((s) => r[s.key])));
  const step = niceStep(max), top = Math.ceil(max / step) * step;
  const x = (i) => L + (W - L - R) * (rows.length <= 1 ? 0.5 : i / (rows.length - 1));
  const y = (v) => T + (H - T - B) * (1 - v / top);
  const parts = [`<svg viewBox="0 0 ${W} ${H}" role="img">`];
  axes(parts, W, R, L, top, step, y);
  rows.forEach((r, i) => { if (rows.length <= 40 || i % Math.ceil(rows.length / 30) === 0) parts.push(`<g class="axis"><text x="${x(i)}" y="${H - 12}" text-anchor="middle">${esc(shortDate(r.date))}</text></g>`); });
  for (const s of series) {
    const pts = rows.map((r, i) => `${x(i)},${y(r[s.key])}`).join(' ');
    parts.push(`<polyline points="${pts}" fill="none" stroke="${s.color}" stroke-width="3.5" stroke-linejoin="round"/>`);
    rows.forEach((r, i) => parts.push(`<circle cx="${x(i)}" cy="${y(r[s.key])}" r="${rows.length <= 40 ? 5.5 : 3}" fill="${s.color}" data-i="${i}"/>`));
  }
  parts.push('</svg>');
  el.innerHTML = `<div class="legend">${series.map((s) => `<span><i style="background:${s.color}"></i>${s.label}</span>`).join('')}</div>${parts.join('')}`;
  el.querySelectorAll('circle[data-i]').forEach((c) => {
    c.addEventListener('mousemove', (e) => {
      const r = rows[+c.dataset.i];
      showTip(e, `<b>${esc(longDate(r.date))}</b><br>${series.map((s) => `${s.label}: ${n(r[s.key])}`).join('<br>')}`);
    });
    c.addEventListener('mouseleave', hideTip);
  });
}

// ---------------------------------------------------------------- history

async function renderHistory() {
  const [daily, matches] = await Promise.all([api('/api/daily'), api('/api/matches')]);
  const counted = matches.filter((m) => m.status === 'counted');
  const skipped = matches.filter((m) => m.status !== 'counted');
  view.innerHTML = `
    <h1>${t('history')}</h1>
    <div class="grid g2">
      <div class="panel chart"><h2>${t('perDay')}</h2><div id="c1"></div></div>
      <div class="panel chart"><h2>${t('cumulative')}</h2><div id="c2"></div></div>
    </div>
    <div class="section"><h2>${t('matchesTitle')} · ${n(counted.length)}</h2>
      ${tableHtml('tMatches', [
        { label: t('colDate'), value: (m) => m.startedUtc, html: (m) => dt(m.startedUtc) },
        { label: t('colMode'), value: (m) => m.mode, html: (m) => esc(m.mode) },
        { label: t('colLength'), num: true, value: (m) => m.durationMin, html: (m) => `${n1(m.durationMin)} ${t('min')}` },
        { label: t('colPlace'), num: true, value: (m) => (m.placement == null ? null : -m.placement), html: (m) => rankBadge(m.placement, true) },
        { label: t('colMine'), num: true, cls: 'big gold', value: (m) => m.mine, html: (m) => n(m.mine) },
        { label: t('colSquad'), num: true, cls: 'big sea', value: (m) => m.squad, html: (m) => n(m.squad) },
      ], counted, { empty: t('noMatches'), sort: { i: 0, dir: 'desc' } })}
    </div>
    ${skipped.length ? `<div class="section"><h2>${t('skippedTitle')} · ${n(skipped.length)}</h2>
      ${tableHtml('tSkipped', [
        { label: t('colDate'), value: (m) => m.startedUtc, html: (m) => dt(m.startedUtc, true) },
        { label: t('colMode'), value: (m) => m.playlist, html: (m) => esc(m.playlist || '—') },
        { label: t('colReason'), value: (m) => m.skipReason, cls: 'wrap', html: (m) => esc(m.skipReason) },
      ], skipped, { sort: { i: 0, dir: 'desc' } })}</div>` : ''}`;
  bindTables(view);
  if (daily.length) {
    barChart($('#c1'), daily);
    lineChart($('#c2'), daily);
  } else {
    $('#c1').innerHTML = $('#c2').innerHTML = `<div class="empty">${t('noData')}</div>`;
  }
}

// ---------------------------------------------------------------- efficiency

async function renderEfficiency() {
  const e = await api('/api/efficiency');
  const o = e.overall;
  const ranked = [...e.ranking].sort((a, b) => b.cpm - a.cpm || b.avgMine - a.avgMine).map((r, i) => ({ ...r, rank: i + 1 }));
  view.innerHTML = `
    <h1>${t('efficiency')}</h1>
    <div class="grid g3">
      <div class="tile me stattile">${wi('timer', 'pic')}<div class="value">${n1(o.cpm)}</div><div class="name">${t('perMinute')}</div><div class="sub">${t('perMinuteSub')}</div></div>
      <div class="tile rare stattile">${wi('chest-icon', 'pic')}<div class="value">${n1(o.perMatch)}</div><div class="name">${t('perMatch')}</div><div class="sub">${t('onAverage')}</div></div>
      <div class="tile sprite stattile">${wi('crown', 'pic')}<div class="value">${n(o.best)}</div><div class="name">${t('record')}</div><div class="sub">${t('bestMatch')}</div></div>
    </div>
    <div class="section"><h2>${t('ranking')}</h2>
      ${tableHtml('tRank', [
        { label: '#', num: true, value: (r) => -r.rank, html: (r) => rankBadge(r.rank) },
        { label: t('colSpot'), value: (r) => r.name, html: (r) => `<span class="placename">${r.isPoi ? icon('pin') : ''}${esc(r.name)}</span>` },
        { label: t('colMatches'), num: true, value: (r) => r.matches, html: (r) => n(r.matches) },
        { label: t('colPerMinute'), num: true, cls: 'big gold', value: (r) => r.cpm, html: (r) => n1(r.cpm) },
        { label: t('colPerMatch'), num: true, value: (r) => r.avgMine, html: (r) => n1(r.avgMine) },
        { label: t('colBest'), num: true, value: (r) => r.best, html: (r) => n(r.best) },
      ], ranked, { sort: { i: 0, dir: 'desc' } })}
    </div>
    <div class="section"><h2>${t('byMatch')}</h2>
      ${tableHtml('tEffMatches', [
        { label: t('colDate'), value: (m) => m.startedUtc, html: (m) => dt(m.startedUtc) },
        { label: t('colMode'), value: (m) => m.mode, html: (m) => esc(m.mode) },
        { label: t('colLanding'), value: (m) => m.place, html: (m) => `<span class="placename">${esc(m.place)}</span>` },
        { label: t('colInGame'), num: true, value: (m) => m.minutes, html: (m) => `${n1(m.minutes)} ${t('min')}` },
        { label: t('colMine'), num: true, cls: 'big gold', value: (m) => m.mine, html: (m) => n(m.mine) },
        { label: t('colPerMinute'), num: true, value: (m) => m.cpm, html: (m) => n1(m.cpm) },
      ], [...e.matches], { sort: { i: 0, dir: 'desc' } })}
    </div>`;
  bindTables(view);
}

// ---------------------------------------------------------------- map

/** How many times I opened a chest → marker colour and size (CSS px). */
const LOOT_CLASSES = [
  { min: 1, color: '#f4e39a', ink: '#2a2310', r: 4, label: () => t('once') },
  { min: 2, color: '#f0b43c', ink: '#2a2008', r: 6, label: () => '2–3' },
  { min: 4, color: '#e47834', ink: '#2a1306', r: 7, label: () => '4–6' },
  { min: 7, color: '#d2463e', ink: '#ffffff', r: 8.5, label: () => t('sevenPlus') },
];
const lootClass = (k) => [...LOOT_CLASSES].reverse().find((c) => k >= c.min);

async function renderMap() {
  const [matches, status, calib] = await Promise.all([api('/api/matches'), api('/api/status'), api('/api/map/calibration')]);
  const counted = matches.filter((m) => m.status === 'counted');
  view.innerHTML = `
    <h1>${t('map')}</h1>
    <div class="maplayout">
      <div class="mapcol"><div class="mapframe"><canvas id="mapc" width="1200" height="1200"></canvas>
        <div class="zoom"><button class="icon" id="zIn" title="${t('zoomIn')}">+</button><button class="icon" id="zOut" title="${t('zoomOut')}">−</button></div>
        ${status.hasMap ? `<div class="credit">${t('mapCredit')}</div>` : ''}</div></div>
      <div class="mapside">
        <div class="panel">
          <h3>${t('show')}</h3>
          <select id="mMatch"><option value="">${t('allMatches')}</option>${counted.map((m) => `<option value="${esc(m.id)}">${dt(m.startedUtc)} · ${esc(m.mode)} · ${n(m.mine)}</option>`).join('')}</select>
          <select id="mType"><option value="">${t('allTypes')}</option>${Object.keys(TYPES).map((k) => `<option value="${k}">${typeName(k)}</option>`).join('')}</select>
          <div class="layers">
            <label class="chk"><input type="checkbox" id="lMe" checked> ${t('layerMe')}</label>
            <label class="chk"><input type="checkbox" id="lSquad" checked> ${t('layerSquad')}</label>
            <label class="chk"><input type="checkbox" id="lOther" checked> ${t('layerOther')}</label>
            <label class="chk"><input type="checkbox" id="lHeat"> ${t('layerHeat')}</label>
          </div>
        </div>
        <div class="panel">
          <h3>${t('howMany')}</h3>
          <div class="maplegend">
            ${LOOT_CLASSES.map((c) => `<div><span class="sw"><span class="mk" style="width:${2 * c.r + 2}px;height:${2 * c.r + 2}px;background:${c.color}"></span></span>${c.label()}</div>`).join('')}
            <div><span class="sw"><span class="mk" style="width:10px;height:10px;background:rgba(0,0,0,.35);border:2px solid var(--squad)"></span></span>${t('squadOnly')}</div>
            <div><span class="sw"><span class="mk" style="width:5px;height:5px;background:rgba(205,212,224,.7);border:0"></span></span>${t('others')}</div>
          </div>
          <div class="maphint">${t('mapHint')}</div>
        </div>
        <div class="panel" id="mStats"></div>
      </div>
    </div>`;

  const mv = new MapView($('#mapc'), status.hasMap, calib);
  current = mv;
  const reload = async () => {
    const id = $('#mMatch').value;
    mv.data = await api(`/api/map/chests${id ? `?match=${encodeURIComponent(id)}` : ''}`);
    mv.draw();
    renderMapStats(mv);
  };
  const sync = () => {
    mv.layers = { me: $('#lMe').checked, squad: $('#lSquad').checked, other: $('#lOther').checked, heat: $('#lHeat').checked, type: $('#mType').value };
    mv.draw();
    renderMapStats(mv);
  };
  $('#mMatch').addEventListener('change', () => reload());
  ['mType', 'lMe', 'lSquad', 'lOther', 'lHeat'].forEach((id) => $(`#${id}`).addEventListener('change', sync));
  $('#zIn').addEventListener('click', () => mv.zoomAt(1.5));
  $('#zOut').addEventListener('click', () => mv.zoomAt(1 / 1.5));
  await mv.ready;
  mv.fit();
  sync();
  await reload();
}

function renderMapStats(mv) {
  const list = mv.data.chests.filter((c) => c.me > 0 && (!mv.layers.type || c.type === mv.layers.type));
  const total = list.reduce((a, c) => a + c.me, 0);
  const top = list.reduce((a, c) => Math.max(a, c.me), 0);
  const again = list.filter((c) => c.me > 1).length;
  $('#mStats').innerHTML = `<h3>${t('openedByMe')}</h3>
    <div class="kv">
      <div>${t('distinct')}</div><div><b>${n(list.length)}</b></div>
      <div>${t('totalOpens')}</div><div><b>${n(total)}</b></div>
      <div>${t('reopened')}</div><div><b>${n(again)}</b></div>
      <div>${t('mostOften')}</div><div><b>${top ? `${n(top)} ${t('times', top)}` : '—'}</b></div>
    </div>`;
}

/** Complex numbers for the 2-point similarity transform world (X + iY) → image pixel (px + i·py). */
const C = {
  sub: (a, b) => [a[0] - b[0], a[1] - b[1]],
  add: (a, b) => [a[0] + b[0], a[1] + b[1]],
  mul: (a, b) => [a[0] * b[0] - a[1] * b[1], a[0] * b[1] + a[1] * b[0]],
  div: (a, b) => { const d = b[0] * b[0] + b[1] * b[1]; return [(a[0] * b[0] + a[1] * b[1]) / d, (a[1] * b[0] - a[0] * b[1]) / d]; },
};
const lerp = (a, b, k) => a.map((v, i) => Math.round(v + (b[i] - v) * k));

class MapView {
  constructor(canvas, hasMap, calib) {
    this.c = canvas;
    this.ctx = canvas.getContext('2d');
    this.data = { chests: [], others: [] };
    this.layers = { me: true, squad: true, other: true, heat: false, type: '' };
    this.zoom = 1; this.ox = 0; this.oy = 0;
    this.drawn = [];
    this.img = null;
    this.ready = new Promise((resolve) => {
      if (!hasMap) { resolve(); return; }
      const img = new Image();
      img.onload = () => { this.img = img; this.c.width = img.naturalWidth * 2; this.c.height = img.naturalHeight * 2; resolve(); };
      img.onerror = () => resolve();
      img.src = '/map.png';
    }).then(() => document.fonts.ready).then(() => {
      this.k = this.img ? 2 : 1; // the canvas is 2x the map image so markers stay sharp
      const k = calib && calib.w1 ? calib : { w1: { x: 0, y: 0 }, p1: { x: 600, y: 600 }, w2: { x: 100000, y: 0 }, p2: { x: 1150, y: 600 } };
      const w1 = [k.w1.x, k.w1.y], w2 = [k.w2.x, k.w2.y], p1 = [k.p1.x, k.p1.y], p2 = [k.p2.x, k.p2.y];
      this.a = C.div(C.sub(p2, p1), C.sub(w2, w1));
      this.b = C.sub(p1, C.mul(this.a, w1));
    });
    this.onResize = () => { this.fit(); this.draw(); };
    window.addEventListener('resize', this.onResize);
    this.bind();
  }

  dispose() { window.removeEventListener('resize', this.onResize); }

  /** Fit the whole map into the visible part of the window. */
  fit() {
    const col = this.c.parentElement.parentElement; // .mapcol
    const top = col.getBoundingClientRect().top + window.scrollY;
    const maxH = Math.max(420, window.innerHeight - Math.min(top, 140) - 24);
    const w = Math.min(col.clientWidth - 2, maxH * (this.c.width / this.c.height));
    this.c.style.width = `${Math.floor(w)}px`;
  }

  /** Canvas pixels per CSS pixel. */
  get ratio() { return this.c.width / (this.c.clientWidth || this.c.width); }

  toBase(x, y) { const p = C.add(C.mul(this.a, [x, y]), this.b); return { x: p[0] * this.k, y: p[1] * this.k }; }
  toScreen(x, y) { const p = this.toBase(x, y); return { x: p.x * this.zoom + this.ox, y: p.y * this.zoom + this.oy }; }

  clampView() {
    const W = this.c.width, H = this.c.height;
    this.zoom = Math.min(6, Math.max(1, this.zoom));
    this.ox = Math.min(0, Math.max(W - W * this.zoom, this.ox));
    this.oy = Math.min(0, Math.max(H - H * this.zoom, this.oy));
  }

  zoomAt(factor, cx = this.c.width / 2, cy = this.c.height / 2) {
    const z = Math.min(6, Math.max(1, this.zoom * factor));
    const f = z / this.zoom;
    this.ox = cx - (cx - this.ox) * f;
    this.oy = cy - (cy - this.oy) * f;
    this.zoom = z;
    this.clampView();
    this.draw();
  }

  bind() {
    const c = this.c;
    const pos = (e) => { const r = c.getBoundingClientRect(); return { x: ((e.clientX - r.left) * c.width) / r.width, y: ((e.clientY - r.top) * c.height) / r.height }; };
    c.addEventListener('wheel', (e) => { e.preventDefault(); const p = pos(e); this.zoomAt(e.deltaY < 0 ? 1.25 : 0.8, p.x, p.y); }, { passive: false });
    let drag = null;
    c.addEventListener('pointerdown', (e) => { drag = { ...pos(e), ox: this.ox, oy: this.oy, moved: false }; c.setPointerCapture(e.pointerId); });
    c.addEventListener('pointermove', (e) => {
      const p = pos(e);
      if (!drag) { this.hover(e, p); return; }
      const dx = p.x - drag.x, dy = p.y - drag.y;
      if (!drag.moved && Math.abs(dx) + Math.abs(dy) > 4 * this.ratio) { drag.moved = true; c.classList.add('drag'); hideTip(); }
      if (drag.moved) { this.ox = drag.ox + dx; this.oy = drag.oy + dy; this.clampView(); this.draw(); }
    });
    const end = () => { drag = null; c.classList.remove('drag'); };
    c.addEventListener('pointerup', end);
    c.addEventListener('pointercancel', end);
    c.addEventListener('pointerleave', () => { if (!drag) hideTip(); });
    c.addEventListener('dblclick', (e) => { const p = pos(e); this.zoomAt(2, p.x, p.y); });
  }

  hover(e, p) {
    let best = null, bestD = Infinity;
    const slack = 3 * this.ratio;
    for (const d of this.drawn) {
      const dist = Math.hypot(d.x - p.x, d.y - p.y);
      if (dist <= d.r + slack && dist < bestD) { best = d; bestD = dist; }
    }
    if (!best) { hideTip(); return; }
    const ch = best.chest;
    const all = !$('#mMatch')?.value;
    if (!ch) { showTip(e, `<b>${t('chest')}</b><br>${all ? t('nobodyOpened') : t('notInMatch')}`); return; }
    const lines = [`<b>${esc(typeOne(ch.type))}</b>`];
    if (ch.me) lines.push(`${t('iOpened', n(ch.me), t('times', ch.me))}${all && ch.matches !== ch.me ? ` (${t('inMatches', n(ch.matches))})` : ''}`);
    if (ch.squad) lines.push(t('squadOpened', n(ch.squad), t('times', ch.squad)));
    showTip(e, lines.join('<br>'));
  }

  draw() {
    const { ctx, c } = this;
    if (!this.a) return;
    const R = this.ratio;
    const L = this.layers;
    ctx.setTransform(1, 0, 0, 1, 0, 0);
    ctx.clearRect(0, 0, c.width, c.height);
    ctx.setTransform(this.zoom, 0, 0, this.zoom, this.ox, this.oy);
    if (this.img) { ctx.imageSmoothingQuality = 'high'; ctx.drawImage(this.img, 0, 0, c.width, c.height); } else { ctx.fillStyle = '#16233a'; ctx.fillRect(0, 0, c.width, c.height); }
    const chests = this.data.chests.filter((ch) => !L.type || ch.type === L.type);
    if (L.heat) {
      ctx.drawImage(this.heat(chests), 0, 0);
    }
    ctx.setTransform(1, 0, 0, 1, 0, 0);
    ctx.fillStyle = 'rgba(8, 11, 17, .22)'; // calm the map down so markers read better
    ctx.fillRect(0, 0, c.width, c.height);
    this.drawn = [];
    if (L.heat) return;

    const zf = 1 + 0.35 * Math.log2(this.zoom);
    const dot = (q, r, fill, stroke, lw) => {
      ctx.beginPath(); ctx.arc(q.x, q.y, r, 0, Math.PI * 2);
      if (fill) { ctx.fillStyle = fill; ctx.fill(); }
      if (stroke) { ctx.strokeStyle = stroke; ctx.lineWidth = lw; ctx.stroke(); }
    };
    if (L.other && !L.type) {
      for (const o of this.data.others) {
        const q = this.toScreen(o.x, o.y), r = 1.9 * R * zf;
        dot(q, r, 'rgba(205,212,224,.55)', 'rgba(0,0,0,.4)', 0.7 * R);
        this.drawn.push({ ...q, r, chest: null });
      }
    }
    if (L.squad) {
      for (const ch of chests) {
        if (ch.me > 0 || ch.squad === 0) continue;
        const q = this.toScreen(ch.x, ch.y), r = 3.6 * R * zf;
        dot(q, r, 'rgba(0,0,0,.35)', SQUAD, 1.8 * R);
        this.drawn.push({ ...q, r, chest: ch });
      }
    }
    if (L.me) {
      const mine = chests.filter((ch) => ch.me > 0).sort((a, b) => a.me - b.me); // the most looted on top
      ctx.textAlign = 'center';
      ctx.textBaseline = 'middle';
      const df = document.body.classList.contains('df');
      const family = df ? '"PCC Display", Arial, sans-serif' : 'Arial, sans-serif';
      for (const ch of mine) {
        const k = lootClass(ch.me);
        const q = this.toScreen(ch.x, ch.y), r = k.r * R * zf;
        dot(q, r, k.color, 'rgba(10,12,18,.85)', 1.3 * R);
        if (ch.me > 1) {
          ctx.fillStyle = k.ink;
          ctx.font = `${df ? '' : 'bold '}${Math.round(r * (ch.me >= 10 ? 1.05 : 1.3))}px ${family}`;
          ctx.fillText(String(ch.me), q.x, q.y + 0.5 * R);
        }
        this.drawn.push({ ...q, r, chest: ch });
      }
    }
  }

  /** Heat map in base (unzoomed) canvas coordinates, weighted by how often the chest was opened. */
  heat(chests) {
    const { c, layers: L } = this;
    const off = document.createElement('canvas');
    off.width = c.width; off.height = c.height;
    const o = off.getContext('2d');
    const r = 26 * (c.width / 1200);
    o.globalCompositeOperation = 'lighter';
    for (const ch of chests) {
      const w = (L.me ? ch.me : 0) + (L.squad ? ch.squad : 0);
      if (!w) continue;
      const q = this.toBase(ch.x, ch.y);
      const g = o.createRadialGradient(q.x, q.y, 0, q.x, q.y, r);
      g.addColorStop(0, `rgba(0,0,0,${Math.min(1, 0.12 * w)})`); g.addColorStop(1, 'rgba(0,0,0,0)');
      o.fillStyle = g; o.fillRect(q.x - r, q.y - r, 2 * r, 2 * r);
    }
    const data = o.getImageData(0, 0, off.width, off.height);
    const d = data.data;
    for (let i = 0; i < d.length; i += 4) {
      const a = d[i + 3] / 255;
      if (a <= 0) continue;
      const k = Math.min(1, a * 1.4);
      const col = k < 0.5 ? lerp([91, 159, 208], [240, 194, 74], k * 2) : lerp([240, 194, 74], [210, 70, 62], (k - 0.5) * 2);
      d[i] = col[0]; d[i + 1] = col[1]; d[i + 2] = col[2]; d[i + 3] = Math.min(220, 60 + a * 400);
    }
    o.putImageData(data, 0, 0);
    return off;
  }
}

// ---------------------------------------------------------------- updates and settings

const size = (bytes) => (bytes >= 1073741824
  ? `${new Intl.NumberFormat(t('locale'), { maximumFractionDigits: 1 }).format(bytes / 1073741824)} ${t('gb')}`
  : `${Math.round(bytes / 1048576)} ${t('mb')}`);

async function setLanguage(code) {
  const r = await post('/api/settings', { lang: code });
  LANG = r.lang === 'en' ? 'en' : 'ru';
  applyLang();
  route();
}

async function renderUpdates() {
  const draw = async () => {
    const s = await api('/api/status');
    const p = s.progress;
    view.innerHTML = `
      <h1>${t('updates')}</h1>
      ${warnBanner(s.recordingWarning)}
      <div class="panel settings">
        <h2>${t('settings')}</h2>
        <div class="setting"><span class="label">${t('language')}</span>
          <div class="seg" role="group">
            <button data-lang="ru" class="${LANG === 'ru' ? 'on' : ''}">Русский</button><button data-lang="en" class="${LANG === 'en' ? 'on' : ''}">English</button>
          </div>
        </div>
      </div>
      <div class="grid g2 section">
        <div class="panel">
          <h2>${t('program')}</h2>
          <div class="kv">
            <div>${t('version')}</div><div><b>v${esc(s.version)}</b></div>
            <div>${t('updatesKey')}</div><div>${esc(s.update.status)}</div>
            <div>${t('autostart')}</div><div>${s.autostart ? t('on') : t('off')}</div>
          </div>
          <div style="margin-top:16px"><button class="primary" id="upd">${icon('refresh')}<span>${t('checkUpdates')}</span></button></div>
        </div>
        <div class="panel">
          <h2>${t('replays')}</h2>
          <div class="kv">
            <div>${t('inFortnite')}</div><div>${s.demosFound ? n(s.replayFiles) : t('noFolder')}</div>
            <div>${t('copies')}</div><div>${n(s.backups)} · ${size(s.backupBytes)}</div>
            <div>${t('latest')}</div><div>${s.newestReplayUtc ? `${dt(s.newestReplayUtc)} · ${ago(s.newestReplayUtc)}` : '—'}</div>
            <div>Fortnite</div><div>${s.fortniteRunning ? t('fnRunning') : t('fnStopped')}</div>
            ${p.busy ? `<div>${t('countingKey')}</div><div>${t('of', n(p.done), n(p.total || p.pending))}</div>` : ''}
          </div>
          <div style="margin-top:16px"><button id="recount">${icon('refresh')}<span>${t('recount')}</span></button></div>
        </div>
      </div>
      <div class="panel section">
        <h2>${t('howTo')}</h2>
        <ol class="steps">${t('steps').map((x) => `<li>${x}</li>`).join('')}</ol>
      </div>
      <div class="fine">Portions of the materials used are trademarks and/or copyrighted works of Epic Games, Inc. All rights reserved by Epic.
        This material is not official and is not endorsed by Epic.</div>`;
    view.querySelectorAll('.seg button').forEach((b) => b.addEventListener('click', () => { if (b.dataset.lang !== LANG) setLanguage(b.dataset.lang).catch(() => {}); }));
    $('#recount').addEventListener('click', async (e) => { e.currentTarget.disabled = true; await post('/api/recount'); setTimeout(() => draw().catch(() => {}), 1500); });
    $('#upd').addEventListener('click', async (e) => { const b = e.currentTarget; b.disabled = true; b.querySelector('span').textContent = t('checking'); await post('/api/update/check'); draw().catch(() => {}); });
  };
  await draw();
  every(5000, () => draw().catch(() => {}));
}

// ---------------------------------------------------------------- start

/** The display font is the user's own file (not shipped); small text gets bigger only when it is present. */
function detectDisplayFont() {
  return document.fonts.load('20px "PCC Display"', 'Aa').then((faces) => {
    if (faces.length) document.body.classList.add('df');
  }).catch(() => {});
}

(async () => {
  try {
    LANG = (await api('/api/settings')).lang === 'en' ? 'en' : 'ru';
  } catch {
    LANG = 'ru';
  }
  applyLang();
  await detectDisplayFont();
  route();
})();
