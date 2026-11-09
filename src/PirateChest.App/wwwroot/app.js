'use strict';

// ---------------------------------------------------------------- host window (WebView2)

const host = window.chrome?.webview;
if (!host) document.body.classList.add('nohost');
const send = (m) => { try { host?.postMessage(m); } catch { /* not in the app window */ } };

/** Title bar and empty side panel move the window; a quick second press maximizes, like a normal title bar. */
let lastPress = { t: 0, x: 0, y: 0 };
function dragArea(el, skip) {
  el.addEventListener('mousedown', (e) => {
    if (e.button !== 0 || (skip && e.target.closest(skip))) return;
    const now = performance.now();
    if (now - lastPress.t < 450 && Math.abs(e.screenX - lastPress.x) < 6 && Math.abs(e.screenY - lastPress.y) < 6) {
      lastPress.t = 0;
      send('max');
      return;
    }
    lastPress = { t: now, x: e.screenX, y: e.screenY };
    send('drag');
  });
}
dragArea(document.getElementById('titlebar'), '.wb');
dragArea(document.getElementById('side'), 'a');
document.querySelectorAll('.wb').forEach((b) => b.addEventListener('click', () => send(b.dataset.w)));
host?.addEventListener('message', (e) => {
  if (typeof e.data !== 'string' || !e.data.startsWith('state:')) return;
  const max = e.data === 'state:max';
  const b = document.querySelector('.wb[data-w="max"]');
  b.title = max ? 'Restore' : 'Maximize';
  b.querySelector('use').setAttribute('href', max ? '#w-restore' : '#w-max');
});

// ---------------------------------------------------------------- helpers

