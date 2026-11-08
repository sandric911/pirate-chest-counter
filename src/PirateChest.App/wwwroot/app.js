'use strict';

// ---------------------------------------------------------------- helpers

const $ = (sel, root = document) => root.querySelector(sel);
const esc = (s) => String(s ?? '').replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
const nf = new Intl.NumberFormat('ru-RU');
const n = (v) => nf.format(v ?? 0);
const n1 = (v) => (v == null ? '—' : new Intl.NumberFormat('ru-RU', { maximumFractionDigits: 2 }).format(v));
const icon = (name, cls = '') => `<svg class="ico ${cls}"><use href="#i-${name}"/></svg>`;

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

const tip = $('#tip');
function showTip(e, html) {
  tip.innerHTML = html;
  tip.style.display = 'block';
  tip.style.left = `${e.clientX + 14}px`;
  tip.style.top = `${e.clientY + 14}px`;
}
function hideTip() { tip.style.display = 'none'; }

// ---------------------------------------------------------------- sortable tables

const sortState = {};
const tables = {};

/** cols: [{ label, value(row) for sorting, html(row) for the cell, num, sort:false }] */
function tableHtml(id, cols, rows, { empty = 'Нет данных', sort = null, max = 600 } = {}) {
  if (!sortState[id] && sort) sortState[id] = sort;
  tables[id] = { cols, rows, empty };
  return `<div class="tablewrap" style="max-height:${max}px"><table id="${id}"><thead><tr>${headHtml(id)}</tr></thead><tbody>${bodyHtml(id)}</tbody></table></div>`;
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
    const c = cols[st.i];
    const val = c.value ?? (() => 0);
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
  root.querySelectorAll('table[id]').forEach((t) => {
    if (!tables[t.id]) return;
    t.querySelector('thead').addEventListener('click', (e) => {
      const th = e.target.closest('th.sort');
      if (!th) return;
      const i = +th.dataset.i;
      const st = sortState[t.id];
      const firstDir = tables[t.id].cols[i].num ? 'desc' : 'asc';
      sortState[t.id] = { i, dir: st && st.i === i ? (st.dir === 'asc' ? 'desc' : 'asc') : firstDir };
      t.querySelector('thead tr').innerHTML = headHtml(t.id);
      t.querySelector('tbody').innerHTML = bodyHtml(t.id);
    });
  });
}

// ---------------------------------------------------------------- router

const view = $('#view');
let timer = null;
let current = null;

const routes = {
  '': renderHome,
  history: renderHistory,
  efficiency: renderEfficiency,
  map: renderMap,
  updates: renderUpdates,
  status: renderUpdates,
};

function route() {
  let name = location.hash.replace(/^#\/?/, '').split('?')[0];
  if (name === 'status') name = 'updates';
  const fn = routes[name] ?? renderHome;
  document.querySelectorAll('#tabs a').forEach((a) => a.classList.toggle('active', a.dataset.tab === name));
  if (timer) { clearInterval(timer); timer = null; }
  if (current?.dispose) current.dispose();
  current = null;
  fn().catch((e) => { view.innerHTML = `<div class="banner">${icon('warn')}<div>Не удалось загрузить данные: ${esc(e.message)}</div></div>`; });
}
window.addEventListener('hashchange', route);

function setVersion(v) { $('#ver').textContent = `v${v}`; }

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
  Normal: { tile: 't-uncommon', icon: 'chest' },
  Rare: { tile: 't-rare', icon: 'star' },
  Sprite: { tile: 't-epic', icon: 'sprite' },
};

