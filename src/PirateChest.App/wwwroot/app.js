'use strict';

// ---------------------------------------------------------------- helpers

const $ = (sel, root = document) => root.querySelector(sel);
const esc = (s) => String(s ?? '').replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
const nf = new Intl.NumberFormat('ru-RU');
const n = (v) => nf.format(v ?? 0);
const n1 = (v) => (v == null ? '—' : new Intl.NumberFormat('ru-RU', { maximumFractionDigits: 2 }).format(v));
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
  const t = await r.text();
  return t ? JSON.parse(t) : null;
}

function plural(k, one, few, many) {
  const m10 = k % 10, m100 = k % 100;
  if (m10 === 1 && m100 !== 11) return one;
  if (m10 >= 2 && m10 <= 4 && (m100 < 12 || m100 > 14)) return few;
  return many;
}
const matchesW = (k) => plural(k, 'матч', 'матча', 'матчей');
const timesW = (k) => plural(k, 'раз', 'раза', 'раз');

function dt(iso, withYear = false) {
  if (!iso) return '—';
  return new Date(iso).toLocaleString('ru-RU', { day: '2-digit', month: '2-digit', ...(withYear ? { year: 'numeric' } : {}), hour: '2-digit', minute: '2-digit' });
}
function ago(iso) {
  if (!iso) return '';
  const s = (Date.now() - new Date(iso).getTime()) / 1000;
  if (s < 90) return 'только что';
  if (s < 3600) return `${Math.round(s / 60)} мин назад`;
  if (s < 86400 * 2) return `${Math.round(s / 3600)} ч назад`;
  return `${Math.round(s / 86400)} дн назад`;
}
/** 1:05:09 */
function clock(ms) {
  const t = Math.max(0, Math.floor(ms / 1000));
  const h = Math.floor(t / 3600), m = Math.floor((t % 3600) / 60), s = t % 60;
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
function tableHtml(id, cols, rows, { empty = 'Нет данных', sort = null } = {}) {
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
      return (typeof x === 'string' ? x.localeCompare(y, 'ru') : x - y) * k;
    });
  }
  return list.map((r) => `<tr>${cols.map((c) => `<td class="${c.num ? 'n ' : ''}${c.cls ?? ''}">${c.html(r)}</td>`).join('')}</tr>`).join('');
}
function bindTables(root = document) {
  root.querySelectorAll('table.board[id]').forEach((t) => {
    if (!tables[t.id]) return;
    t.querySelector('thead').addEventListener('click', (e) => {
      const th = e.target.closest('th.sort');
      if (!th) return;
      const i = +th.dataset.i;
      const st = sortState[t.id];
      const first = tables[t.id].cols[i].num ? 'desc' : 'asc';
      sortState[t.id] = { i, dir: st && st.i === i ? (st.dir === 'asc' ? 'desc' : 'asc') : first };
      t.querySelector('thead tr').innerHTML = headHtml(t.id);
      t.querySelector('tbody').innerHTML = bodyHtml(t.id);
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
  window.scrollTo(0, 0);
  if (name !== '') refreshChip();
  fn().catch((e) => { view.innerHTML = `<div class="banner">${icon('warn')}<div>Не удалось загрузить данные: ${esc(e.message)}</div></div>`; });
}
window.addEventListener('hashchange', route);

function setChip(mine, version) {
  $('#chipValue').textContent = n(mine);
  if (version) $('#ver').textContent = `v${version}`;
}
function refreshChip() { api('/api/summary').then((s) => setChip(s.mine, s.version)).catch(() => {}); }

function warnBanner(text) {
  return text ? `<div class="banner">${icon('warn')}<div><b>${esc(text)}</b><br>Как включить запись повторов — во вкладке <a href="#/updates">«Обновления»</a>.</div></div>` : '';
}
function progressBanner(p) {
  if (!p.busy) return '';
  const pct = p.total ? Math.round((100 * p.done) / p.total) : 5;
  return `<div class="banner info">${icon('refresh')}<div style="flex:1"><b>Считаю матчи: ${n(p.done)} из ${n(p.total || p.pending)}</b>
    <div class="progress"><div style="width:${pct}%"></div></div></div></div>`;
}

// ---------------------------------------------------------------- home

const TYPES = {
  Normal: { cls: 'normal', img: '/img/chest.webp', one: 'Обычный сундук' },
  Rare: { cls: 'rare', img: '/img/chest-rare.webp', one: 'Редкий сундук' },
  Sprite: { cls: 'sprite', img: '/img/chest-sprite.webp', one: 'Спрайтовый сундук' },
};

/** Session clock state; the browser clock may differ from the PC clock the server uses. */
let session = null;

function sessionNote(s, now) {
  if (!s.active) return 'начнётся с первым матчем';
  if (s.playing) return '<span class="live"></span>идёт матч';
  if (!s.lastEndUtc) return '';
  const pause = Math.max(0, Math.floor((now - new Date(s.lastEndUtc).getTime()) / 60000));
  return `перерыв ${pause} мин, сброс через ${Math.max(0, s.gapMin - pause)} мин`;
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
    setChip(s.mine, s.version);
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
          <div class="name">Мои сундуки</div><div class="sub">за ${n(s.matches)} ${matchesW(s.matches)}</div>
        </div>
        <div class="tile squad hero">
          ${wi('squads', 'pic')}
          <div class="value">${n(s.squad)}</div>
          <div class="name">Мои + отряд</div><div class="sub">вместе с тиммейтами, не меньше</div>
        </div>
      </div>
      <div class="grid g3 section">
        <div class="panel"><h3>Текущая сессия</h3>
          <div class="clock ${ss.active ? '' : 'off'}">${wi('timer')}<span id="sessClock">${ss.active ? clock(Date.now() + session.offset - new Date(ss.startedUtc).getTime()) : '—'}</span></div>
          <div class="clocknote" id="sessNote">${sessionNote(ss, Date.now() + session.offset)}</div>
          <div class="stats small">
            <div class="stat"><div class="v gold">${n(ss.mine)}</div><div class="k">мои</div></div>
            <div class="stat"><div class="v sea">${n(ss.squad)}</div><div class="k">отряд</div></div>
            <div class="stat"><div class="v">${n(ss.matches)}</div><div class="k">${matchesW(ss.matches)}</div></div></div></div>
        <div class="panel"><h3>Сегодня</h3><div class="stats">
          <div class="stat"><div class="v gold">${n(s.today.mine)}</div><div class="k">мои</div></div>
          <div class="stat"><div class="v sea">${n(s.today.squad)}</div><div class="k">отряд</div></div>
          <div class="stat"><div class="v">${n(s.today.matches)}</div><div class="k">${matchesW(s.today.matches)}</div></div></div></div>
        <div class="panel"><h3>Последний матч</h3>${last ? `
          <div class="stat"><div class="v gold">+${n(last.mine)} <small>мои · +${n(last.squad)} с отрядом</small></div>
          <div class="d">${esc(last.mode)} · ${dt(last.endedUtc)}</div></div>` : '<div class="stat"><div class="v muted">—</div><div class="d">матчей пока нет</div></div>'}</div>
      </div>
      <div class="section">
        <h2>По типам сундуков</h2>
        <div class="grid g3">${s.byType.map((t) => `
          <div class="tile ${TYPES[t.type]?.cls ?? 'normal'} typetile">
            <img class="pic" src="${TYPES[t.type]?.img ?? TYPES.Normal.img}" alt="">
            <div class="count">${n(t.mine)}</div>
            <div class="name">${esc(t.label)}</div><div class="sub">с отрядом: ${n(t.squad)}</div>
          </div>`).join('')}
        </div>
        <div class="ministats">
          <div class="ministat">${wi('crown')}<div><div class="v">${n(s.best)}</div><div class="k">лучший матч</div></div></div>
          <div class="ministat">${wi('chest-icon')}<div><div class="v">${n1(s.perMatch)}</div><div class="k">в среднем за матч</div></div></div>
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
const shortDate = (d) => { const [, m, day] = d.split('-'); return `${day}.${m}`; };
const longDate = (d) => new Date(`${d}T12:00:00`).toLocaleDateString('ru-RU', { weekday: 'short', day: 'numeric', month: 'long' });

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
  el.innerHTML = `<div class="legend"><span><i style="background:${ME}"></i>мои</span><span><i style="background:${SQUAD}"></i>с отрядом</span></div>${parts.join('')}`;
  el.querySelectorAll('rect[data-i]').forEach((p) => {
    p.addEventListener('mousemove', (e) => {
      const r = rows[+p.dataset.i];
      showTip(e, `<b>${esc(longDate(r.date))}</b><br>мои: ${n(r.mine)}<br>с отрядом: ${n(r.squad)}<br>матчей: ${n(r.matches)}`);
    });
    p.addEventListener('mouseleave', hideTip);
  });
}

function lineChart(el, rows) {
  const series = [{ key: 'cumSquad', color: SQUAD, label: 'с отрядом' }, { key: 'cumMine', color: ME, label: 'мои' }];
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
    <h1>История</h1>
    <div class="grid g2">
      <div class="panel chart"><h2>Сундуки по дням</h2><div id="c1"></div></div>
      <div class="panel chart"><h2>Нарастающий итог</h2><div id="c2"></div></div>
    </div>
    <div class="section"><h2>Матчи · ${n(counted.length)}</h2>
      ${tableHtml('tMatches', [
        { label: 'Дата', value: (m) => m.startedUtc, html: (m) => dt(m.startedUtc) },
        { label: 'Режим', value: (m) => m.mode, html: (m) => esc(m.mode) },
        { label: 'Длительность', num: true, value: (m) => m.durationMin, html: (m) => `${n1(m.durationMin)} мин` },
        { label: 'Место', num: true, value: (m) => (m.placement == null ? null : -m.placement), html: (m) => rankBadge(m.placement, true) },
        { label: 'Мои', num: true, cls: 'big gold', value: (m) => m.mine, html: (m) => n(m.mine) },
        { label: 'Отряд', num: true, cls: 'big sea', value: (m) => m.squad, html: (m) => n(m.squad) },
      ], counted, { empty: 'Матчей пока нет', sort: { i: 0, dir: 'desc' } })}
    </div>
    ${skipped.length ? `<div class="section"><h2>Пропущено · ${n(skipped.length)}</h2>
      ${tableHtml('tSkipped', [
        { label: 'Дата', value: (m) => m.startedUtc, html: (m) => dt(m.startedUtc, true) },
        { label: 'Режим', value: (m) => m.playlist, html: (m) => esc(m.playlist || '—') },
        { label: 'Причина', value: (m) => m.skipReason, cls: 'wrap', html: (m) => esc(m.skipReason) },
      ], skipped, { sort: { i: 0, dir: 'desc' } })}</div>` : ''}`;
  bindTables(view);
  if (daily.length) {
    barChart($('#c1'), daily);
    lineChart($('#c2'), daily);
  } else {
    $('#c1').innerHTML = $('#c2').innerHTML = '<div class="empty">Нет данных</div>';
  }
}

// ---------------------------------------------------------------- efficiency

async function renderEfficiency() {
  const e = await api('/api/efficiency');
  const o = e.overall;
  const ranked = [...e.ranking].sort((a, b) => b.cpm - a.cpm || b.avgMine - a.avgMine).map((r, i) => ({ ...r, rank: i + 1 }));
  view.innerHTML = `
    <h1>Эффективность фарма</h1>
    <div class="grid g3">
      <div class="tile me stattile">${wi('timer', 'pic')}<div class="value">${n1(o.cpm)}</div><div class="name">В минуту</div><div class="sub">сундуков за минуту игры</div></div>
      <div class="tile rare stattile">${wi('chest-icon', 'pic')}<div class="value">${n1(o.perMatch)}</div><div class="name">За матч</div><div class="sub">в среднем</div></div>
      <div class="tile sprite stattile">${wi('crown', 'pic')}<div class="value">${n(o.best)}</div><div class="name">Рекорд</div><div class="sub">лучший матч</div></div>
    </div>
    <div class="section"><h2>Рейтинг мест высадки</h2>
      ${tableHtml('tRank', [
        { label: '#', num: true, value: (r) => -r.rank, html: (r) => rankBadge(r.rank) },
        { label: 'Место', value: (r) => r.name, html: (r) => `<span class="placename">${r.isPoi ? icon('pin') : ''}${esc(r.name)}</span>` },
        { label: 'Матчей', num: true, value: (r) => r.matches, html: (r) => n(r.matches) },
        { label: 'В минуту', num: true, cls: 'big gold', value: (r) => r.cpm, html: (r) => n1(r.cpm) },
        { label: 'За матч', num: true, value: (r) => r.avgMine, html: (r) => n1(r.avgMine) },
        { label: 'Лучший', num: true, value: (r) => r.best, html: (r) => n(r.best) },
      ], ranked, { sort: { i: 0, dir: 'desc' } })}
    </div>
    <div class="section"><h2>По матчам</h2>
      ${tableHtml('tEffMatches', [
        { label: 'Дата', value: (m) => m.startedUtc, html: (m) => dt(m.startedUtc) },
        { label: 'Режим', value: (m) => m.mode, html: (m) => esc(m.mode) },
        { label: 'Высадка', value: (m) => m.place, html: (m) => `<span class="placename">${esc(m.place)}</span>` },
        { label: 'В игре', num: true, value: (m) => m.minutes, html: (m) => `${n1(m.minutes)} мин` },
        { label: 'Мои', num: true, cls: 'big gold', value: (m) => m.mine, html: (m) => n(m.mine) },
        { label: 'В минуту', num: true, value: (m) => m.cpm, html: (m) => n1(m.cpm) },
      ], [...e.matches], { sort: { i: 0, dir: 'desc' } })}
    </div>`;
  bindTables(view);
}

// ---------------------------------------------------------------- map

/** How many times I opened a chest → marker colour and size (CSS px). */
const LOOT_CLASSES = [
  { min: 1, color: '#f4e39a', ink: '#2a2310', r: 4, label: '1 раз' },
  { min: 2, color: '#f0b43c', ink: '#2a2008', r: 6, label: '2–3' },
  { min: 4, color: '#e47834', ink: '#2a1306', r: 7, label: '4–6' },
  { min: 7, color: '#d2463e', ink: '#ffffff', r: 8.5, label: '7 и больше' },
];
const lootClass = (k) => [...LOOT_CLASSES].reverse().find((c) => k >= c.min);

async function renderMap() {
  const [matches, status, calib] = await Promise.all([api('/api/matches'), api('/api/status'), api('/api/map/calibration')]);
  const counted = matches.filter((m) => m.status === 'counted');
  view.innerHTML = `
    <h1>Карта</h1>
    <div class="maplayout">
      <div class="mapcol"><div class="mapframe"><canvas id="mapc" width="1200" height="1200"></canvas>
        <div class="zoom"><button class="icon" id="zIn" title="Приблизить">+</button><button class="icon" id="zOut" title="Отдалить">−</button></div>
        ${status.hasMap ? '<div class="credit">Карта: Fortnite © Epic Games</div>' : ''}</div></div>
      <div class="mapside">
        <div class="panel">
          <h3>Что показать</h3>
          <select id="mMatch"><option value="">Все матчи</option>${counted.map((m) => `<option value="${esc(m.id)}">${dt(m.startedUtc)} · ${esc(m.mode)} · ${n(m.mine)}</option>`).join('')}</select>
          <select id="mType"><option value="">Все типы сундуков</option><option value="Normal">Обычные</option><option value="Rare">Редкие</option><option value="Sprite">Спрайтовые</option></select>
          <div class="layers">
            <label class="chk"><input type="checkbox" id="lMe" checked> открытые мной</label>
            <label class="chk"><input type="checkbox" id="lSquad" checked> открытые только отрядом</label>
            <label class="chk"><input type="checkbox" id="lOther" checked> остальные сундуки</label>
            <label class="chk"><input type="checkbox" id="lHeat"> тепловая карта</label>
          </div>
        </div>
        <div class="panel">
          <h3>Сколько раз я открыл</h3>
          <div class="maplegend">
            ${LOOT_CLASSES.map((c) => `<div><span class="sw"><span class="mk" style="width:${2 * c.r + 2}px;height:${2 * c.r + 2}px;background:${c.color}"></span></span>${c.label}</div>`).join('')}
            <div><span class="sw"><span class="mk" style="width:10px;height:10px;background:rgba(0,0,0,.35);border:2px solid var(--squad)"></span></span>только отряд</div>
            <div><span class="sw"><span class="mk" style="width:5px;height:5px;background:rgba(205,212,224,.7);border:0"></span></span>остальные</div>
          </div>
          <div class="maphint">Цифра на кружке — сколько раз. Колёсико мыши приближает, карту можно двигать.</div>
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
  $('#mStats').innerHTML = `<h3>Открыто мной</h3>
    <div class="kv">
      <div>Разных сундуков</div><div><b>${n(list.length)}</b></div>
      <div>Всего открытий</div><div><b>${n(total)}</b></div>
      <div>Открывал повторно</div><div><b>${n(again)}</b></div>
      <div>Чаще всего</div><div><b>${top ? `${n(top)} ${timesW(top)}` : '—'}</b></div>
    </div>`;
}

/** Complex numbers for the 2-point similarity transform world (X + iY) → image pixel (px + i·py). */
const C = {
  sub: (a, b) => [a[0] - b[0], a[1] - b[1]],
  add: (a, b) => [a[0] + b[0], a[1] + b[1]],
  mul: (a, b) => [a[0] * b[0] - a[1] * b[1], a[0] * b[1] + a[1] * b[0]],
  div: (a, b) => { const d = b[0] * b[0] + b[1] * b[1]; return [(a[0] * b[0] + a[1] * b[1]) / d, (a[1] * b[0] - a[0] * b[1]) / d]; },
};
const lerp = (a, b, t) => a.map((v, i) => Math.round(v + (b[i] - v) * t));

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
    }).then(() => {
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
    if (!ch) { showTip(e, `<b>Сундук</b><br>${all ? 'ещё никто из отряда не открывал' : 'в этом матче не открывали'}`); return; }
    const lines = [`<b>${esc(TYPES[ch.type]?.one ?? 'Сундук')}</b>`];
    if (ch.me) lines.push(`я открыл: ${n(ch.me)} ${timesW(ch.me)}${all && ch.matches !== ch.me ? ` (в ${n(ch.matches)} ${plural(ch.matches, 'матче', 'матчах', 'матчах')})` : ''}`);
    if (ch.squad) lines.push(`отряд: ${n(ch.squad)} ${timesW(ch.squad)}`);
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
      for (const ch of mine) {
        const k = lootClass(ch.me);
        const q = this.toScreen(ch.x, ch.y), r = k.r * R * zf;
        dot(q, r, k.color, 'rgba(10,12,18,.85)', 1.3 * R);
        if (ch.me > 1) {
          ctx.fillStyle = k.ink;
          ctx.font = `bold ${Math.round(r * (ch.me >= 10 ? 1.05 : 1.3))}px Arial, sans-serif`;
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
      const t = Math.min(1, a * 1.4);
      const col = t < 0.5 ? lerp([91, 159, 208], [240, 194, 74], t * 2) : lerp([240, 194, 74], [210, 70, 62], (t - 0.5) * 2);
      d[i] = col[0]; d[i + 1] = col[1]; d[i + 2] = col[2]; d[i + 3] = Math.min(220, 60 + a * 400);
    }
    o.putImageData(data, 0, 0);
    return off;
  }
}

// ---------------------------------------------------------------- updates

const size = (bytes) => (bytes >= 1073741824
  ? `${new Intl.NumberFormat('ru-RU', { maximumFractionDigits: 1 }).format(bytes / 1073741824)} ГБ`
  : `${Math.round(bytes / 1048576)} МБ`);

async function renderUpdates() {
  const draw = async () => {
    const s = await api('/api/status');
    const p = s.progress;
    $('#ver').textContent = `v${s.version}`;
    view.innerHTML = `
      <h1>Обновления</h1>
      ${warnBanner(s.recordingWarning)}
      <div class="grid g2">
        <div class="panel">
          <h2>Программа</h2>
          <div class="kv">
            <div>Версия</div><div><b>v${esc(s.version)}</b></div>
            <div>Обновления</div><div>${esc(s.update.status)}</div>
            <div>Автозапуск</div><div>${s.autostart ? 'включён' : 'выключен'}</div>
          </div>
          <div style="margin-top:16px"><button class="primary" id="upd">${icon('refresh')}<span>Проверить обновления</span></button></div>
        </div>
        <div class="panel">
          <h2>Реплеи</h2>
          <div class="kv">
            <div>В папке Fortnite</div><div>${s.demosFound ? n(s.replayFiles) : 'папки нет'}</div>
            <div>Сохранено копий</div><div>${n(s.backups)} · ${size(s.backupBytes)}</div>
            <div>Последний</div><div>${s.newestReplayUtc ? `${dt(s.newestReplayUtc)} · ${ago(s.newestReplayUtc)}` : '—'}</div>
            <div>Fortnite</div><div>${s.fortniteRunning ? 'запущен' : 'не запущен'}</div>
            ${p.busy ? `<div>Считаю</div><div>${n(p.done)} из ${n(p.total || p.pending)}</div>` : ''}
          </div>
          <div style="margin-top:16px"><button id="recount">${icon('refresh')}<span>Пересчитать всё</span></button></div>
        </div>
      </div>
      <div class="panel section">
        <h2>Как включить запись повторов</h2>
        <ol class="steps">
          <li>В Fortnite откройте меню → <b>Настройки</b>.</li>
          <li>Вкладка <b>«Игра»</b> → раздел <b>«Повторы»</b>.</li>
          <li>Включите <b>запись повторов</b> для Королевской битвы.</li>
          <li>Играйте — после каждого матча счётчик обновится сам.</li>
        </ol>
      </div>
      <div class="fine">Portions of the materials used are trademarks and/or copyrighted works of Epic Games, Inc. All rights reserved by Epic.
        This material is not official and is not endorsed by Epic.</div>`;
    $('#recount').addEventListener('click', async (e) => { e.currentTarget.disabled = true; await post('/api/recount'); setTimeout(() => draw().catch(() => {}), 1500); });
    $('#upd').addEventListener('click', async (e) => { const b = e.currentTarget; b.disabled = true; b.querySelector('span').textContent = 'Проверяю…'; await post('/api/update/check'); draw().catch(() => {}); });
  };
  await draw();
  every(5000, () => draw().catch(() => {}));
}

route();