const $ = (sel, root = document) => root.querySelector(sel);
const esc = (s) => String(s ?? '').replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
const NF = new Intl.NumberFormat('en-US');
const n = (v) => NF.format(v ?? 0);
const n1 = (v) => (v == null ? '—' : new Intl.NumberFormat('en-US', { maximumFractionDigits: 2 }).format(v));
const icon = (name, cls = '') => `<svg class="ico ${cls}"><use href="#i-${name}"/></svg>`;
/** Fortnite icon from img/ (white silhouette, tinted with the text colour). */
const wi = (name, cls = '') => `<span class="wi ${cls}" style="--src:url(/img/${name}.webp)"></span>`;
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
const plural = (k, one, many) => (k === 1 ? one : many);
const matchesW = (k) => plural(k, 'match', 'matches');
const timesW = (k) => plural(k, 'time', 'times');
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
  return new Date(iso).toLocaleString('en-GB', { day: '2-digit', month: 'short', ...(withYear ? { year: 'numeric' } : {}), hour: '2-digit', minute: '2-digit' });
}
function ago(iso) {
  if (!iso) return '';
  const s = (Date.now() - new Date(iso).getTime()) / 1000;
  if (s < 90) return 'just now';
  if (s < 3600) return `${Math.round(s / 60)} min ago`;
  if (s < 86400 * 2) return `${Math.round(s / 3600)} h ago`;
  return `${Math.round(s / 86400)} days ago`;
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
    const parts = [];
    for (let pos = digits - 1; pos >= 0; pos--) {
      parts.push(`<span class="c" data-p="${pos}"><span class="strip">${[0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 0].map((d) => `<span>${d}</span>`).join('')}</span></span>`);
      if (pos > 0 && pos % 3 === 0) parts.push(`<span class="sep" data-s="${pos}">,</span>`);
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
function tableHtml(id, cols, rows, { empty = 'No data', sort = null, rowCls = null } = {}) {
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
      return (typeof x === 'string' ? x.localeCompare(y, 'en') : x - y) * k;
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
  try {
    await fn();
    if (seq !== routeSeq) return;
    stagger();
  } catch (e) {
    view.innerHTML = page(`<div class="banner">${icon('warn')}<div>Could not load data: ${esc(e.message)}</div></div>`);
  }
  view.scrollTop = 0;
  view.classList.remove('leaving');
}
window.addEventListener('hashchange', route);

function warnBanner(text) {
  return text ? `<div class="banner">${icon('warn')}<div><b>${esc(text)}</b><br>How to turn on replay recording: see <a href="#/settings">Settings</a>.</div></div>` : '';
}
function progressBanner(p) {
  if (!p.busy) return '';
  const pct = p.total ? Math.round((100 * p.done) / p.total) : 5;
  return `<div class="banner info">${icon('refresh')}<div style="flex:1"><b>Counting matches: ${n(p.done)} of ${n(p.total || p.pending)}</b>
    <div class="progress"><div style="width:${pct}%"></div></div></div></div>`;
}

// ---------------------------------------------------------------- home

const TYPES = {
  Normal: { img: '/img/chest.webp', name: 'Normal', one: 'Normal chest' },
  Rare: { img: '/img/chest-rare.webp', name: 'Rare', one: 'Rare chest' },
  Sprite: { img: '/img/chest-sprite.webp', name: 'Sprite', one: 'Sprite chest' },
};

/** Session clock state; the browser clock may differ from the PC clock the server uses. */
let session = null;

function sessionNote(s, now) {
  if (!s.active) return 'starts with your first match';
  if (s.playing) return '<span class="live"></span>match in progress';
  if (!s.lastEndUtc) return '';
  const pause = Math.max(0, Math.floor((now - new Date(s.lastEndUtc).getTime()) / 60000));
  return `break ${pause} min, resets in ${Math.max(0, s.gapMin - pause)} min`;
}

function tickSession() {
  const el = $('#sessClock');
  if (!el || !session) return;
  const now = Date.now() + session.offset;
  el.textContent = session.active ? clock(now - new Date(session.startedUtc).getTime()) : '—';
  el.parentElement.classList.toggle('off', !session.active);
  $('#sessNote').innerHTML = sessionNote(session, now);
}

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
      <div class="name">${esc(TYPES[x.type]?.name ?? x.type)}</div><div class="sub" data-sub="t${x.type}"></div>
    </div>`).join('');
  view.innerHTML = page(`
    <div id="banners"></div>
    <div class="grid g2">
      <div class="tile hero">
        <img class="pic" src="/img/chest.webp" alt="">
        <div class="value">${odoHtml('mine')}<span class="delta" data-delta="mine"></span></div>
        <div class="name">My chests</div><div class="sub" id="heroSub"></div>
      </div>
      <div class="tile hero">
        ${wi('squads', 'pic')}
        <div class="value">${odoHtml('squad')}<span class="delta" data-delta="squad"></span></div>
        <div class="name">Me + squad</div><div class="sub">with teammates, at least</div>
      </div>
    </div>
    <div class="grid g3 section">
      <div class="panel"><h3>Current session</h3>
        <div class="clock">${wi('timer')}<span id="sessClock">—</span></div>
        <div class="clocknote" id="sessNote"></div>
        <div class="stats small">
          <div class="stat"><div class="v gold">${odoHtml('sMine')}<span class="delta" data-delta="sMine"></span></div><div class="k">mine</div></div>
          <div class="stat"><div class="v sea">${odoHtml('sSquad')}<span class="delta" data-delta="sSquad"></span></div><div class="k">squad</div></div>
          <div class="stat"><div class="v">${odoHtml('sMatches')}<span class="delta" data-delta="sMatches"></span></div><div class="k" id="sMatchesK">matches</div></div></div></div>
      <div class="panel"><h3>Today</h3><div class="stats">
        <div class="stat"><div class="v gold">${odoHtml('dMine')}<span class="delta" data-delta="dMine"></span></div><div class="k">mine</div></div>
        <div class="stat"><div class="v sea">${odoHtml('dSquad')}<span class="delta" data-delta="dSquad"></span></div><div class="k">squad</div></div>
        <div class="stat"><div class="v">${odoHtml('dMatches')}<span class="delta" data-delta="dMatches"></span></div><div class="k" id="dMatchesK">matches</div></div></div></div>
      <div class="panel"><h3>Last match</h3><div id="lastMatch"></div></div>
    </div>
    <div class="section">
      <h2>By chest type</h2>
      <div class="grid g3">${typeTiles}</div>
      <div class="ministats">
        <div class="ministat">${wi('crown')}<div><div class="v" id="best"></div><div class="k">best match</div></div></div>
        <div class="ministat">${wi('chest-icon')}<div><div class="v" id="perMatch"></div><div class="k">average per match</div></div></div>
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
    $('#heroSub').textContent = `in ${n(s.matches)} ${matchesW(s.matches)}`;
    $('#sMatchesK').textContent = matchesW(ss.matches);
    $('#dMatchesK').textContent = matchesW(s.today.matches);
    for (const x of s.byType) view.querySelector(`[data-sub="t${x.type}"]`).textContent = `with squad: ${n(x.squad)}`;
    $('#best').textContent = n(s.best);
    $('#perMatch').textContent = n1(s.perMatch);
    $('#lastMatch').innerHTML = lm
      ? `<div class="stat"><div class="v gold">+${n(lm.mine)} <small>mine · +${n(lm.squad)} with squad</small></div><div class="d">${esc(lm.mode)} · ${dt(lm.endedUtc)}</div></div>`
      : '<div class="stat"><div class="v muted">—</div><div class="d">no matches yet</div></div>';
    tickSession();
  };
  update(s0, st0);
  every(5000, async () => {
    try {
      const [s, st] = await Promise.all([api('/api/summary'), api('/api/status')]);
      update(s, st);
    } catch { /* next time */ }
  });
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
const shortDate = (d) => new Date(`${d}T12:00:00`).toLocaleDateString('en-GB', { day: 'numeric', month: 'short' });
const longDate = (d) => new Date(`${d}T12:00:00`).toLocaleDateString('en-GB', { weekday: 'short', day: 'numeric', month: 'long' });

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
    const bar = (x0, ww, v, fill) => `<rect x="${x0}" y="${y(v)}" width="${ww}" height="${Math.max(0, y(0) - y(v))}" rx="3" fill="${fill}" data-i="${i}"/>`;
    parts.push(bar(x + (bw - w) / 2, w, r.squad, SQUAD));
    parts.push(bar(x + (bw - w) / 2 + w * 0.2, w * 0.6, r.mine, ME));
    if (rows.length <= 40 || i % Math.ceil(rows.length / 30) === 0) parts.push(`<g class="axis"><text x="${x + bw / 2}" y="${H - 12}" text-anchor="middle">${esc(shortDate(r.date))}</text></g>`);
  });
  parts.push('</svg>');
  el.innerHTML = `<div class="legend"><span><i style="background:${ME}"></i>mine</span><span><i style="background:${SQUAD}"></i>with squad</span></div>${parts.join('')}`;
  el.querySelectorAll('rect[data-i]').forEach((p) => {
    p.addEventListener('mousemove', (e) => {
      const r = rows[+p.dataset.i];
      showTip(e, `<b>${esc(longDate(r.date))}</b><br>mine: ${n(r.mine)}<br>with squad: ${n(r.squad)}<br>matches: ${n(r.matches)}`);
    });
    p.addEventListener('mouseleave', hideTip);
  });
}