async function renderHome() {
  const draw = async () => {
    const [s, st] = await Promise.all([api('/api/summary'), api('/api/status')]);
    setVersion(s.version);
    view.innerHTML = `
      ${warnBanner(st.recordingWarning)}
      ${progressBanner(s.progress)}
      <div class="grid g2">
        <div class="tile t-legend">
          <svg class="ico watermark"><use href="#i-chest"/></svg>
          <div class="label">${icon('chest')} Мои сундуки</div>
          <div class="big">${n(s.mine)}</div>
          <div class="sub">за ${n(s.matches)} ${matchesW(s.matches)}</div>
        </div>
        <div class="tile t-rare">
          <svg class="ico watermark"><use href="#i-squad"/></svg>
          <div class="label">${icon('squad')} Мои + отряд <span class="tag">не меньше</span></div>
          <div class="big">${n(s.squad)}</div>
        </div>
      </div>
      <div class="grid g3" style="margin-top:18px">
        <div class="tile t-epic"><h3>${icon('calendar')} Сегодня</h3><div class="row">
          <div class="stat"><div class="v">${n(s.today.mine)}</div><div class="k">мои</div></div>
          <div class="stat"><div class="v">${n(s.today.squad)}</div><div class="k">с отрядом</div></div>
          <div class="stat"><div class="v">${n(s.today.matches)}</div><div class="k">${matchesW(s.today.matches)}</div></div></div></div>
        <div class="tile t-uncommon"><h3>${icon('bolt')} Текущая сессия</h3>${s.session.matches ? `<div class="row">
          <div class="stat"><div class="v">${n(s.session.mine)}</div><div class="k">мои</div></div>
          <div class="stat"><div class="v">${n(s.session.squad)}</div><div class="k">с отрядом</div></div>
          <div class="stat"><div class="v">${n(s.session.matches)}</div><div class="k">${matchesW(s.session.matches)}</div></div></div>`
          : '<div class="stat"><div class="k" style="font-size:15px">Сессия не идёт</div></div>'}</div>
        <div class="card"><h3>${icon('clock')} Последний матч</h3>${s.lastMatch ? `
          <div class="stat"><div class="v gold">+${n(s.lastMatch.mine)} <small>мои · +${n(s.lastMatch.squad)} с отрядом</small></div>
          <div class="k muted">${esc(s.lastMatch.mode)} · закончился ${dt(s.lastMatch.endedUtc)} (${ago(s.lastMatch.endedUtc)})</div></div>` : '<div class="muted">Пока нет матчей.</div>'}</div>
      </div>
      <div class="card" style="margin-top:18px">
        <h2>${icon('chest')} По типам сундуков</h2>
        <div class="types">${s.byType.map((t) => `
          <div class="tile ${TYPES[t.type]?.tile ?? 't-common'}">
            ${icon(TYPES[t.type]?.icon ?? 'chest')}
            <div class="t">${esc(t.label)}</div>
            <div class="n">${n(t.mine)}</div>
            <div class="s">с отрядом: ${n(t.squad)}</div>
          </div>`).join('')}
        </div>
        <div class="row" style="margin-top:16px">
          <div class="stat"><div class="v">${n(s.best)}</div><div class="k">${icon('trophy')} лучший матч</div></div>
          <div class="stat"><div class="v">${n1(s.perMatch)}</div><div class="k">${icon('target')} в среднем за матч</div></div>
        </div>
      </div>`;
  };
  await draw();
  timer = setInterval(() => draw().catch(() => {}), 5000);
}

// ---------------------------------------------------------------- charts (tiny, offline)

function niceStep(max) {
  const raw = max / 5;
  const p = Math.pow(10, Math.floor(Math.log10(raw)));
  const m = raw / p;
  return (m <= 1 ? 1 : m <= 2 ? 2 : m <= 5 ? 5 : 10) * p;
}
const shortDate = (d) => { const [, m, day] = d.split('-'); return `${day}.${m}`; };
const longDate = (d) => new Date(`${d}T12:00:00`).toLocaleDateString('ru-RU', { weekday: 'short', day: 'numeric', month: 'long' });

function axes(svgParts, W, H, L, R, T, B, top, step, y) {
  for (let v = 0; v <= top; v += step) {
    svgParts.push(`<g class="grid"><line x1="${L}" x2="${W - R}" y1="${y(v)}" y2="${y(v)}"/></g><g class="axis"><text x="${L - 8}" y="${y(v) + 6}" text-anchor="end">${n(v)}</text></g>`);
  }
}

