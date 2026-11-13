'use strict';

// ---------------------------------------------------------------- language (English / Russian)

let LANG = 'en';
/** The same text in both languages: L('History', 'История'). */
const L = (en, ru) => (LANG === 'ru' ? ru : en);
/** Plural: pl(k, 'match', 'matches', 'матч', 'матча', 'матчей'). */
function pl(k, enOne, enMany, ruOne, ruFew, ruMany) {
  if (LANG !== 'ru') return k === 1 ? enOne : enMany;
  const m10 = k % 10, m100 = k % 100;
  if (m10 === 1 && m100 !== 11) return ruOne;
  if (m10 >= 2 && m10 <= 4 && (m100 < 12 || m100 > 14)) return ruFew;
  return ruMany;
}
const matchesW = (k) => pl(k, 'match', 'matches', 'матч', 'матча', 'матчей');
const timesW = (k) => pl(k, 'time', 'times', 'раз', 'раза', 'раз');
const locale = () => (LANG === 'ru' ? 'ru-RU' : 'en-GB');
const numLocale = () => (LANG === 'ru' ? 'ru-RU' : 'en-US');

const TABS = {
  '': ['Home', 'Главная'],
  history: ['History', 'История'],
  efficiency: ['Efficiency', 'Эффективность'],
  map: ['Map', 'Карта'],
  overlay: ['Overlay', 'Оверлей'],
  settings: ['Settings', 'Настройки'],
};
function applyLang() {
  document.documentElement.lang = LANG;
  document.title = L('Pirate Chest Counter', 'Счётчик сундуков');
  document.querySelectorAll('#tabs a').forEach((a) => { a.textContent = L(...TABS[a.dataset.tab]); });
  const t = { min: L('Minimize', 'Свернуть'), close: L('Close to tray', 'Закрыть в трей') };
  document.querySelectorAll('.wb').forEach((b) => { if (t[b.dataset.w]) b.title = t[b.dataset.w]; });
}

// ---------------------------------------------------------------- host window (WebView2)

const host = window.chrome?.webview;
if (!host) document.body.classList.add('nohost');
const send = (m) => { try { host?.postMessage(m); } catch { /* not in the app window */ } };

/** Title bar and empty side panel move the window (it has a fixed size, so no maximize). */
function dragArea(el, skip) {
  el.addEventListener('mousedown', (e) => {
    if (e.button !== 0 || (skip && e.target.closest(skip))) return;
    send('drag');
  });
}
dragArea(document.getElementById('titlebar'), '.wb');
dragArea(document.getElementById('side'), 'a');
document.querySelectorAll('.wb').forEach((b) => b.addEventListener('click', () => send(b.dataset.w)));

// ---------------------------------------------------------------- helpers

const $ = (sel, root = document) => root.querySelector(sel);
const esc = (s) => String(s ?? '').replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
const n = (v) => new Intl.NumberFormat(numLocale()).format(v ?? 0);
const n1 = (v) => (v == null ? '—' : new Intl.NumberFormat(numLocale(), { maximumFractionDigits: 2 }).format(v));
const icon = (name, cls = '') => `<svg class="ico ${cls}"><use href="#i-${name}"/></svg>`;
/** Fortnite icon from img/ (white silhouette, tinted with the text colour). */
const wi = (name, cls = '') => `<span class="wi ${cls}" style="--src:url(/img/${name}.webp)"></span>`;
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
const reduceMotion = () => window.matchMedia('(prefers-reduced-motion: reduce)').matches;

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
  return new Date(iso).toLocaleString(locale(), { day: '2-digit', month: 'short', ...(withYear ? { year: 'numeric' } : {}), hour: '2-digit', minute: '2-digit' });
}
function ago(iso) {
  if (!iso) return '';
  const s = (Date.now() - new Date(iso).getTime()) / 1000;
  if (s < 90) return L('just now', 'только что');
  if (s < 3600) return L(`${Math.round(s / 60)} min ago`, `${Math.round(s / 60)} мин назад`);
  if (s < 86400 * 2) return L(`${Math.round(s / 3600)} h ago`, `${Math.round(s / 3600)} ч назад`);
  return L(`${Math.round(s / 86400)} days ago`, `${Math.round(s / 86400)} дн. назад`);
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
  // Below-right of the pointer; flipped to the other side near the window's edge.
  const w = tip.offsetWidth, h = tip.offsetHeight;
  const x = e.clientX + 16 + w > window.innerWidth - 8 ? e.clientX - 16 - w : e.clientX + 16;
  const y = e.clientY + 16 + h > window.innerHeight - 8 ? e.clientY - 16 - h : e.clientY + 16;
  tip.style.left = `${Math.max(8, x)}px`;
  tip.style.top = `${Math.max(8, y)}px`;
}
function hideTip() { tip.style.display = 'none'; }

/**
 * Where the top of the digits sits inside a line box of height 1em (Burbank Big Condensed Black). The green "+N"
 * is smaller than the number it follows, so it is moved down by the difference to line up with the digits' top.
 */
function measureDigitTop() {
  try {
    const c = document.createElement('canvas').getContext('2d');
    c.font = '100px "Burbank Big"';
    const m = c.measureText('0123456789');
    const a = m.fontBoundingBoxAscent / 100, d = m.fontBoundingBoxDescent / 100, cap = m.actualBoundingBoxAscent / 100;
    if (Number.isFinite(a) && Number.isFinite(cap)) document.documentElement.style.setProperty('--digtop', String((1 - (a + d)) / 2 + a - cap));
  } catch { /* keep the CSS default */ }
}

// ---------------------------------------------------------------- odometer (rolls like a mechanical counter)

class Odo {
  constructor(el) {
    this.el = el;
    this.value = null;
    this.digits = 0;
    this.raf = 0;
  }

  build(digits) {
    this.digits = digits;
    const sep = LANG === 'ru' ? ' ' : ',';
    const parts = [];
    for (let pos = digits - 1; pos >= 0; pos--) {
      parts.push(`<span class="c" data-p="${pos}"><span class="strip">${[0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 0].map((d) => `<span>${d}</span>`).join('')}</span></span>`);
      if (pos > 0 && pos % 3 === 0) parts.push(`<span class="sep" data-s="${pos}">${sep}</span>`);
    }
    this.el.innerHTML = parts.join('');
    this.cols = [...this.el.querySelectorAll('.c')].map((c) => ({ pos: +c.dataset.p, el: c, strip: c.firstElementChild }));
    this.seps = [...this.el.querySelectorAll('.sep')].map((s) => ({ pos: +s.dataset.s, el: s }));
  }

  draw(v) {
    for (const c of this.cols) {
      const p = 10 ** c.pos;
      const whole = Math.floor(v / p + 1e-9);
      const d = whole % 10;
      const f = c.pos === 0 ? v - Math.floor(v) : Math.min(1, Math.max(0, v - whole * p - (p - 1)));
      const leading = c.pos > 0 && whole < 1;
      c.el.style.display = leading && f <= 0 ? 'none' : '';
      c.strip.style.transform = `translateY(${-(d + f)}em)`;
    }
    for (const s of this.seps) s.el.style.display = v + 1 > 10 ** s.pos ? '' : 'none';
  }

  /** Shows v; rolls from the current value (or from `from` the first time). */
  set(v, from = null, delay = 0) {
    const start = this.value ?? from;
    const digits = Math.max(1, String(Math.max(v, start ?? 0)).length);
    if (digits !== this.digits) this.build(digits);
    cancelAnimationFrame(this.raf);
    if (start == null || start === v || reduceMotion()) {
      this.value = v;
      this.draw(v);
      return;
    }
    this.draw(start);
    this.value = v;
    const t0 = performance.now() + delay;
    const len = 1400;
    const step = (now) => {
      const k = Math.min(1, Math.max(0, (now - t0) / len));
      const e = 1 - (1 - k) ** 3;
      this.draw(start + (v - start) * e);
      if (k < 1) this.raf = requestAnimationFrame(step);
    };
    this.raf = requestAnimationFrame(step);
  }
}

let odos = {};
function odo(key) {
  if (!odos[key] || !odos[key].el?.isConnected) odos[key] = new Odo(view.querySelector(`[data-odo="${key}"]`));
  return odos[key];
}
const odoHtml = (key) => `<span class="odo" data-odo="${key}"></span>`;
function setDelta(key, v) {
  const el = view.querySelector(`[data-delta="${key}"]`);
  if (!el) return;
  const txt = v > 0 ? `+${n(v)}` : '';
  if (el.textContent !== txt) el.textContent = txt;
}

// ---------------------------------------------------------------- sortable leaderboard tables

const sortState = {};
const tables = {};