function lineChart(el, rows) {
  const series = [{ key: 'cumSquad', color: SQUAD, label: 'with squad' }, { key: 'cumMine', color: ME, label: 'mine' }];
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
  const seq = routeSeq;
  const [daily, matches] = await Promise.all([api('/api/daily'), api('/api/matches')]);
  if (seq !== routeSeq) return;
  const counted = matches.filter((m) => m.status === 'counted');
  const skipped = matches.filter((m) => m.status !== 'counted');
  view.innerHTML = page(`
    <h1>History</h1>
    <div class="grid g2">
      <div class="panel chart"><h2>Chests per day</h2><div id="c1"></div></div>
      <div class="panel chart"><h2>Running total</h2><div id="c2"></div></div>
    </div>
    <div class="section"><h2>Matches · ${n(counted.length)}</h2>
      ${tableHtml('tMatches', [
        { label: 'Date', value: (m) => m.startedUtc, html: (m) => dt(m.startedUtc) },
        { label: 'Mode', value: (m) => m.mode, html: (m) => esc(m.mode) },
        { label: 'Length', num: true, value: (m) => m.durationMin, html: (m) => `${n1(m.durationMin)} min` },
        { label: 'Place', num: true, value: (m) => (m.placement == null ? null : -m.placement), html: (m) => rankBadge(m.placement) },
        { label: 'Mine', num: true, cls: 'big gold', value: (m) => m.mine, html: (m) => n(m.mine) },
        { label: 'Squad', num: true, cls: 'big sea', value: (m) => m.squad, html: (m) => n(m.squad) },
      ], counted, { empty: 'No matches yet', sort: { i: 0, dir: 'desc' } })}
    </div>
    ${skipped.length ? `<div class="section"><h2>Skipped · ${n(skipped.length)}</h2>
      ${tableHtml('tSkipped', [
        { label: 'Date', value: (m) => m.startedUtc, html: (m) => dt(m.startedUtc, true) },
        { label: 'Mode', value: (m) => m.playlist, html: (m) => esc(m.playlist || '—') },
        { label: 'Reason', value: (m) => m.skipReason, cls: 'wrap', html: (m) => esc(m.skipReason) },
      ], skipped, { sort: { i: 0, dir: 'desc' } })}</div>` : ''}`);
  bindTables(view);
  if (daily.length) {
    barChart($('#c1'), daily);
    lineChart($('#c2'), daily);
  } else {
    $('#c1').innerHTML = $('#c2').innerHTML = '<div class="empty">No data</div>';
  }
}