function barChart(el, rows) {
  const W = 1000, H = 280, L = 60, R = 30, T = 14, B = 40;
  const max = Math.max(1, ...rows.map((r) => r.squad));
  const step = niceStep(max), top = Math.ceil(max / step) * step;
  const bw = (W - L - R) / Math.max(rows.length, 1);
  const y = (v) => T + (H - T - B) * (1 - v / top);
  const parts = [`<svg viewBox="0 0 ${W} ${H}" role="img"><defs>
    <linearGradient id="gMe" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#ffe259"/><stop offset="1" stop-color="#f39a2c"/></linearGradient>
    <linearGradient id="gSq" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#5fd0ff" stop-opacity=".75"/><stop offset="1" stop-color="#1a3fa6" stop-opacity=".75"/></linearGradient></defs>`];
  axes(parts, W, H, L, R, T, B, top, step, y);
  rows.forEach((r, i) => {
    const x = L + i * bw, w = Math.max(3, bw * 0.72);
    parts.push(`<rect x="${x + (bw - w) / 2}" y="${y(r.squad)}" width="${w}" height="${y(0) - y(r.squad)}" fill="url(#gSq)" stroke="#fff6" stroke-width="2" data-i="${i}"/>`);
    parts.push(`<rect x="${x + (bw - w) / 2 + w * 0.17}" y="${y(r.mine)}" width="${w * 0.66}" height="${y(0) - y(r.mine)}" fill="url(#gMe)" stroke="#fff" stroke-width="2" data-i="${i}"/>`);
    if (rows.length <= 40 || i % Math.ceil(rows.length / 30) === 0) parts.push(`<g class="axis"><text x="${x + bw / 2}" y="${H - 12}" text-anchor="middle">${esc(shortDate(r.date))}</text></g>`);
  });
  parts.push('</svg>');
  el.innerHTML = `<div class="legend"><span><i style="background:#ffd84a"></i>мои</span><span><i style="background:#2f86ea"></i>с отрядом</span></div>${parts.join('')}`;
  el.querySelectorAll('rect[data-i]').forEach((rect) => {
    rect.addEventListener('mousemove', (e) => {
      const r = rows[+rect.dataset.i];
      showTip(e, `<b>${esc(longDate(r.date))}</b><br>мои: ${n(r.mine)}<br>с отрядом: ${n(r.squad)}<br>матчей: ${n(r.matches)}`);
    });
    rect.addEventListener('mouseleave', hideTip);
  });
}

function lineChart(el, rows) {
  const series = [{ key: 'cumSquad', color: '#4fd6ff', label: 'с отрядом' }, { key: 'cumMine', color: '#ffd84a', label: 'мои' }];
  const W = 1000, H = 280, L = 70, R = 34, T = 14, B = 40;
  const max = Math.max(1, ...rows.flatMap((r) => series.map((s) => r[s.key])));
  const step = niceStep(max), top = Math.ceil(max / step) * step;
  const x = (i) => L + (W - L - R) * (rows.length <= 1 ? 0.5 : i / (rows.length - 1));
  const y = (v) => T + (H - T - B) * (1 - v / top);
  const parts = [`<svg viewBox="0 0 ${W} ${H}" role="img">`];
  axes(parts, W, H, L, R, T, B, top, step, y);
  rows.forEach((r, i) => { if (rows.length <= 40 || i % Math.ceil(rows.length / 30) === 0) parts.push(`<g class="axis"><text x="${x(i)}" y="${H - 12}" text-anchor="middle">${esc(shortDate(r.date))}</text></g>`); });
  for (const s of series) {
    const pts = rows.map((r, i) => `${x(i)},${y(r[s.key])}`).join(' ');
    parts.push(`<polyline points="${pts}" fill="none" stroke="#0008" stroke-width="9" stroke-linejoin="round"/><polyline points="${pts}" fill="none" stroke="${s.color}" stroke-width="5" stroke-linejoin="round"/>`);
    rows.forEach((r, i) => parts.push(`<circle cx="${x(i)}" cy="${y(r[s.key])}" r="7" fill="${s.color}" stroke="#fff" stroke-width="2.5" data-i="${i}"/>`));
  }
  parts.push('</svg>');
  el.innerHTML = `<div class="legend">${series.map((s) => `<span><i style="background:${s.color}"></i>${s.label}</span>`).join('')}</div>${parts.join('')}`;
  el.querySelectorAll('circle').forEach((c) => {
    c.addEventListener('mousemove', (e) => {
      const r = rows[+c.dataset.i];
      showTip(e, `<b>${esc(longDate(r.date))}</b><br>${series.map((s) => `${s.label}: ${n(r[s.key])}`).join('<br>')}`);
    });
    c.addEventListener('mouseleave', hideTip);
  });
}