/** cols: [{ label, value(row) for sorting, html(row) for the cell, num, cls }]; rowCls(row) adds a class to the row. */
function tableHtml(id, cols, rows, { empty = L('No data', 'Нет данных'), sort = null, rowCls = null } = {}) {
  if (!sortState[id] && sort) sortState[id] = sort;
  tables[id] = { cols, rows, empty, rowCls };
  return `<div class="boardwrap"><table class="board" id="${id}"><thead><tr>${headHtml(id)}</tr></thead><tbody>${bodyHtml(id)}</tbody></table></div>`;
}
function headHtml(id) {
  const { cols } = tables[id];
  const st = sortState[id];
  return cols.map((c, i) => `<th class="${c.num ? 'n ' : ''}sort ${st && st.i === i ? st.dir : ''}" data-i="${i}">${c.label}</th>`).join('');
}
function bodyHtml(id) {
  const { cols, rows, empty, rowCls } = tables[id];
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
  return list.map((r) => `<tr class="${rowCls ? rowCls(r) : ''}">${cols.map((c) => `<td class="${c.num ? 'n ' : ''}${c.cls ?? ''}">${c.html(r)}</td>`).join('')}</tr>`).join('');
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

/** Fortnite leaderboard place plate: #1 gold with the Victory Royale crown, #2 silver, #3 bronze. */
function rankBadge(r, crown = true) {
  if (r == null) return '<span class="muted">—</span>';
  const cls = r === 1 ? 'r1' : r === 2 ? 'r2' : r === 3 ? 'r3' : '';
  return `<span class="rank ${cls}"><span>${crown && r === 1 ? wi('crown') : ''}#${r}</span></span>`;
}
const topCls = (r) => (r <= 3 ? `top${r}` : '');

// ---------------------------------------------------------------- router with page transitions

const view = $('#view');
let timers = [];
let cleanups = [];
let current = null;
let firstRoute = true;
/** Increases on every navigation; a page that finished loading after the user moved on does not draw itself. */
let routeSeq = 0;

const routes = {
  '': renderHome,
  history: renderHistory,
  efficiency: renderEfficiency,
  map: renderMap,
  overlay: renderOverlay,
  settings: renderSettings,
  updates: renderSettings,
};

function every(ms, fn) { timers.push(setInterval(fn, ms)); }

/**
 * Calls fn the moment the program's matches change (a match was counted, a recount went on). One request stays open
 * and the program answers it when its revision number moves; it ends with the page it was started on.
 */
function onMatchesChanged(fn) {
  const seq = routeSeq;
  const ctl = new AbortController();
  cleanups.push(() => ctl.abort());
  (async () => {
    let rev = null;
    while (seq === routeSeq) {
      try {
        const r = await fetch(rev === null ? '/api/rev' : `/api/rev?since=${rev}`, { cache: 'no-store', signal: ctl.signal });
        if (!r.ok) throw new Error(`/api/rev: ${r.status}`);
        const next = (await r.json()).rev;
        if (seq !== routeSeq) return;
        if (rev !== null && next !== rev) fn();
        rev = next;
      } catch {
        if (ctl.signal.aborted) return;
        await sleep(1000); // the program is restarting (update): ask again
      }
    }
  })();
}

/** Wraps a page; children rise in one after another (only on the first draw of a page). */
function page(html, cls = '') {
  return `<div class="page ${cls}">${html}</div>`;
}
function stagger() {
  view.querySelectorAll('.page > *').forEach((el, i) => el.style.setProperty('--i', i));
}

async function route() {
  let name = location.hash.replace(/^#\/?/, '').split('?')[0];
  if (name === 'updates') name = 'settings';
  const fn = routes[name] ?? renderHome;
  const seq = ++routeSeq;
  document.querySelectorAll('#tabs a').forEach((a) => a.classList.toggle('active', a.dataset.tab === name));
  timers.forEach(clearInterval);
  timers = [];
  cleanups.forEach((f) => f());
  cleanups = [];
  if (current?.dispose) current.dispose();
  current = null;
  hideTip();
  if (!firstRoute && !reduceMotion()) {
    view.classList.add('leaving');
    await sleep(120);
  }
  firstRoute = false;
  if (seq !== routeSeq) return;
  odos = {};
  view.classList.toggle('fill', fn === renderMap); // the map takes the whole view, without page padding or scroll
  try {
    await fn();
    if (seq !== routeSeq) return;
    stagger();
  } catch (e) {
    view.innerHTML = page(`<div class="banner">${icon('warn')}<div>${L('Could not load data: ', 'Не удалось загрузить данные: ')}${esc(e.message)}</div></div>`);
  }
  view.scrollTop = 0;
  view.classList.remove('leaving');
}
window.addEventListener('hashchange', route);

function warnBanner(text) {
  return text ? `<div class="banner">${icon('warn')}<div><b>${esc(text)}</b><br>${L('How to turn on replay recording: see <a href="#/settings">Settings</a>.', 'Как включить запись повторов — в разделе <a href="#/settings">«Настройки»</a>.')}</div></div>` : '';
}
function progressBanner(p) {
  if (!p.busy) return '';
  const pct = p.total ? Math.round((100 * p.done) / p.total) : 5;
  return `<div class="banner info">${icon('refresh')}<div style="flex:1"><b>${L(`Counting matches: ${n(p.done)} of ${n(p.total || p.pending)}`, `Считаю матчи: ${n(p.done)} из ${n(p.total || p.pending)}`)}</b>
    <div class="progress"><div style="width:${pct}%"></div></div></div></div>`;
}

// ---------------------------------------------------------------- home

const TYPES = {
  Normal: { img: '/img/chest.png', name: () => L('Normal', 'Обычные'), one: () => L('Normal chest', 'Обычный сундук') },
  Rare: { img: '/img/chest-rare.png', name: () => L('Rare', 'Редкие'), one: () => L('Rare chest', 'Редкий сундук') },
  Sprite: { img: '/img/chest-sprite.png', name: () => L('Sprite', 'Спрайтовые'), one: () => L('Sprite chest', 'Спрайтовый сундук') },
};

/** Session clock state; the browser clock may differ from the PC clock the server uses. */
let session = null;

function sessionNote(s) {
  if (!s.active) return L('starts with your first match', 'начнётся с первым матчем');
  if (s.playing) return `<span class="live"></span>${L('match in progress', 'идёт матч')}`;
  return '';
}

function tickSession() {
  const el = $('#sessClock');
  if (!el || !session) return;
  const now = Date.now() + session.offset;
  el.textContent = session.active ? clock(now - new Date(session.startedUtc).getTime()) : '—';
  el.parentElement.classList.toggle('off', !session.active);
  $('#sessNote').innerHTML = sessionNote(session);
}

/** Seconds as m:ss (a match is shorter than an hour). */
const mmss = (sec) => {
  const t = Math.round(sec ?? 0);
  return `${Math.floor(t / 60)}:${String(t % 60).padStart(2, '0')}`;
};

const SEEN_KEY = 'pcc.seenMatch';
function seenMatch() { try { return localStorage.getItem(SEEN_KEY); } catch { return null; } }
function markSeen(id) { try { localStorage.setItem(SEEN_KEY, id); } catch { /* storage off */ } }

async function renderHome() {
  const seq = routeSeq;
  const [s0, st0] = await Promise.all([api('/api/summary'), api('/api/status')]);
  if (seq !== routeSeq) return;
  const typeTiles = s0.byType.map((x) => `
    <div class="tile typetile">
      <img class="pic" src="${TYPES[x.type]?.img ?? TYPES.Normal.img}" alt="">
      <div class="count">${odoHtml(`t${x.type}`)}<span class="delta" data-delta="t${x.type}"></span></div>
      <div class="name">${esc(TYPES[x.type]?.name() ?? x.type)}</div><div class="sub" data-sub="t${x.type}"></div>
    </div>`).join('');
  const mine = L('mine', 'мои'), squad = L('squad', 'отряд');
  view.innerHTML = page(`
    <div id="banners"></div>
    <div class="grid g2">
      <div class="tile hero">
        <img class="pic" src="/img/chest.png" alt="">
        <div class="value">${odoHtml('mine')}<span class="delta" data-delta="mine"></span></div>
        <div class="name">${L('My chests', 'Мои сундуки')}</div><div class="sub" id="heroSub"></div>
      </div>
      <div class="tile hero">
        ${wi('squads', 'pic')}
        <div class="value">${odoHtml('squad')}<span class="delta" data-delta="squad"></span></div>
        <div class="name">${L('Me + squad', 'Мои + отряд')}</div><div class="sub">${L('with teammates, at least', 'вместе с тиммейтами, не меньше')}</div>
      </div>
    </div>
    <div class="grid g3 section">
      <div class="panel"><h3>${L('Current session', 'Текущая сессия')}</h3>
        <div class="clock">${wi('timer')}<span id="sessClock">—</span></div>
        <div class="clocknote" id="sessNote"></div>
        <div class="stats small">
          <div class="stat"><div class="v gold">${odoHtml('sMine')}<span class="delta" data-delta="sMine"></span></div><div class="k">${mine}</div></div>
          <div class="stat"><div class="v sea">${odoHtml('sSquad')}<span class="delta" data-delta="sSquad"></span></div><div class="k">${squad}</div></div>
          <div class="stat"><div class="v">${odoHtml('sMatches')}</div><div class="k" id="sMatchesK"></div></div></div></div>
      <div class="panel"><h3>${L('Today', 'Сегодня')}</h3><div class="stats">
        <div class="stat"><div class="v gold">${odoHtml('dMine')}<span class="delta" data-delta="dMine"></span></div><div class="k">${mine}</div></div>
        <div class="stat"><div class="v sea">${odoHtml('dSquad')}<span class="delta" data-delta="dSquad"></span></div><div class="k">${squad}</div></div>
        <div class="stat"><div class="v">${odoHtml('dMatches')}</div><div class="k" id="dMatchesK"></div></div></div></div>
      <div class="panel"><h3>${L('Last match', 'Последний матч')}</h3><div id="lastMatch"></div></div>
    </div>
    <div class="section">
      <h2>${L('By chest type', 'По типам сундуков')}</h2>
      <div class="grid g3">${typeTiles}</div>
      <div class="ministats">
        <div class="ministat">${wi('flag')}<div><div class="v" id="best"></div><div class="k">${L('best match', 'лучший матч')}</div></div></div>
        <div class="ministat">${wi('chest-icon')}<div><div class="v" id="perMatch"></div><div class="k">${L('average per match', 'в среднем за матч')}</div></div></div>
        <div class="ministat" id="winBox">${wi('crown')}<div><div class="v" id="winRate"></div><div class="k" id="winK"></div></div></div>
        <div class="ministat">${wi('timer')}<div><div class="v" id="avgLen"></div><div class="k">${L('average match length', 'средняя длина игры')}</div></div></div>
      </div>
    </div>`, 'home');

  let firstDraw = true;
  const update = (s, st) => {
    session = { ...s.session, offset: new Date(s.session.serverNowUtc).getTime() - Date.now() };
    const ss = s.session;
    const lm = s.lastMatch;
    // "+N": what the last match added, while it belongs to the current session.
    const live = lm && lm.inSession;
    const today = live && lm.isToday;
    const d = {
      mine: live ? lm.mine : 0, squad: live ? lm.squad : 0,
      sMine: live ? lm.mine : 0, sSquad: live ? lm.squad : 0, sMatches: live ? 1 : 0,
      dMine: today ? lm.mine : 0, dSquad: today ? lm.squad : 0, dMatches: today ? 1 : 0,
    };
    for (const x of s.byType) d[`t${x.type}`] = live ? (lm.byType?.[x.type] ?? 0) : 0;
    const vals = {
      mine: s.mine, squad: s.squad,
      sMine: ss.mine, sSquad: ss.squad, sMatches: ss.matches,
      dMine: s.today.mine, dSquad: s.today.squad, dMatches: s.today.matches,
    };
    for (const x of s.byType) vals[`t${x.type}`] = x.mine;

    // Roll the counters from before the last match the first time the window sees it.
    const rollIn = firstDraw && live && seenMatch() !== lm.id;
    for (const [k, v] of Object.entries(vals)) {
      odo(k).set(v, rollIn ? v - d[k] : null, rollIn ? 450 : 0);
      setDelta(k, d[k]);
    }
    if (lm) markSeen(lm.id);
    firstDraw = false;

    $('#banners').innerHTML = warnBanner(st.recordingWarning) + progressBanner(s.progress);
    $('#heroSub').textContent = L(`in ${n(s.matches)} ${matchesW(s.matches)}`, `за ${n(s.matches)} ${matchesW(s.matches)}`);
    $('#sMatchesK').textContent = matchesW(ss.matches);
    $('#dMatchesK').textContent = matchesW(s.today.matches);
    for (const x of s.byType) view.querySelector(`[data-sub="t${x.type}"]`).textContent = L(`with squad: ${n(x.squad)}`, `с отрядом: ${n(x.squad)}`);
    $('#best').textContent = n(s.best);
    $('#perMatch').textContent = n1(s.perMatch);
    $('#winRate').textContent = s.placed ? `${n1(s.winRate)}%` : '—';
    $('#winK').textContent = L(`win rate · ${n(s.wins)} ${pl(s.wins, 'win', 'wins')}`, `винрейт · ${n(s.wins)} ${pl(s.wins, '', '', 'победа', 'победы', 'побед')}`);
    $('#winBox').title = L(`Victory Royale in ${n(s.wins)} of ${n(s.placed)} matches`, `Победа в ${n(s.wins)} из ${n(s.placed)} ${pl(s.placed, '', '', 'матча', 'матчей', 'матчей')}`);
    $('#avgLen').textContent = s.matches ? mmss(s.avgLengthSec) : '—';
    $('#lastMatch').innerHTML = lm
      ? `<div class="stat"><div class="v gold">+${n(lm.mine)} <small>${L(`mine · +${n(lm.squad)} with squad`, `мои · +${n(lm.squad)} с отрядом`)}</small></div><div class="d">${esc(lm.mode)} · ${dt(lm.endedUtc)}</div></div>`
      : `<div class="stat"><div class="v muted">—</div><div class="d">${L('no matches yet', 'матчей пока нет')}</div></div>`;
    tickSession();
  };
  update(s0, st0);
  const refresh = async () => {
    try {
      const [s, st] = await Promise.all([api('/api/summary'), api('/api/status')]);
      update(s, st);
    } catch { /* next time */ }
  };
  every(5000, refresh);
  onMatchesChanged(refresh); // a counted match shows up at once
  every(1000, tickSession);
}

// ---------------------------------------------------------------- charts (tiny, offline)

const ME = '#f6d24a', SQUAD = '#6cbcff';

function niceStep(max) {
  const raw = max / 5;
  const p = Math.pow(10, Math.floor(Math.log10(raw)));
  const m = raw / p;
  return (m <= 1 ? 1 : m <= 2 ? 2 : m <= 5 ? 5 : 10) * p;
}
const shortDate = (d) => new Date(`${d}T12:00:00`).toLocaleDateString(locale(), { day: 'numeric', month: 'short' });
const longDate = (d) => new Date(`${d}T12:00:00`).toLocaleDateString(locale(), { weekday: 'short', day: 'numeric', month: 'long' });

function axes(parts, W, R, Lp, top, step, y) {
  for (let v = 0; v <= top; v += step) {
    parts.push(`<g class="grid"><line x1="${Lp}" x2="${W - R}" y1="${y(v)}" y2="${y(v)}"/></g><g class="axis"><text x="${Lp - 10}" y="${y(v) + 7}" text-anchor="end">${n(v)}</text></g>`);
  }
}

function barChart(el, rows) {
  const W = 1000, H = 300, Lp = 70, R = 30, T = 16, B = 44;
  const max = Math.max(1, ...rows.map((r) => r.squad));
  const step = niceStep(max), top = Math.ceil(max / step) * step;
  const bw = (W - Lp - R) / Math.max(rows.length, 1);
  const y = (v) => T + (H - T - B) * (1 - v / top);
  const parts = [`<svg viewBox="0 0 ${W} ${H}" role="img">`];
  axes(parts, W, R, Lp, top, step, y);
  rows.forEach((r, i) => {
    const x = Lp + i * bw, w = Math.max(4, bw * 0.66);
    const bar = (x0, ww, v, fill) => `<rect x="${x0}" y="${y(v)}" width="${ww}" height="${Math.max(0, y(0) - y(v))}" rx="3" fill="${fill}" data-i="${i}"/>`;
    parts.push(bar(x + (bw - w) / 2, w, r.squad, SQUAD));
    parts.push(bar(x + (bw - w) / 2 + w * 0.2, w * 0.6, r.mine, ME));
    if (rows.length <= 40 || i % Math.ceil(rows.length / 30) === 0) parts.push(`<g class="axis"><text x="${x + bw / 2}" y="${H - 12}" text-anchor="middle">${esc(shortDate(r.date))}</text></g>`);
  });
  parts.push('</svg>');
  const mine = L('mine', 'мои'), withSq = L('with squad', 'с отрядом');
  el.innerHTML = `<div class="legend"><span><i style="background:${ME}"></i>${mine}</span><span><i style="background:${SQUAD}"></i>${withSq}</span></div>${parts.join('')}`;
  el.querySelectorAll('rect[data-i]').forEach((p) => {
    p.addEventListener('mousemove', (e) => {
      const r = rows[+p.dataset.i];
      showTip(e, `<b>${esc(longDate(r.date))}</b><br>${mine}: ${n(r.mine)}<br>${withSq}: ${n(r.squad)}<br>${L('matches', 'матчей')}: ${n(r.matches)}`);
    });
    p.addEventListener('mouseleave', hideTip);
  });
}

function lineChart(el, rows) {
  const series = [{ key: 'cumSquad', color: SQUAD, label: L('with squad', 'с отрядом') }, { key: 'cumMine', color: ME, label: L('mine', 'мои') }];
  const W = 1000, H = 300, Lp = 80, R = 64, T = 16, B = 44; // R: room for the last date label
  const max = Math.max(1, ...rows.flatMap((r) => series.map((s) => r[s.key])));
  const step = niceStep(max), top = Math.ceil(max / step) * step;
  const x = (i) => Lp + (W - Lp - R) * (rows.length <= 1 ? 0.5 : i / (rows.length - 1));
  const y = (v) => T + (H - T - B) * (1 - v / top);
  const parts = [`<svg viewBox="0 0 ${W} ${H}" role="img">`];
  axes(parts, W, R, Lp, top, step, y);
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
  const seq = routeSeq;
  const [daily, matches] = await Promise.all([api('/api/daily'), api('/api/matches')]);
  if (seq !== routeSeq) return;
  const counted = matches.filter((m) => m.status === 'counted');
  const skipped = matches.filter((m) => m.status !== 'counted');
  const min = L('min', 'мин');
  view.innerHTML = page(`
    <h1>${L('History', 'История')}</h1>
    <div class="grid g2">
      <div class="panel chart"><h2>${L('Chests per day', 'Сундуки по дням')}</h2><div id="c1"></div></div>
      <div class="panel chart"><h2>${L('Running total', 'Нарастающий итог')}</h2><div id="c2"></div></div>
    </div>
    <div class="section"><h2>${L('Matches', 'Матчи')} · ${n(counted.length)}</h2>
      ${tableHtml('tMatches', [
        { label: L('Date', 'Дата'), value: (m) => m.startedUtc, html: (m) => dt(m.startedUtc) },
        { label: L('Mode', 'Режим'), value: (m) => m.mode, html: (m) => esc(m.mode) },
        { label: L('Length', 'Длительность'), num: true, value: (m) => m.durationMin, html: (m) => `${n1(m.durationMin)} ${min}` },
        { label: L('Place', 'Место'), num: true, value: (m) => (m.placement == null ? null : -m.placement), html: (m) => rankBadge(m.placement) },
        { label: L('Mine', 'Мои'), num: true, cls: 'big gold', value: (m) => m.mine, html: (m) => n(m.mine) },
        { label: L('Squad', 'Отряд'), num: true, cls: 'big sea', value: (m) => m.squad, html: (m) => n(m.squad) },
      ], counted, { empty: L('No matches yet', 'Матчей пока нет'), sort: { i: 0, dir: 'desc' } })}
    </div>
    ${skipped.length ? `<div class="section"><h2>${L('Skipped', 'Пропущено')} · ${n(skipped.length)}</h2>
      ${tableHtml('tSkipped', [
        { label: L('Date', 'Дата'), value: (m) => m.startedUtc, html: (m) => dt(m.startedUtc, true) },
        { label: L('Mode', 'Режим'), value: (m) => m.playlist, html: (m) => esc(m.playlist || '—') },
        { label: L('Reason', 'Причина'), value: (m) => m.skipReason, cls: 'wrap', html: (m) => esc(m.skipReason) },
      ], skipped, { sort: { i: 0, dir: 'desc' } })}</div>` : ''}`);
  bindTables(view);
  if (daily.length) {
    barChart($('#c1'), daily);
    lineChart($('#c2'), daily);
  } else {
    $('#c1').innerHTML = $('#c2').innerHTML = `<div class="empty">${L('No data', 'Нет данных')}</div>`;
  }
}

// ---------------------------------------------------------------- efficiency

async function renderEfficiency() {
  const seq = routeSeq;
  const e = await api('/api/efficiency');
  if (seq !== routeSeq) return;
  const o = e.overall;
  const ranked = [...e.ranking].sort((a, b) => b.cpm - a.cpm || b.avgMine - a.avgMine).map((r, i) => ({ ...r, rank: i + 1 }));
  const min = L('min', 'мин');
  view.innerHTML = page(`
    <h1>${L('Farming efficiency', 'Эффективность фарма')}</h1>
    <div class="grid g3">
      <div class="tile stattile">${wi('timer', 'pic')}<div class="value">${n1(o.cpm)}</div><div class="name">${L('Per minute', 'В минуту')}</div><div class="sub">${L('chests per minute in game', 'сундуков за минуту игры')}</div></div>
      <div class="tile stattile">${wi('chest-icon', 'pic')}<div class="value">${n1(o.perMatch)}</div><div class="name">${L('Per match', 'За матч')}</div><div class="sub">${L('on average', 'в среднем')}</div></div>
      <div class="tile stattile">${wi('flag', 'pic')}<div class="value">${n(o.best)}</div><div class="name">${L('Record', 'Рекорд')}</div><div class="sub">${L('best match', 'лучший матч')}</div></div>
    </div>
    <div class="section"><h2>${L('Landing spots', 'Рейтинг мест высадки')}</h2>
      ${tableHtml('tRank', [
        { label: '#', num: true, value: (r) => -r.rank, html: (r) => rankBadge(r.rank) },
        { label: L('Spot', 'Место'), value: (r) => r.name, html: (r) => `<span class="placename">${r.isPoi ? icon('pin') : ''}${esc(r.name)}</span>` },
        { label: L('Matches', 'Матчей'), num: true, value: (r) => r.matches, html: (r) => n(r.matches) },
        { label: L('Per minute', 'В минуту'), num: true, cls: 'big gold', value: (r) => r.cpm, html: (r) => n1(r.cpm) },
        { label: L('Per match', 'За матч'), num: true, value: (r) => r.avgMine, html: (r) => n1(r.avgMine) },
        { label: L('Best', 'Лучший'), num: true, value: (r) => r.best, html: (r) => n(r.best) },
      ], ranked, { sort: { i: 0, dir: 'desc' }, rowCls: (r) => topCls(r.rank) })}
    </div>
    <div class="section"><h2>${L('By match', 'По матчам')}</h2>
      ${tableHtml('tEffMatches', [
        { label: L('Date', 'Дата'), value: (m) => m.startedUtc, html: (m) => dt(m.startedUtc) },
        { label: L('Mode', 'Режим'), value: (m) => m.mode, html: (m) => esc(m.mode) },
        { label: L('Landing', 'Высадка'), value: (m) => m.place, html: (m) => `<span class="placename">${esc(m.place)}</span>` },
        { label: L('In game', 'В игре'), num: true, value: (m) => m.minutes, html: (m) => `${n1(m.minutes)} ${min}` },
        { label: L('Mine', 'Мои'), num: true, cls: 'big gold', value: (m) => m.mine, html: (m) => n(m.mine) },
        { label: L('Per minute', 'В минуту'), num: true, value: (m) => m.cpm, html: (m) => n1(m.cpm) },
      ], [...e.matches], { sort: { i: 0, dir: 'desc' } })}
    </div>`);
  bindTables(view);
}

// ---------------------------------------------------------------- map (Leaflet over map tiles, see tools/assets/build_map_tiles.py)

/** How many times I opened a chest → marker colour and size (CSS px when the whole island is in view). */
const LOOT_CLASSES = [
  { min: 1, color: '#f4e39a', ink: '#2a2310', r: 4, label: () => L('once', '1 раз') },
  { min: 2, color: '#f0b43c', ink: '#2a2008', r: 6, label: () => '2–3' },
  { min: 4, color: '#e47834', ink: '#2a1306', r: 7, label: () => '4–6' },
  { min: 7, color: '#d2463e', ink: '#ffffff', r: 8.5, label: () => L('7 or more', '7 и больше') },
];
const lootClass = (k) => [...LOOT_CLASSES].reverse().find((c) => k >= c.min);
const lerp = (a, b, k) => a.map((v, i) => Math.round(v + (b[i] - v) * k));

/**
 * Layer switches. Out of the box only "My chests" is on; everything else is one click away and the choice is
 * remembered (WebView2 keeps localStorage between runs).
 */
const MAP_PREFS_KEY = 'pcc.map';
const MAP_DEFAULTS = {
  me: true, types: { Normal: true, Rare: true, Sprite: true },
  squad: false, others: false, heat: false,
  places: false, named: true, landmarks: false,
  style: 'official', open: { me: true, places: true }, panel: true,
};
function loadMapPrefs() {
  try {
    const s = JSON.parse(localStorage.getItem(MAP_PREFS_KEY) || 'null');
    if (s && typeof s === 'object') return { ...MAP_DEFAULTS, ...s, types: { ...MAP_DEFAULTS.types, ...s.types }, open: { ...MAP_DEFAULTS.open, ...s.open } };
  } catch { /* defaults */ }
  return structuredClone(MAP_DEFAULTS);
}
function saveMapPrefs(p) { try { localStorage.setItem(MAP_PREFS_KEY, JSON.stringify(p)); } catch { /* storage off */ } }

/** Tile set description (assets/maptiles/map.json), loaded once. */
let mapMeta = null;
/** Where the map was looking; coming back to the tab keeps the view while the app runs. */
let mapLastView = null;

/** Map frame: the official 2048 px map; zoom 3 shows one frame pixel per screen pixel. LatLng = [frame y, frame x]. */
const FRAME_CRS = window.L ? window.L.extend({}, window.L.CRS.Simple, { transformation: new window.L.Transformation(1 / 8, 0, 1 / 8, 0) }) : null;

/**
 * A chest marker drawn on the shared canvas: a circle, with the number of opens written inside. It grows a little
 * as you zoom in, so dense clusters come apart and the numbers stay readable.
 */
const ChestDot = window.L ? window.L.CircleMarker.extend({
  _project() {
    const m = this._map;
    this._radius = this.options.base * Math.min(2.1, Math.max(0.8, 1 + 0.32 * (m.getZoom() - (m._fitZoom ?? 2.5))));
    window.L.CircleMarker.prototype._project.call(this);
  },
  _updatePath() {
    window.L.CircleMarker.prototype._updatePath.call(this);
    const rd = this._renderer;
    const t = this.options.label;
    if (!t || !rd._drawing || this._radius < 4.5) return;
    const ctx = rd._ctx, p = this._point, r = this._radius;
    ctx.fillStyle = this.options.ink;
    ctx.font = `${Math.round(r * (t.length > 1 ? 1.2 : 1.45))}px "Burbank Big", Arial, sans-serif`;
    ctx.textAlign = 'center';
    ctx.textBaseline = 'middle';
    ctx.fillText(t, p.x, p.y + r * 0.08);
  },
}) : null;

async function renderMap() {
  const seq = routeSeq;
  if (!window.L) throw new Error(L('map library did not load', 'не загрузилась библиотека карты'));
  const [matches, meta] = await Promise.all([api('/api/matches'), mapMeta ?? api('/maptiles/map.json')]);
  if (seq !== routeSeq) return;
  mapMeta = meta;
  const counted = matches.filter((m) => m.status === 'counted');
  const P = loadMapPrefs();
  if (!meta.styles.some((s) => s.id === P.style)) P.style = meta.styles[0].id;

  const sw = (key, on, cls = '') => `<button class="switch ${cls}${on ? ' on' : ''}" data-l="${key}" role="switch" aria-checked="${on}"></button>`;
  const caret = (g) => `<button class="caret${P.open[g] ? ' open' : ''}" data-open="${g}" title="${L('Show more', 'Подробнее')}"><svg class="ico"><use href="#i-chev"/></svg></button>`;
  view.innerHTML = page(`
    <div class="mapshell${P.panel ? '' : ' collapsed'}" id="mapShell">
      <aside class="mappanel"><div class="mpscroll">
        <select id="mMatch"><option value="">${L('All matches', 'Все матчи')}</option>${counted.map((m) => `<option value="${esc(m.id)}">${dt(m.startedUtc)} · ${esc(m.mode)} · ${n(m.mine)}</option>`).join('')}</select>
        <div class="msearch">${icon('search')}<input id="mSearch" type="text" placeholder="${L('Search places…', 'Поиск мест…')}" autocomplete="off" spellcheck="false"><div class="mresults" id="mResults"></div></div>

        <div class="lgroup">
          <div class="lrow lhead">${sw('me', P.me)}<span class="lname">${L('My chests', 'Мои сундуки')}</span><span class="lcount" id="cMe"></span>${caret('me')}</div>
          <div class="lsub${P.open.me ? ' open' : ''}${P.me ? '' : ' dim'}" data-sub="me"><div>
            ${Object.entries(TYPES).map(([k, v]) => `<div class="lrow">${sw(`type:${k}`, P.types[k], 'sm')}<img class="lpic" src="${v.img}" alt=""><span class="lname">${esc(v.name())}</span><span class="lcount" data-count="${k}"></span></div>`).join('')}
            <div class="maplegend"><div class="lhint">${L('How many times I opened', 'Сколько раз я открыл')}</div>
              ${LOOT_CLASSES.map((c) => `<div><span class="sw"><span class="mk" style="width:${2 * c.r + 2}px;height:${2 * c.r + 2}px;background:${c.color}"></span></span>${c.label()}</div>`).join('')}
            </div>
          </div></div>
        </div>
        <div class="lgroup">
          <div class="lrow lhead">${sw('squad', P.squad)}<span class="lname">${L('Squad only', 'Только отряд')}</span><span class="lcount" id="cSquad"></span><span class="mk lmk sq"></span></div>
          <div class="lnote">${L('opened by teammates, not by me', 'открывали тиммейты, но не я')}</div>
        </div>
        <div class="lgroup">
          <div class="lrow lhead">${sw('others', P.others)}<span class="lname">${L('Other chests', 'Остальные сундуки')}</span><span class="lcount" id="cOthers"></span><span class="mk lmk ot"></span></div>
          <div class="lnote">${L('chest spots nobody in the squad has opened', 'места сундуков, которые никто из отряда не открывал')}</div>
        </div>
        <div class="lgroup">
          <div class="lrow lhead">${sw('heat', P.heat)}<span class="lname">${L('Heat map', 'Тепловая карта')}</span><span class="lheat"></span></div>
          <div class="lnote">${L('where I open the most; shown instead of the chest markers', 'где я открываю больше всего; вместо значков сундуков')}</div>
        </div>
        <div class="lgroup">
          <div class="lrow lhead">${sw('places', P.places)}<span class="lname">${L('Locations', 'Места')}</span>${caret('places')}</div>
          <div class="lsub${P.open.places ? ' open' : ''}${P.places ? '' : ' dim'}" data-sub="places"><div>
            <div class="lrow">${sw('named', P.named, 'sm')}<span class="lname">${L('Named locations', 'Названные места')}</span></div>
            <div class="lrow">${sw('landmarks', P.landmarks, 'sm')}<span class="lname">${L('Landmarks', 'Ориентиры')}</span></div>
          </div></div>
        </div>
        ${meta.styles.length > 1 ? `<div class="lgroup"><h3>${L('Map style', 'Вид карты')}</h3>
          <div class="seg" id="mStyle">${meta.styles.map((s) => `<button data-v="${esc(s.id)}" class="${s.id === P.style ? 'on' : ''}">${esc(L(s.name, s.ru))}</button>`).join('')}</div></div>` : ''}
        <div class="lgroup" id="mStats"></div>
      </div></aside>
      <div class="mapbox" id="mapBox">
        <div id="lmap"></div>
        <button class="mbtn mcollapse" id="mCollapse" title="${L('Hide or show the panel', 'Скрыть или показать панель')}"><svg class="ico"><use href="#i-chev"/></svg></button>
        <div class="mzoom">
          <button class="mbtn" id="zIn" title="${L('Zoom in', 'Приблизить')}"><svg class="ico"><use href="#i-plus"/></svg></button>
          <button class="mbtn" id="zOut" title="${L('Zoom out', 'Отдалить')}"><svg class="ico"><use href="#i-minus"/></svg></button>
          <button class="mbtn" id="zFit" title="${L('Whole island', 'Весь остров')}"><svg class="ico"><use href="#i-fit"/></svg></button>
        </div>
        <div class="mcredit">${L('Map: ', 'Карта: ')}${esc(meta.credit)}</div>
      </div>
    </div>`, 'mappage');

  const cm = new ChestMap($('#lmap'), meta, P);
  current = cm;
  const save = () => saveMapPrefs(P);

  // Switches: groups and sub-switches.
  view.querySelectorAll('.mappanel [data-l]').forEach((b) => b.addEventListener('click', () => {
    const k = b.dataset.l;
    let on;
    if (k.startsWith('type:')) on = P.types[k.slice(5)] = !P.types[k.slice(5)];
    else on = P[k] = !P[k];
    b.classList.toggle('on', on);
    b.setAttribute('aria-checked', String(on));
    const sub = view.querySelector(`[data-sub="${k}"]`);
    if (sub) sub.classList.toggle('dim', !on);
    if (on && sub && !P.open[k]) { P.open[k] = true; sub.classList.add('open'); view.querySelector(`[data-open="${k}"]`)?.classList.add('open'); }
    save();
    if (['places', 'named', 'landmarks'].includes(k)) cm.drawPlaces();
    else cm.drawChests();
    renderMapCounts(cm);
  }));
  view.querySelectorAll('.mappanel [data-open]').forEach((b) => b.addEventListener('click', () => {
    const g = b.dataset.open;
    P.open[g] = !P.open[g];
    b.classList.toggle('open', P.open[g]);
    view.querySelector(`[data-sub="${g}"]`).classList.toggle('open', P.open[g]);
    save();
  }));
  $('#mStyle')?.addEventListener('click', (e) => {
    const b = e.target.closest('button');
    if (!b || b.dataset.v === P.style) return;
    P.style = b.dataset.v;
    $('#mStyle').querySelectorAll('button').forEach((x) => x.classList.toggle('on', x === b));
    save();
    cm.setStyle();
  });
  $('#mCollapse').addEventListener('click', () => {
    P.panel = !P.panel;
    $('#mapShell').classList.toggle('collapsed', !P.panel);
    save();
  });
  $('#mapShell').addEventListener('transitionend', (e) => { if (e.target.classList.contains('mappanel')) cm.map.invalidateSize({ pan: false }); });
  $('#zIn').addEventListener('click', () => cm.map.zoomIn());
  $('#zOut').addEventListener('click', () => cm.map.zoomOut());
  $('#zFit').addEventListener('click', () => cm.fit(true));
  bindPlaceSearch(cm);

  const reload = async () => {
    const id = $('#mMatch').value;
    const data = await api(`/api/map/chests${id ? `?match=${encodeURIComponent(id)}` : ''}`);
    if (seq !== routeSeq) return;
    cm.data = data;
    cm.matchFilter = !!id;
    cm.drawChests();
    renderMapCounts(cm);
  };
  $('#mMatch').addEventListener('change', () => reload().catch(() => {}));
  cm.drawPlaces();
  renderMapCounts(cm);
  await reload();
}

/** Numbers next to the switches and the "Opened by me" box. */
function renderMapCounts(cm) {
  const P = cm.prefs;
  const chests = cm.data.chests;
  const mine = chests.filter((c) => c.me > 0 && P.types[c.type] !== false);
  const total = mine.reduce((a, c) => a + c.me, 0);
  const top = mine.reduce((a, c) => Math.max(a, c.me), 0);
  const again = mine.filter((c) => c.me > 1).length;
  $('#cMe').textContent = n(total);
  for (const k of Object.keys(TYPES)) {
    const el = view.querySelector(`[data-count="${k}"]`);
    if (el) el.textContent = n(chests.filter((c) => c.type === k).reduce((a, c) => a + c.me, 0));
  }
  $('#cSquad').textContent = n(chests.filter((c) => c.me === 0 && c.squad > 0 && P.types[c.type] !== false).length);
  $('#cOthers').textContent = n(cm.data.others.length);
  $('#mStats').innerHTML = `<h3>${L('Opened by me', 'Открыто мной')}</h3>
    <div class="kv">
      <div>${L('Different chests', 'Разных сундуков')}</div><div class="num">${n(mine.length)}</div>
      <div>${L('Total opens', 'Всего открытий')}</div><div class="num">${n(total)}</div>
      <div>${L('Opened again', 'Открывал повторно')}</div><div class="num">${n(again)}</div>
      <div>${L('Most often', 'Чаще всего')}</div><div class="num">${top ? `${n(top)} ${timesW(top)}` : '—'}</div>
    </div>
    <div class="maphint">${L('Scroll or double-click to zoom, drag to move. Hover a chest for details.', 'Колёсико или двойной щелчок — приблизить, перетаскивание — двигать карту. Наведите на сундук — подробности.')}</div>`;
}

/** Search box: finds places in both languages and flies there. */
function bindPlaceSearch(cm) {
  const input = $('#mSearch'), box = $('#mResults');
  let hits = [], sel = 0;
  const norm = (s) => s.toLocaleLowerCase().replace(/ё/g, 'е');
  const draw = () => {
    box.innerHTML = hits.map((p, i) => `<button class="${i === sel ? 'sel' : ''}" data-i="${i}"><span>${esc(L(p.name, p.ru))}</span><small>${p.kind === 'named' ? L('location', 'место') : L('landmark', 'ориентир')}</small></button>`).join('');
    box.classList.toggle('open', hits.length > 0);
  };
  const go = (p) => { input.value = ''; hits = []; draw(); input.blur(); cm.flyToPlace(p); };
  input.addEventListener('input', () => {
    const q = norm(input.value.trim());
    hits = q ? cm.meta.places.filter((p) => norm(p.name).includes(q) || norm(p.ru).includes(q)).slice(0, 8) : [];
    sel = 0;
    draw();
  });
  input.addEventListener('keydown', (e) => {
    if (e.key === 'ArrowDown' || e.key === 'ArrowUp') { e.preventDefault(); if (hits.length) { sel = (sel + (e.key === 'ArrowDown' ? 1 : hits.length - 1)) % hits.length; draw(); } }
    else if (e.key === 'Enter' && hits[sel]) go(hits[sel]);
    else if (e.key === 'Escape') { input.value = ''; hits = []; draw(); input.blur(); }
  });
  input.addEventListener('blur', () => setTimeout(() => box.classList.remove('open'), 150));
  input.addEventListener('focus', () => box.classList.toggle('open', hits.length > 0));
  box.addEventListener('mousedown', (e) => { const b = e.target.closest('button'); if (b) { e.preventDefault(); go(hits[+b.dataset.i]); } });
}

class ChestMap {
  constructor(el, meta, prefs) {
    const Lf = window.L;
    this.meta = meta;
    this.prefs = prefs;
    this.data = { chests: [], others: [] };
    this.matchFilter = false;
    const cal = meta.calibration;
    /** World (Unreal units, X → east, Y → south) → LatLng in the frame. */
    this.ll = (x, y) => Lf.latLng(cal.scale * y + cal.y0, cal.scale * x + cal.x0);
    this.fitBounds = Lf.latLngBounds(meta.fit);
    const frame = Lf.latLngBounds([[0, 0], [meta.frame, meta.frame]]);

    this.renderer = Lf.canvas({ padding: 0.6, tolerance: 4 });
    const map = this.map = Lf.map(el, {
      crs: FRAME_CRS, renderer: this.renderer, zoomControl: false, attributionControl: false,
      minZoom: 0, maxZoom: 6, zoomSnap: 0.25, zoomDelta: 0.5, wheelPxPerZoomLevel: 90, wheelDebounceTime: 25,
      maxBounds: frame.pad(0.05), maxBoundsViscosity: 0.85, bounceAtZoomLimits: false, inertiaDeceleration: 2600,
    });
    map.createPane('heat').style.zIndex = 300;
    map.createPane('labels').style.zIndex = 350;
    map.getPane('labels').style.pointerEvents = 'none';
    map.getPane('heat').style.pointerEvents = 'none';

    this.groups = { others: Lf.featureGroup(), squad: Lf.featureGroup(), me: Lf.featureGroup() };
    for (const g of Object.values(this.groups)) {
      g.on('mouseover mousemove', (e) => this.tip(e));
      g.on('mouseout', hideTip);
      g.on('click', (e) => { if (map.getZoom() < 4) map.flyTo(e.latlng, Math.min(map.getMaxZoom(), map.getZoom() + 1.5), { duration: 0.5 }); });
    }
    this.places = Lf.layerGroup();
    this.heat = null;
    this.tiles = null;
    this.setStyle();

    map._fitZoom = map.getBoundsZoom(this.fitBounds, false, Lf.point(24, 24));
    map.setMinZoom(Math.max(0, Math.floor((map._fitZoom - 0.75) * 4) / 4));
    if (mapLastView) map.setView(mapLastView.center, mapLastView.zoom, { animate: false });
    else this.fit(false);
    map.on('moveend', () => { mapLastView = { center: map.getCenter(), zoom: map.getZoom() }; });
    map.on('movestart zoomstart', hideTip);
    this.onResize = () => {
      const z = map.getBoundsZoom(this.fitBounds, false, Lf.point(24, 24));
      if (Math.abs(z - map._fitZoom) > 0.01) { map._fitZoom = z; map.setMinZoom(Math.max(0, Math.floor((z - 0.75) * 4) / 4)); }
    };
    map.on('resize', this.onResize);
  }

  dispose() {
    hideTip();
    this.map.remove();
  }

  fit(animate) {
    this.map.fitBounds(this.fitBounds, { padding: [12, 12], animate });
  }

  /**
   * Map picture: a tile layer at the picture's own resolution over all of it, plus (from zoom 4) sharpened tiles over
   * the island only, so zooming in stays crisp while the open sea costs nothing.
   */
  setStyle() {
    const Lf = window.L, map = this.map;
    const st = this.meta.styles.find((s) => s.id === this.prefs.style) ?? this.meta.styles[0];
    const url = `/maptiles/${st.id}/{z}/{x}_{y}.webp?v=${this.meta.version}`;
    const opt = { tileSize: this.meta.tileSize, maxZoom: 6, noWrap: true, keepBuffer: 3, className: 'maptile' };
    const layers = [Lf.tileLayer(url, { ...opt, minZoom: 0, maxNativeZoom: st.maxNativeZoom, bounds: Lf.latLngBounds(st.bounds) })];
    if (st.detail) {
      layers.push(Lf.tileLayer(url, { ...opt, minZoom: st.detail.minZoom, minNativeZoom: st.detail.minZoom, maxNativeZoom: st.detail.maxNativeZoom, bounds: Lf.latLngBounds(st.detail.bounds) }));
    }
    const old = this.tiles ?? [];
    this.tiles = layers;
    layers.forEach((t) => t.addTo(map));
    map.getContainer().style.background = st.bg;
    if (old.length) {
      // The new picture fades in over the old one; then the old one goes.
      const drop = () => old.forEach((t) => map.hasLayer(t) && map.removeLayer(t));
      layers[0].once('load', () => setTimeout(drop, 250));
      setTimeout(drop, 3000);
    }
  }

  /**
   * Rebuilds the chest markers (draw order: others, squad, mine with the most opened on top), or the heat map, which
   * takes their place while it is on.
   */
  drawChests() {
    const P = this.prefs, map = this.map;
    for (const g of Object.values(this.groups)) { g.clearLayers(); map.removeLayer(g); }
    const chests = this.data.chests.filter((c) => P.types[c.type] !== false);
    this.drawHeat(chests);
    if (P.heat) return;
    if (P.others) {
      for (const o of this.data.others) {
        this.groups.others.addLayer(new ChestDot(this.ll(o.x, o.y), { base: 2.2, fillColor: '#e3e8f2', fillOpacity: 0.75, color: '#0a0f1c', opacity: 0.55, weight: 1, chest: null }));
      }
      this.groups.others.addTo(map);
    }
    if (P.squad) {
      for (const c of chests) {
        if (c.me > 0 || c.squad === 0) continue;
        this.groups.squad.addLayer(new ChestDot(this.ll(c.x, c.y), { base: 3.8, fillColor: '#06101f', fillOpacity: 0.45, color: SQUAD, opacity: 1, weight: 2, chest: c }));
      }
      this.groups.squad.addTo(map);
    }
    if (P.me) {
      for (const c of chests.filter((x) => x.me > 0).sort((a, b) => a.me - b.me)) {
        const k = lootClass(c.me);
        this.groups.me.addLayer(new ChestDot(this.ll(c.x, c.y), {
          base: k.r, fillColor: k.color, fillOpacity: 1, color: '#0a0c12', opacity: 0.9, weight: 1.4,
          label: c.me > 1 ? String(c.me) : '', ink: k.ink, chest: c,
        }));
      }
      this.groups.me.addTo(map);
    }
  }

  /** Heat map over the island, weighted by how often a chest was opened (mine, plus the squad's when that is on). */
  drawHeat(chests) {
    const Lf = window.L, P = this.prefs;
    if (this.heat) { this.map.removeLayer(this.heat); this.heat = null; }
    if (!P.heat) return;
    // Over the island only, one canvas pixel per frame pixel. Latitude is the frame's y, growing downwards,
    // so the island's top edge is its "south".
    const b = this.fitBounds, x0 = b.getWest(), top = b.getSouth();
    const w = Math.ceil(b.getEast() - x0), h = Math.ceil(b.getNorth() - top);
    const off = document.createElement('canvas');
    off.width = w; off.height = h;
    const o = off.getContext('2d');
    const r = 30;
    o.globalCompositeOperation = 'lighter';
    for (const c of chests) {
      const wgt = c.me + (P.squad ? c.squad : 0);
      if (!wgt) continue;
      const q = this.ll(c.x, c.y);
      const px = q.lng - x0, py = q.lat - top;
      const g = o.createRadialGradient(px, py, 0, px, py, r);
      g.addColorStop(0, `rgba(0,0,0,${Math.min(1, 0.12 * wgt)})`);
      g.addColorStop(1, 'rgba(0,0,0,0)');
      o.fillStyle = g;
      o.fillRect(px - r, py - r, 2 * r, 2 * r);
    }
    const img = o.getImageData(0, 0, w, h);
    const d = img.data;
    for (let i = 0; i < d.length; i += 4) {
      const a = d[i + 3] / 255;
      if (a <= 0) continue;
      const k = Math.min(1, a * 1.4);
      const col = k < 0.5 ? lerp([108, 188, 255], [246, 210, 74], k * 2) : lerp([246, 210, 74], [210, 70, 62], (k - 0.5) * 2);
      d[i] = col[0]; d[i + 1] = col[1]; d[i + 2] = col[2]; d[i + 3] = Math.min(220, 60 + a * 400);
    }
    o.putImageData(img, 0, 0);
    this.heat = Lf.imageOverlay(off.toDataURL(), Lf.latLngBounds([[top, x0], [top + h, x0 + w]]), { pane: 'heat', interactive: false, className: 'heatlayer' }).addTo(this.map);
  }

  /** Place names on the map, like the game's map labels. */
  drawPlaces() {
    const Lf = window.L, P = this.prefs;
    this.places.clearLayers();
    this.map.removeLayer(this.places);
    if (!P.places) return;
    for (const p of this.meta.places) {
      if (p.kind === 'named' ? !P.named : !P.landmarks) continue;
      this.places.addLayer(Lf.marker(this.ll(p.x, p.y), {
        pane: 'labels', interactive: false, keyboard: false,
        icon: Lf.divIcon({ className: `place ${p.kind}`, html: `<span>${esc(L(p.name, p.ru))}</span>`, iconSize: [0, 0] }),
      }));
    }
    this.places.addTo(this.map);
  }

  flyToPlace(p) {
    const Lf = window.L, at = this.ll(p.x, p.y);
    this.map.flyTo(at, Math.max(this.map.getZoom(), 4.25), { duration: 0.8 });
    const ping = Lf.marker(at, { interactive: false, keyboard: false, icon: Lf.divIcon({ className: 'mping', html: `<i></i><span>${esc(L(p.name, p.ru))}</span>`, iconSize: [0, 0] }) }).addTo(this.map);
    setTimeout(() => this.map.removeLayer(ping), 3200);
  }

  tip(e) {
    const ch = (e.propagatedFrom ?? e.layer ?? e.sourceTarget)?.options?.chest;
    const ev = e.originalEvent;
    if (!ev) return;
    if (!ch) {
      showTip(ev, `<b>${L('Chest', 'Сундук')}</b><br>${this.matchFilter ? L('not opened in this match', 'в этом матче не открывали') : L('nobody in the squad has opened it yet', 'ещё никто из отряда не открывал')}`);
      return;
    }
    const lines = [`<b>${esc(TYPES[ch.type]?.one() ?? L('Chest', 'Сундук'))}</b>`];
    if (ch.me) {
      const inM = !this.matchFilter && ch.matches !== ch.me ? ` (${L('in', 'в')} ${n(ch.matches)} ${pl(ch.matches, 'match', 'matches', 'матче', 'матчах', 'матчах')})` : '';
      lines.push(`${L(`I opened it ${n(ch.me)} ${timesW(ch.me)}`, `я открыл: ${n(ch.me)} ${timesW(ch.me)}`)}${inM}`);
    }
    if (ch.squad) lines.push(`${L('squad', 'отряд')}: ${n(ch.squad)} ${timesW(ch.squad)}`);
    showTip(ev, lines.join('<br>'));
  }
}

// ---------------------------------------------------------------- overlay settings

const SWATCHES = ['#ffffff', '#f6d24a', '#5fe08a', '#6cbcff'];

async function renderOverlay() {
  const seq = routeSeq;
  let o = await api('/api/overlay');
  if (seq !== routeSeq) return;
  const st = o.settings;
  const seg = (key, opts) => `<div class="seg" data-seg="${key}">${opts.map(([v, l]) => `<button data-v="${v}" class="${st[key] === v ? 'on' : ''}">${l}</button>`).join('')}</div>`;
  const sw = (key) => `<button class="switch ${st[key] ? 'on' : ''}" data-sw="${key}" aria-label="${key}"></button>`;
  view.innerHTML = page(`
    <h1>${L('Overlay', 'Оверлей')}</h1>
    <div class="ovl">
      <div class="panel"><div class="rows">
        <div class="row"><div class="label">${L('Overlay', 'Оверлей')}</div><div class="ctrl">${sw('enabled')}<span class="muted" id="ovState"></span></div></div>
        <div class="row"><div class="label">${L('Number', 'Число')}</div><div class="ctrl">${seg('show', [['total', L('Total', 'Всего')], ['session', L('Session', 'Сессия')], ['today', L('Today', 'Сегодня')]])}</div></div>
        <div class="row"><div class="label">${L('Position', 'Положение')}</div><div class="ctrl"><button id="ovMove"></button><button id="ovReset">${L('Reset', 'Сбросить')}</button></div></div>
        <div class="row"><div class="label">${L('Size', 'Размер')}</div><div class="ctrl"><input type="range" min="50" max="300" step="5" id="ovScale" value="${st.scale}" style="flex:1"><span class="val" id="ovScaleV"></span></div></div>
        <div class="row"><div class="label">${L('Number colour', 'Цвет цифр')}</div><div class="ctrl"><div class="swatches">${SWATCHES.map((c) => `<button class="swatch" data-c="${c}" style="background:${c}"></button>`).join('')}<input type="color" id="ovColor" value="${st.color}" title="${L('Custom colour', 'Свой цвет')}"></div></div></div>
        <div class="row"><div class="label">${L('Background', 'Подложка')}</div><div class="ctrl">${seg('background', [['none', L('None', 'Нет')], ['dark', L('Dark', 'Тёмная')], ['glass', L('Glass', 'Стекло')]])}</div></div>
        <div class="row"><div class="label">${L('Background opacity', 'Прозрачность подложки')}</div><div class="ctrl"><input type="range" min="0" max="100" step="5" id="ovOpacity" value="${st.opacity}" style="flex:1"><span class="val" id="ovOpacityV"></span></div></div>
        <div class="row"><div class="label">${L('Chest icon', 'Иконка сундука')}</div><div class="ctrl">${sw('icon')}</div></div>
        <div class="row"><div class="label">${L('Label', 'Подпись')}</div><div class="ctrl">${sw('label')}</div></div>
        <div class="row"><div class="label">${L('Show', 'Показывать')}</div><div class="ctrl">${seg('when', [['fortnite', L('Only over Fortnite', 'Только поверх Fortnite')], ['always', L('Always', 'Всегда')]])}</div></div>
      </div></div>
      <div>
        <div class="preview" style="background-image:url(/map.png)"><div class="ov" id="ovPreview"></div></div>
        <div class="hint">${L(
          'The overlay is a separate window on top of the game: it does not touch Fortnite. With "Only over Fortnite" it shows only while the Fortnite window is the active one and hides when you switch away. It is visible over the game in <b>Windowed Fullscreen</b> or <b>Windowed</b> display mode. The number updates about a second after you leave a match for the lobby; (+N) is what the last match added. Clicks go through it; use Move to drag it into place.',
          'Оверлей — отдельное окно поверх игры, в Fortnite он не вмешивается. В режиме «Только поверх Fortnite» он виден, только пока окно Fortnite активно, и прячется, когда вы переключаетесь. Поверх игры он виден в режимах экрана <b>«Оконный полноэкранный»</b> и <b>«Оконный»</b>. Число обновляется примерно через секунду после выхода из матча в лобби; (+N) — сколько принёс последний матч. Клики проходят сквозь оверлей; чтобы поставить его на место, нажмите «Переместить».')}</div>
      </div>
    </div>`);

  let saveT = 0;
  const save = () => {
    clearTimeout(saveT);
    saveT = setTimeout(async () => {
      try {
        await post('/api/overlay', st);
        o = await api('/api/overlay');
        draw();
      } catch { /* keep the form */ }
    }, 120);
  };
  const draw = () => {
    view.querySelectorAll('[data-seg]').forEach((g) => g.querySelectorAll('button').forEach((b) => b.classList.toggle('on', st[g.dataset.seg] === b.dataset.v)));
    view.querySelectorAll('[data-sw]').forEach((b) => b.classList.toggle('on', !!st[b.dataset.sw]));
    view.querySelectorAll('.swatch').forEach((b) => b.classList.toggle('on', b.dataset.c.toLowerCase() === st.color.toLowerCase()));
    $('#ovScaleV').textContent = `${st.scale}%`;
    $('#ovOpacityV').textContent = `${st.opacity}%`;
    $('#ovMove').textContent = o.moving ? L('Done', 'Готово') : L('Move', 'Переместить');
    $('#ovMove').classList.toggle('primary', o.moving);
    $('#ovState').textContent = !st.enabled ? L('off', 'выключен')
      : st.when === 'fortnite' ? L('on, shows while the Fortnite window is active', 'включён, виден, пока окно Fortnite активно') : L('on', 'включён');
    const label = st.show === 'session' ? L('SESSION', 'СЕССИЯ') : st.show === 'today' ? L('TODAY', 'СЕГОДНЯ') : L('MY CHESTS', 'МОИ СУНДУКИ');
    const box = `<div class="ovbox ${st.background}${st.opacity < 15 ? ' bare' : ''}" style="--op:${st.opacity / 100}">
      ${st.icon ? '<img src="/img/chest.png" alt="">' : ''}
      <div>${st.label ? `<div class="ol">${label}</div>` : ''}<div class="on" style="color:${esc(st.color)}">${n(o.value)}${o.delta > 0 ? ` <span class="od">(+${n(o.delta)})</span>` : ''}</div></div></div>`;
    const pv = $('#ovPreview');
    pv.innerHTML = box;
    pv.style.transform = `scale(${st.scale / 100})`;
    pv.style.opacity = st.enabled ? 1 : 0.45;
  };

  view.querySelectorAll('[data-seg]').forEach((g) => g.addEventListener('click', (e) => {
    const b = e.target.closest('button');
    if (!b) return;
    st[g.dataset.seg] = b.dataset.v;
    draw();
    save();
  }));
  view.querySelectorAll('[data-sw]').forEach((b) => b.addEventListener('click', () => { st[b.dataset.sw] = !st[b.dataset.sw]; draw(); save(); }));
  view.querySelectorAll('.swatch').forEach((b) => b.addEventListener('click', () => { st.color = b.dataset.c; $('#ovColor').value = st.color; draw(); save(); }));
  $('#ovColor').addEventListener('input', (e) => { st.color = e.target.value; draw(); save(); });
  $('#ovScale').addEventListener('input', (e) => { st.scale = +e.target.value; draw(); save(); });
  $('#ovOpacity').addEventListener('input', (e) => { st.opacity = +e.target.value; draw(); save(); });
  $('#ovMove').addEventListener('click', async () => {
    await post('/api/overlay/move', { on: !o.moving });
    o = await api('/api/overlay');
    draw();
  });
  $('#ovReset').addEventListener('click', async () => { await post('/api/overlay/reset-position'); });
  draw();
  every(2000, async () => {
    try {
      const fresh = await api('/api/overlay');
      o = { ...fresh, settings: st };
      draw();
    } catch { /* next time */ }
  });
  // Leaving the page ends move mode, so the overlay never stays grabbable over the game.
  current = { dispose: () => { if (o.moving) post('/api/overlay/move', { on: false }).catch(() => {}); } };
}

// ---------------------------------------------------------------- settings

const size = (bytes) => (bytes >= 1073741824
  ? `${new Intl.NumberFormat(numLocale(), { maximumFractionDigits: 1 }).format(bytes / 1073741824)} ${L('GB', 'ГБ')}`
  : `${Math.round(bytes / 1048576)} ${L('MB', 'МБ')}`);

async function setLanguage(code) {
  const r = await post('/api/settings', { lang: code });
  LANG = r.lang === 'ru' ? 'ru' : 'en';
  applyLang();
  measureDigitTop();
  route();
}

async function renderSettings() {
  const seq = routeSeq;
  const draw = async (settled) => {
    const s = await api('/api/status');
    if (seq !== routeSeq) return;
    const p = s.progress;
    view.innerHTML = page(`
      <h1>${L('Settings', 'Настройки')}</h1>
      ${warnBanner(s.recordingWarning)}
      <div class="panel">
        <div class="row" style="border:0;padding:0"><div class="label">${L('Language', 'Язык')}</div><div class="ctrl">
          <div class="seg" id="langSeg"><button data-lang="en" class="${LANG === 'en' ? 'on' : ''}">English</button><button data-lang="ru" class="${LANG === 'ru' ? 'on' : ''}">Русский</button></div>
        </div></div>
      </div>
      <div class="grid g2 section">
        <div class="panel">
          <h2>${L('Program', 'Программа')}</h2>
          <div class="kv">
            <div>${L('Version', 'Версия')}</div><div class="num">v${esc(s.version)}</div>
            <div>${L('Updates', 'Обновления')}</div><div>${esc(s.update.status)}</div>
            <div>${L('Start with Windows', 'Запуск с Windows')}</div><div>${s.autostart ? L('on', 'включён') : L('off', 'выключен')}</div>
          </div>
          <div style="margin-top:18px"><button class="primary" id="upd">${icon('refresh')}<span>${L('Check for updates', 'Проверить обновления')}</span></button></div>
        </div>
        <div class="panel">
          <h2>${L('Replays', 'Реплеи')}</h2>
          <div class="kv">
            <div>${L('In the Fortnite folder', 'В папке Fortnite')}</div><div>${s.demosFound ? n(s.replayFiles) : L('no folder', 'папки нет')}</div>
            <div>${L('Copies saved', 'Сохранено копий')}</div><div>${n(s.backups)} · ${size(s.backupBytes)}</div>
            <div>${L('Latest', 'Последний')}</div><div>${s.newestReplayUtc ? `${dt(s.newestReplayUtc)} · ${ago(s.newestReplayUtc)}` : '—'}</div>
            <div>Fortnite</div><div>${s.fortniteRunning ? L('running', 'запущен') : L('not running', 'не запущен')}</div>
            ${p.busy ? `<div>${L('Counting', 'Считаю')}</div><div>${L(`${n(p.done)} of ${n(p.total || p.pending)}`, `${n(p.done)} из ${n(p.total || p.pending)}`)}</div>` : ''}
          </div>
          <div style="margin-top:18px"><button id="recount">${icon('refresh')}<span>${L('Recount everything', 'Пересчитать всё')}</span></button></div>
        </div>
      </div>
      <div class="panel section">
        <h2>${L('How to turn on replay recording', 'Как включить запись повторов')}</h2>
        <ol class="steps">${L(
          '<li>In Fortnite open the menu → <b>Settings</b>.</li><li><b>Game</b> tab → <b>Replays</b> section.</li><li>Turn on <b>Record Replays</b> for Battle Royale.</li><li>Play: the counter updates itself after every match.</li>',
          '<li>В Fortnite откройте меню → <b>Настройки</b>.</li><li>Вкладка <b>«Игра»</b> → раздел <b>«Повторы»</b>.</li><li>Включите <b>запись повторов</b> для Королевской битвы.</li><li>Играйте — после каждого матча счётчик обновится сам.</li>')}</ol>
      </div>
      <div class="fine">${L('Replay copies are kept in the program\'s data folder and are never deleted by the program, even by a full uninstall.',
        'Копии реплеев лежат в папке данных программы, и программа их никогда не удаляет, даже при полном удалении.')}
        Portions of the materials used are trademarks and/or copyrighted works of Epic Games, Inc. All rights reserved by Epic.
        This material is not official and is not endorsed by Epic.</div>`, settled ? 'settled' : '');
    view.querySelectorAll('#langSeg button').forEach((b) => b.addEventListener('click', () => { if (b.dataset.lang !== LANG) setLanguage(b.dataset.lang).catch(() => {}); }));
    $('#recount').addEventListener('click', async (e) => { e.currentTarget.disabled = true; await post('/api/recount'); setTimeout(() => draw(true).catch(() => {}), 1500); });
    $('#upd').addEventListener('click', async (e) => { const b = e.currentTarget; b.disabled = true; b.querySelector('span').textContent = L('Checking…', 'Проверяю…'); await post('/api/update/check'); draw(true).catch(() => {}); });
  };
  await draw(false);
  every(5000, () => draw(true).catch(() => {}));
}

// ---------------------------------------------------------------- start

(async () => {
  try {
    LANG = (await api('/api/settings')).lang === 'ru' ? 'ru' : 'en';
  } catch { /* English */ }
  applyLang();
  try {
    await Promise.all([document.fonts.load('30px "Burbank Big"'), document.fonts.load('16px "Burbank Small"')]);
  } catch { /* fall back to system fonts */ }
  measureDigitTop();
  requestAnimationFrame(() => document.body.classList.remove('booting'));
  route();
})();