// ---------------------------------------------------------------- efficiency

async function renderEfficiency() {
  const seq = routeSeq;
  const e = await api('/api/efficiency');
  if (seq !== routeSeq) return;
  const o = e.overall;
  const ranked = [...e.ranking].sort((a, b) => b.cpm - a.cpm || b.avgMine - a.avgMine).map((r, i) => ({ ...r, rank: i + 1 }));
  view.innerHTML = page(`
    <h1>Farming efficiency</h1>
    <div class="grid g3">
      <div class="tile stattile">${wi('timer', 'pic')}<div class="value">${n1(o.cpm)}</div><div class="name">Per minute</div><div class="sub">chests per minute in game</div></div>
      <div class="tile stattile">${wi('chest-icon', 'pic')}<div class="value">${n1(o.perMatch)}</div><div class="name">Per match</div><div class="sub">on average</div></div>
      <div class="tile stattile">${wi('crown', 'pic')}<div class="value">${n(o.best)}</div><div class="name">Record</div><div class="sub">best match</div></div>
    </div>
    <div class="section"><h2>Landing spots</h2>
      ${tableHtml('tRank', [
        { label: '#', num: true, value: (r) => -r.rank, html: (r) => rankBadge(r.rank) },
        { label: 'Spot', value: (r) => r.name, html: (r) => `<span class="placename">${r.isPoi ? icon('pin') : ''}${esc(r.name)}</span>` },
        { label: 'Matches', num: true, value: (r) => r.matches, html: (r) => n(r.matches) },
        { label: 'Per minute', num: true, cls: 'big gold', value: (r) => r.cpm, html: (r) => n1(r.cpm) },
        { label: 'Per match', num: true, value: (r) => r.avgMine, html: (r) => n1(r.avgMine) },
        { label: 'Best', num: true, value: (r) => r.best, html: (r) => n(r.best) },
      ], ranked, { sort: { i: 0, dir: 'desc' }, rowCls: (r) => topCls(r.rank) })}
    </div>
    <div class="section"><h2>By match</h2>
      ${tableHtml('tEffMatches', [
        { label: 'Date', value: (m) => m.startedUtc, html: (m) => dt(m.startedUtc) },
        { label: 'Mode', value: (m) => m.mode, html: (m) => esc(m.mode) },
        { label: 'Landing', value: (m) => m.place, html: (m) => `<span class="placename">${esc(m.place)}</span>` },
        { label: 'In game', num: true, value: (m) => m.minutes, html: (m) => `${n1(m.minutes)} min` },
        { label: 'Mine', num: true, cls: 'big gold', value: (m) => m.mine, html: (m) => n(m.mine) },
        { label: 'Per minute', num: true, value: (m) => m.cpm, html: (m) => n1(m.cpm) },
      ], [...e.matches], { sort: { i: 0, dir: 'desc' } })}
    </div>`);
  bindTables(view);
}

// ---------------------------------------------------------------- map