// ---------------------------------------------------------------- history

const placeHtml = (p) => (p == null ? '<span class="muted">—</span>' : p === 1 ? `<span class="crown">${icon('crown')} #1</span>` : `#${p}`);

async function renderHistory() {
  const [daily, matches] = await Promise.all([api('/api/daily'), api('/api/matches')]);
  const counted = matches.filter((m) => m.status === 'counted');
  const skipped = matches.filter((m) => m.status !== 'counted');
  view.innerHTML = `
    <h1>${icon('list')} История</h1>
    <div class="grid g2">
      <div class="card chart"><h2>${icon('calendar')} Сундуки по дням</h2><div id="c1"></div></div>
      <div class="card chart"><h2>${icon('trophy')} Нарастающий итог</h2><div id="c2"></div></div>
    </div>
    <div class="card" style="margin-top:18px"><h2>${icon('list')} Матчи <span class="muted">(${n(counted.length)})</span></h2>
      ${tableHtml('tMatches', [
        { label: 'Дата', value: (m) => m.startedUtc, html: (m) => dt(m.startedUtc) },
        { label: 'Режим', value: (m) => m.mode, html: (m) => esc(m.mode) },
        { label: 'Длительность', num: true, value: (m) => m.durationMin, html: (m) => `${n1(m.durationMin)} мин` },
        { label: 'Место', num: true, value: (m) => (m.placement == null ? null : -m.placement), html: (m) => placeHtml(m.placement) },
        { label: 'Мои', num: true, cls: 'big gold', value: (m) => m.mine, html: (m) => n(m.mine) },
        { label: 'Отряд', num: true, cls: 'big sea', value: (m) => m.squad, html: (m) => n(m.squad) },
      ], counted, { empty: 'Матчей пока нет', sort: { i: 0, dir: 'desc' } })}
    </div>
    ${skipped.length ? `<div class="card" style="margin-top:18px"><h2>${icon('warn')} Пропущено <span class="muted">(${n(skipped.length)})</span></h2>
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
  view.innerHTML = `
    <h1>${icon('flame')} Эффективность фарма</h1>
    <div class="grid g4">
      <div class="tile t-legend stat"><div class="v">${n1(o.cpm)}</div><div class="k">${icon('flame')} сундуков в минуту</div></div>
      <div class="tile t-rare stat"><div class="v">${n1(o.perMatch)}</div><div class="k">${icon('target')} сундуков за матч в среднем</div></div>
      <div class="tile t-epic stat"><div class="v">${n1(o.beforeDeath)}</div><div class="k">${icon('skull')} до первой смерти в среднем</div></div>
      <div class="tile t-uncommon stat"><div class="v">${n(o.best)}</div><div class="k">${icon('trophy')} лучший матч</div></div>
    </div>
    <div class="card" style="margin-top:18px"><h2>${icon('glider')} Рейтинг мест высадки</h2>
      ${tableHtml('tRank', [
        { label: 'Место', value: (r) => r.name, html: (r) => `<span class="place">${r.isPoi ? icon('pin') : ''}${esc(r.name)}</span>` },
        { label: 'Матчей', num: true, value: (r) => r.matches, html: (r) => n(r.matches) },
        { label: 'В минуту', num: true, cls: 'big gold', value: (r) => r.cpm, html: (r) => n1(r.cpm) },
        { label: 'За матч', num: true, value: (r) => r.avgMine, html: (r) => n1(r.avgMine) },
        { label: 'До смерти', num: true, value: (r) => r.avgBeforeDeath, html: (r) => n1(r.avgBeforeDeath) },
        { label: 'Лучший', num: true, value: (r) => r.best, html: (r) => n(r.best) },
      ], e.ranking, { sort: { i: 2, dir: 'desc' } })}
    </div>
    <div class="card" style="margin-top:18px"><h2>${icon('list')} По матчам</h2>
      ${tableHtml('tEffMatches', [
        { label: 'Дата', value: (m) => m.startedUtc, html: (m) => dt(m.startedUtc) },
        { label: 'Режим', value: (m) => m.mode, html: (m) => esc(m.mode) },
        { label: 'Высадка', value: (m) => m.place, html: (m) => esc(m.place) },
        { label: 'Активно', num: true, value: (m) => m.minutes, html: (m) => `${n1(m.minutes)} мин` },
        { label: 'Мои', num: true, cls: 'big gold', value: (m) => m.mine, html: (m) => n(m.mine) },
        { label: 'До смерти', num: true, value: (m) => m.beforeDeath, html: (m) => n(m.beforeDeath) },
        { label: 'В минуту', num: true, value: (m) => m.cpm, html: (m) => n1(m.cpm) },
      ], [...e.matches], { sort: { i: 0, dir: 'desc' } })}
    </div>`;
  bindTables(view);
}