/** How many times I opened a chest → marker colour and size (CSS px). */
const LOOT_CLASSES = [
  { min: 1, color: '#f4e39a', ink: '#2a2310', r: 4, label: 'once' },
  { min: 2, color: '#f0b43c', ink: '#2a2008', r: 6, label: '2–3' },
  { min: 4, color: '#e47834', ink: '#2a1306', r: 7, label: '4–6' },
  { min: 7, color: '#d2463e', ink: '#ffffff', r: 8.5, label: '7 or more' },
];
const lootClass = (k) => [...LOOT_CLASSES].reverse().find((c) => k >= c.min);

async function renderMap() {
  const seq = routeSeq;
  const [matches, status, calib] = await Promise.all([api('/api/matches'), api('/api/status'), api('/api/map/calibration')]);
  if (seq !== routeSeq) return;
  const counted = matches.filter((m) => m.status === 'counted');
  view.innerHTML = page(`
    <h1>Map</h1>
    <div class="maplayout">
      <div class="mapcol"><div class="mapframe"><canvas id="mapc" width="1200" height="1200"></canvas>
        <div class="zoom"><button class="icon" id="zIn" title="Zoom in">+</button><button class="icon" id="zOut" title="Zoom out">−</button></div>
        ${status.hasMap ? '<div class="credit">Map: Fortnite © Epic Games</div>' : ''}</div></div>
      <div class="mapside">
        <div class="panel">
          <h3>Show</h3>
          <select id="mMatch"><option value="">All matches</option>${counted.map((m) => `<option value="${esc(m.id)}">${dt(m.startedUtc)} · ${esc(m.mode)} · ${n(m.mine)}</option>`).join('')}</select>
          <select id="mType"><option value="">All chest types</option>${Object.entries(TYPES).map(([k, v]) => `<option value="${k}">${v.name}</option>`).join('')}</select>
          <div class="layers">
            <label class="chk"><input type="checkbox" id="lMe" checked> opened by me</label>
            <label class="chk"><input type="checkbox" id="lSquad" checked> opened only by squad</label>
            <label class="chk"><input type="checkbox" id="lOther" checked> other chests</label>
            <label class="chk"><input type="checkbox" id="lHeat"> heat map</label>
          </div>
        </div>
        <div class="panel">
          <h3>How many times I opened</h3>
          <div class="maplegend">
            ${LOOT_CLASSES.map((c) => `<div><span class="sw"><span class="mk" style="width:${2 * c.r + 2}px;height:${2 * c.r + 2}px;background:${c.color}"></span></span>${c.label}</div>`).join('')}
            <div><span class="sw"><span class="mk" style="width:10px;height:10px;background:rgba(0,0,0,.35);border:2px solid var(--squad)"></span></span>squad only</div>
            <div><span class="sw"><span class="mk" style="width:5px;height:5px;background:rgba(205,212,224,.7);border:0"></span></span>others</div>
          </div>
          <div class="maphint">The number is how many times. Scroll to zoom, drag to move the map.</div>
        </div>
        <div class="panel" id="mStats"></div>
      </div>
    </div>`);

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
  $('#mStats').innerHTML = `<h3>Opened by me</h3>
    <div class="kv">
      <div>Different chests</div><div class="num">${n(list.length)}</div>
      <div>Total opens</div><div class="num">${n(total)}</div>
      <div>Opened again</div><div class="num">${n(again)}</div>
      <div>Most often</div><div class="num">${top ? `${n(top)} ${timesW(top)}` : '—'}</div>
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
    }).then(() => document.fonts.load('20px "Burbank Big"')).then(() => {
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
    const top = col.getBoundingClientRect().top;
    const maxH = Math.max(420, window.innerHeight - top - 28);
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
    if (!ch) { showTip(e, `<b>Chest</b><br>${all ? 'nobody in the squad has opened it yet' : 'not opened in this match'}`); return; }
    const lines = [`<b>${esc(TYPES[ch.type]?.one ?? 'Chest')}</b>`];
    if (ch.me) lines.push(`I opened it ${n(ch.me)} ${timesW(ch.me)}${all && ch.matches !== ch.me ? ` (in ${n(ch.matches)} ${matchesW(ch.matches)})` : ''}`);
    if (ch.squad) lines.push(`squad: ${n(ch.squad)} ${timesW(ch.squad)}`);
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
    if (L.heat) ctx.drawImage(this.heat(chests), 0, 0);
    ctx.setTransform(1, 0, 0, 1, 0, 0);
    ctx.fillStyle = 'rgba(6, 11, 24, .22)'; // calm the map down so markers read better
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
      for (const ch of mine) {
        const k = lootClass(ch.me);
        const q = this.toScreen(ch.x, ch.y), r = k.r * R * zf;
        dot(q, r, k.color, 'rgba(10,12,18,.85)', 1.3 * R);
        if (ch.me > 1) {
          ctx.fillStyle = k.ink;
          ctx.font = `${Math.round(r * (ch.me >= 10 ? 1.2 : 1.45))}px "Burbank Big", Arial, sans-serif`;
          ctx.fillText(String(ch.me), q.x, q.y + 0.6 * R);
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
      const col = k < 0.5 ? lerp([108, 188, 255], [246, 210, 74], k * 2) : lerp([246, 210, 74], [210, 70, 62], (k - 0.5) * 2);
      d[i] = col[0]; d[i + 1] = col[1]; d[i + 2] = col[2]; d[i + 3] = Math.min(220, 60 + a * 400);
    }
    o.putImageData(data, 0, 0);
    return off;
  }
}

// ---------------------------------------------------------------- overlay settings

const SWATCHES = ['#ffffff', '#f6d24a', '#5fe08a', '#6cbcff'];

async function renderOverlay() {
  const seq = routeSeq;
  let o = await api('/api/overlay');
  if (seq !== routeSeq) return;
  let st = o.settings;
  const seg = (key, opts) => `<div class="seg" data-seg="${key}">${opts.map(([v, l]) => `<button data-v="${v}" class="${st[key] === v ? 'on' : ''}">${l}</button>`).join('')}</div>`;
  const sw = (key) => `<button class="switch ${st[key] ? 'on' : ''}" data-sw="${key}" aria-label="${key}"></button>`;
  view.innerHTML = page(`
    <h1>Overlay</h1>
    <div class="ovl">
      <div class="panel"><div class="rows">
        <div class="row"><div class="label">Overlay</div><div class="ctrl">${sw('enabled')}<span class="muted" id="ovState"></span></div></div>
        <div class="row"><div class="label">Number</div><div class="ctrl">${seg('show', [['total', 'Total'], ['session', 'Session'], ['today', 'Today']])}</div></div>
        <div class="row"><div class="label">Position</div><div class="ctrl"><button id="ovMove"></button><button id="ovReset">Reset</button></div></div>
        <div class="row"><div class="label">Size</div><div class="ctrl"><input type="range" min="50" max="300" step="5" id="ovScale" value="${st.scale}" style="flex:1"><span class="val" id="ovScaleV"></span></div></div>
        <div class="row"><div class="label">Number colour</div><div class="ctrl"><div class="swatches">${SWATCHES.map((c) => `<button class="swatch" data-c="${c}" style="background:${c}"></button>`).join('')}<input type="color" id="ovColor" value="${st.color}" title="Custom colour"></div></div></div>
        <div class="row"><div class="label">Background</div><div class="ctrl">${seg('background', [['none', 'None'], ['dark', 'Dark'], ['glass', 'Glass']])}</div></div>
        <div class="row"><div class="label">Background opacity</div><div class="ctrl"><input type="range" min="0" max="100" step="5" id="ovOpacity" value="${st.opacity}" style="flex:1"><span class="val" id="ovOpacityV"></span></div></div>
        <div class="row"><div class="label">Chest icon</div><div class="ctrl">${sw('icon')}</div></div>
        <div class="row"><div class="label">Label</div><div class="ctrl">${sw('label')}</div></div>
        <div class="row"><div class="label">Show</div><div class="ctrl">${seg('when', [['fortnite', 'Only over Fortnite'], ['always', 'Always']])}</div></div>
      </div></div>
      <div>
        <div class="preview" style="background-image:url(/map.png)"><div class="ov" id="ovPreview"></div></div>
        <div class="hint">The overlay is a separate window on top of the game: it does not touch Fortnite. With "Only over Fortnite" it
          shows only while the Fortnite window is the active one and hides when you switch away. It is visible over the game in
          <b>Windowed Fullscreen</b> or <b>Windowed</b> display mode. The number updates about half a minute after each match ends;
          (+N) is what the last match added. Clicks go through it; use Move to drag it into place.</div>
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
    $('#ovMove').textContent = o.moving ? 'Done' : 'Move';
    $('#ovMove').classList.toggle('primary', o.moving);
    $('#ovState').textContent = !st.enabled ? 'off' : st.when === 'fortnite' ? 'on, shows while the Fortnite window is active' : 'on';
    const label = st.show === 'session' ? 'SESSION' : st.show === 'today' ? 'TODAY' : 'MY CHESTS';
    const box = `<div class="ovbox ${st.background}${st.opacity < 15 ? ' bare' : ''}" style="--op:${st.opacity / 100}">
      ${st.icon ? '<img src="/img/chest.webp" alt="">' : ''}
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
  ? `${new Intl.NumberFormat('en-US', { maximumFractionDigits: 1 }).format(bytes / 1073741824)} GB`
  : `${Math.round(bytes / 1048576)} MB`);

async function renderSettings() {
  const seq = routeSeq;
  const draw = async (settled) => {
    const s = await api('/api/status');
    if (seq !== routeSeq) return;
    const p = s.progress;
    view.innerHTML = page(`
      <h1>Settings</h1>
      ${warnBanner(s.recordingWarning)}
      <div class="grid g2">
        <div class="panel">
          <h2>Program</h2>
          <div class="kv">
            <div>Version</div><div class="num">v${esc(s.version)}</div>
            <div>Updates</div><div>${esc(s.update.status)}</div>
            <div>Start with Windows</div><div>${s.autostart ? 'on' : 'off'}</div>
          </div>
          <div style="margin-top:18px"><button class="primary" id="upd">${icon('refresh')}<span>Check for updates</span></button></div>
        </div>
        <div class="panel">
          <h2>Replays</h2>
          <div class="kv">
            <div>In the Fortnite folder</div><div>${s.demosFound ? n(s.replayFiles) : 'no folder'}</div>
            <div>Copies saved</div><div>${n(s.backups)} · ${size(s.backupBytes)}</div>
            <div>Latest</div><div>${s.newestReplayUtc ? `${dt(s.newestReplayUtc)} · ${ago(s.newestReplayUtc)}` : '—'}</div>
            <div>Fortnite</div><div>${s.fortniteRunning ? 'running' : 'not running'}</div>
            ${p.busy ? `<div>Counting</div><div>${n(p.done)} of ${n(p.total || p.pending)}</div>` : ''}
          </div>
          <div style="margin-top:18px"><button id="recount">${icon('refresh')}<span>Recount everything</span></button></div>
        </div>
      </div>
      <div class="panel section">
        <h2>How to turn on replay recording</h2>
        <ol class="steps">
          <li>In Fortnite open the menu → <b>Settings</b>.</li>
          <li><b>Game</b> tab → <b>Replays</b> section.</li>
          <li>Turn on <b>Record Replays</b> for Battle Royale.</li>
          <li>Play: the counter updates itself after every match.</li>
        </ol>
      </div>
      <div class="fine">Replay copies are kept in the program's data folder and are never deleted by the program, even by a full
        uninstall. Portions of the materials used are trademarks and/or copyrighted works of Epic Games, Inc. All rights reserved by Epic.
        This material is not official and is not endorsed by Epic.</div>`, settled ? 'settled' : '');
    $('#recount').addEventListener('click', async (e) => { e.currentTarget.disabled = true; await post('/api/recount'); setTimeout(() => draw(true).catch(() => {}), 1500); });
    $('#upd').addEventListener('click', async (e) => { const b = e.currentTarget; b.disabled = true; b.querySelector('span').textContent = 'Checking…'; await post('/api/update/check'); draw(true).catch(() => {}); });
  };
  await draw(false);
  every(5000, () => draw(true).catch(() => {}));
}

// ---------------------------------------------------------------- start

(async () => {
  try {
    await Promise.all([document.fonts.load('30px "Burbank Big"'), document.fonts.load('16px "Burbank Small"')]);
  } catch { /* fall back to system fonts */ }
  requestAnimationFrame(() => document.body.classList.remove('booting'));
  route();
})();