// ---------------------------------------------------------------- map

async function renderMap() {
  const [matches, status, calib] = await Promise.all([api('/api/matches'), api('/api/status'), api('/api/map/calibration')]);
  const counted = matches.filter((m) => m.status === 'counted');
  view.innerHTML = `
    <h1>${icon('map')} Карта</h1>
    <div class="toolbar">
      <select id="mMatch"><option value="">Все матчи</option>${counted.map((m) => `<option value="${esc(m.id)}">${dt(m.startedUtc)} · ${esc(m.mode)} · ${n(m.mine)}</option>`).join('')}</select>
      <label class="chk"><input type="checkbox" id="lMe" checked><span class="dot" style="background:var(--me)"></span> открыто мной</label>
      <label class="chk"><input type="checkbox" id="lSquad" checked><span class="dot" style="background:var(--squad)"></span> открыто отрядом</label>
      <label class="chk"><input type="checkbox" id="lHeat"> тепловая карта (всё время)</label>
    </div>
    <div class="mapbox" id="mapbox"><canvas id="mapc" width="1200" height="1200"></canvas>
      ${status.hasMap ? '<div class="credit">Карта: Fortnite © Epic Games</div>' : ''}</div>`;

  const mv = new MapView($('#mapc'), status.hasMap, calib);
  current = mv;
  const reload = async () => {
    const id = $('#mMatch').value;
    mv.points = await api(`/api/map/points${id ? `?match=${encodeURIComponent(id)}` : ''}`);
    if ($('#lHeat').checked) mv.heatPoints = await api('/api/map/points');
    mv.draw();
  };
  const sync = () => {
    mv.layers = { me: $('#lMe').checked, squad: $('#lSquad').checked, heat: $('#lHeat').checked };
    reload();
  };
  ['mMatch', 'lMe', 'lSquad', 'lHeat'].forEach((id) => $(`#${id}`).addEventListener('change', sync));
  await mv.ready;
  sync();
}

/** Complex numbers for the 2-point similarity transform world (X + iY) → image pixel (px + i·py). */
const C = {
  sub: (a, b) => [a[0] - b[0], a[1] - b[1]],
  add: (a, b) => [a[0] + b[0], a[1] + b[1]],
  mul: (a, b) => [a[0] * b[0] - a[1] * b[1], a[0] * b[1] + a[1] * b[0]],
  div: (a, b) => { const d = b[0] * b[0] + b[1] * b[1]; return [(a[0] * b[0] + a[1] * b[1]) / d, (a[1] * b[0] - a[0] * b[1]) / d]; },
};

class MapView {
  constructor(canvas, hasMap, calib) {
    this.c = canvas;
    this.ctx = canvas.getContext('2d');
    this.points = [];
    this.heatPoints = [];
    this.layers = { me: true, squad: true, heat: false };
    this.img = null;
    this.ready = new Promise((resolve) => {
      if (!hasMap) { resolve(); return; }
      const img = new Image();
      img.onload = () => { this.img = img; this.c.width = img.naturalWidth * 2; this.c.height = img.naturalHeight * 2; resolve(); };
      img.onerror = () => resolve();
      img.src = '/map.png';
    }).then(() => {
      this.k = this.img ? 2 : 1; // canvas is drawn at 2x the map image for sharp markers
      const k = calib && calib.w1 ? calib : { w1: { x: 0, y: 0 }, p1: { x: 600, y: 600 }, w2: { x: 100000, y: 0 }, p2: { x: 1150, y: 600 } };
      const w1 = [k.w1.x, k.w1.y], w2 = [k.w2.x, k.w2.y], p1 = [k.p1.x, k.p1.y], p2 = [k.p2.x, k.p2.y];
      this.a = C.div(C.sub(p2, p1), C.sub(w2, w1));
      this.b = C.sub(p1, C.mul(this.a, w1));
    });
  }

  /** World → canvas pixels (calibration is stored in map-image pixels). */
  toPx(x, y) { const p = C.add(C.mul(this.a, [x, y]), this.b); return { x: p[0] * this.k, y: p[1] * this.k }; }

  draw() {
    const { ctx, c } = this;
    ctx.clearRect(0, 0, c.width, c.height);
    if (this.img) ctx.drawImage(this.img, 0, 0, c.width, c.height);
    else this.drawGrid();
    const scale = c.width / 1200;
    if (this.layers.heat && this.heatPoints.length) {
      this.drawHeat(this.heatPoints.filter((p) => this.visible(p)));
      return;
    }
    const pts = [...this.points].sort((a, b) => (a.by === 'me') - (b.by === 'me')); // mine on top
    for (const p of pts) {
      if (!this.visible(p)) continue;
      const q = this.toPx(p.x, p.y);
      const r = (p.by === 'me' ? 5.5 : 4.5) * scale;
      ctx.fillStyle = p.by === 'me' ? '#ffd84a' : '#4fd6ff';
      ctx.strokeStyle = '#1b1035'; ctx.lineWidth = 2 * scale;
      ctx.beginPath(); ctx.arc(q.x, q.y, r, 0, Math.PI * 2); ctx.fill(); ctx.stroke();
    }
  }

  visible(p) {
    if (p.by === 'me' && !this.layers.me) return false;
    if (p.by !== 'me' && !this.layers.squad) return false;
    return true;
  }

  drawGrid() {
    const { ctx, c } = this;
    ctx.fillStyle = '#0a2a5a'; ctx.fillRect(0, 0, c.width, c.height);
    ctx.strokeStyle = 'rgba(255,255,255,.08)'; ctx.lineWidth = 1;
    for (let i = 0; i <= 10; i++) {
      const v = (i / 10) * c.width;
      ctx.beginPath(); ctx.moveTo(v, 0); ctx.lineTo(v, c.height); ctx.moveTo(0, v); ctx.lineTo(c.width, v); ctx.stroke();
    }
  }

  drawHeat(pts) {
    const { c } = this;
    const off = document.createElement('canvas');
    off.width = c.width; off.height = c.height;
    const o = off.getContext('2d');
    const r = 22 * (c.width / 1200);
    o.globalCompositeOperation = 'lighter';
    for (const p of pts) {
      const q = this.toPx(p.x, p.y);
      const g = o.createRadialGradient(q.x, q.y, 0, q.x, q.y, r);
      g.addColorStop(0, 'rgba(0,0,0,0.22)'); g.addColorStop(1, 'rgba(0,0,0,0)');
      o.fillStyle = g; o.fillRect(q.x - r, q.y - r, 2 * r, 2 * r);
    }
    const data = o.getImageData(0, 0, off.width, off.height);
    const d = data.data;
    for (let i = 0; i < d.length; i += 4) {
      const a = d[i + 3] / 255;
      if (a <= 0) continue;
      const t = Math.min(1, a * 1.6);
      const col = t < 0.5 ? lerp([79, 214, 255], [255, 216, 74], t * 2) : lerp([255, 216, 74], [243, 80, 40], (t - 0.5) * 2);
      d[i] = col[0]; d[i + 1] = col[1]; d[i + 2] = col[2]; d[i + 3] = Math.min(235, 70 + a * 420);
    }
    o.putImageData(data, 0, 0);
    this.ctx.drawImage(off, 0, 0);
  }
}

const lerp = (a, b, t) => a.map((v, i) => Math.round(v + (b[i] - v) * t));

// ---------------------------------------------------------------- updates (program state)

const size = (bytes) => (bytes >= 1073741824
  ? `${new Intl.NumberFormat('ru-RU', { maximumFractionDigits: 1 }).format(bytes / 1073741824)} ГБ`
  : `${Math.round(bytes / 1048576)} МБ`);

async function renderUpdates() {
  const draw = async () => {
    const s = await api('/api/status');
    const p = s.progress;
    setVersion(s.version);
    view.innerHTML = `
      <h1>${icon('download')} Обновления</h1>
      ${warnBanner(s.recordingWarning)}
      <div class="grid g2">
        <div class="tile t-rare"><h2>${icon('download')} Программа</h2><div class="kv">
          <div>Версия</div><div><b style="font-family:var(--display);font-size:22px">v${esc(s.version)}</b></div>
          <div>Обновления</div><div>${esc(s.update.status)}</div>
          <div>Автозапуск</div><div>${s.autostart ? 'включён' : 'выключен'}</div>
        </div>
        <div style="margin-top:14px"><button class="primary" id="upd"><span>${icon('refresh')} Проверить обновления</span></button></div></div>
        <div class="card"><h2>${icon('chest')} Реплеи</h2><div class="kv">
          <div>В папке Fortnite</div><div>${s.demosFound ? n(s.replayFiles) : 'папки нет'}</div>
          <div>Сохранено копий</div><div>${n(s.backups)} (${size(s.backupBytes)})</div>
          <div>Последний</div><div>${s.newestReplayUtc ? `${dt(s.newestReplayUtc)} (${ago(s.newestReplayUtc)})` : '—'}</div>
          <div>Fortnite</div><div>${s.fortniteRunning ? 'запущен — матч посчитается после окончания' : 'не запущен'}</div>
          ${p.busy ? `<div>Считаю</div><div>${n(p.done)} из ${n(p.total || p.pending)}</div>` : ''}
        </div>
        <div style="margin-top:14px"><button id="recount"><span>${icon('refresh')} Пересчитать всё</span></button></div></div>
      </div>
      <div class="card" style="margin-top:18px"><h2>${icon('gear')} Как включить запись повторов</h2>
        <ol class="steps">
          <li>В Fortnite откройте меню → <b>Настройки</b>.</li>
          <li>Вкладка <b>«Игра»</b> (шестерёнка) → раздел <b>«Повторы»</b>.</li>
          <li>Включите <b>запись повторов</b> для Королевской битвы.</li>
          <li>Играйте — после каждого матча счётчик обновится сам.</li>
        </ol>
      </div>`;
    $('#recount').addEventListener('click', async (e) => { e.currentTarget.disabled = true; await post('/api/recount'); setTimeout(() => draw().catch(() => {}), 1500); });
    $('#upd').addEventListener('click', async (e) => { const b = e.currentTarget; b.disabled = true; b.querySelector('span').lastChild.textContent = ' Проверяю…'; await post('/api/update/check'); draw().catch(() => {}); });
  };
  await draw();
  timer = setInterval(() => draw().catch(() => {}), 5000);
}

route();
