'use strict';

// ---------------------------------------------------------------- helpers

const $ = (sel, root = document) => root.querySelector(sel);
const esc = (s) => String(s ?? '').replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
const nf = new Intl.NumberFormat('ru-RU');
const n = (v) => nf.format(v ?? 0);
const n1 = (v) => (v == null ? '—' : new Intl.NumberFormat('ru-RU', { maximumFractionDigits: 2 }).format(v));

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

function plural(n, one, few, many) {
  const m10 = n % 10, m100 = n % 100;
  if (m10 === 1 && m100 !== 11) return one;
  if (m10 >= 2 && m10 <= 4 && (m100 < 12 || m100 > 14)) return few;
  return many;
}
const chests = (k) => plural(k, 'сундук', 'сундука', 'сундуков');
const matchesW = (k) => plural(k, 'матч', 'матча', 'матчей');

function dt(iso, withYear = false) {
  if (!iso) return '—';
  const d = new Date(iso);
  return d.toLocaleString('ru-RU', { day: '2-digit', month: '2-digit', ...(withYear ? { year: 'numeric' } : {}), hour: '2-digit', minute: '2-digit' });
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

// ---------------------------------------------------------------- router

const view = $('#view');
let timer = null;
let current = null;

const routes = {
  '': renderHome,
  history: renderHistory,
  efficiency: renderEfficiency,
  map: renderMap,
  status: renderStatus,
};

function route() {
  const name = location.hash.replace(/^#\/?/, '').split('?')[0];
  const fn = routes[name] ?? renderHome;
  document.querySelectorAll('#tabs a').forEach((a) => a.classList.toggle('active', (a.dataset.tab === 'home' ? '' : a.dataset.tab) === name));
  if (timer) { clearInterval(timer); timer = null; }
  if (current?.dispose) current.dispose();
  current = null;
  fn().catch((e) => { view.innerHTML = `<div class="banner">Не удалось загрузить данные: ${esc(e.message)}</div>`; });
}
window.addEventListener('hashchange', route);

// ---------------------------------------------------------------- home

async function renderHome() {
  const draw = async () => {
    const [s, st] = await Promise.all([api('/api/summary'), api('/api/status')]);
    $('#ver').textContent = `версия ${s.version}`;
    const p = s.progress;
    const maxType = Math.max(1, ...s.byType.map((t) => t.squad));
    view.innerHTML = `
      ${st.recordingWarning ? `<div class="banner"><div>⚠</div><div><b>${esc(st.recordingWarning)}</b><br>Как включить: вкладка <a href="#/status">«Состояние»</a>.</div></div>` : ''}
      ${p.busy ? `<div class="banner info"><div>⏳</div><div style="flex:1"><b>Считаю реплеи: ${n(p.done)} из ${n(p.total || p.pending)}</b>${p.current ? ` <span class="muted">· ${esc(p.current)}</span>` : ''}
        <div class="progress"><div style="width:${p.total ? Math.round(100 * p.done / p.total) : 5}%"></div></div></div></div>` : ''}
      <div class="grid g2">
        <div class="card hero">
          <div class="label">Мои сундуки</div>
          <div class="big">${n(s.mine)}</div>
          <div class="sub">открыто мной + сломано мной неоткрытыми · ${n(s.matches)} ${matchesW(s.matches)}</div>
        </div>
        <div class="card hero squad">
          <div class="label">Мои + отряд <span class="pill sea">не меньше</span></div>
          <div class="big">${n(s.squad)}</div>
          <div class="sub">далёких тиммейтов реплей может не видеть, поэтому реальное число может быть больше</div>
        </div>
      </div>
      <div class="grid g3" style="margin-top:16px">
        <div class="card"><h3>Сегодня</h3><div class="row">
          <div class="stat"><div class="v gold">${n(s.today.mine)}</div><div class="k">мои</div></div>
          <div class="stat"><div class="v sea">${n(s.today.squad)}</div><div class="k">с отрядом</div></div>
          <div class="stat"><div class="v">${n(s.today.matches)}</div><div class="k">${matchesW(s.today.matches)}</div></div></div></div>
        <div class="card"><h3>Текущая сессия</h3>${s.session.matches ? `<div class="row">
          <div class="stat"><div class="v gold">${n(s.session.mine)}</div><div class="k">мои</div></div>
          <div class="stat"><div class="v sea">${n(s.session.squad)}</div><div class="k">с отрядом</div></div>
          <div class="stat"><div class="v">${n(s.session.matches)}</div><div class="k">${matchesW(s.session.matches)}</div></div></div>`
          : '<div class="muted">Сессия не идёт: последний матч был больше 45 минут назад.</div>'}</div>
        <div class="card"><h3>Последний разобранный матч</h3>${s.lastMatch ? `
          <div class="stat"><div class="v">+${n(s.lastMatch.mine)} <small>мои · +${n(s.lastMatch.squad)} с отрядом</small></div>
          <div class="k">${esc(s.lastMatch.mode)} · закончился ${dt(s.lastMatch.endedUtc)} (${ago(s.lastMatch.endedUtc)})</div></div>` : '<div class="muted">Пока нет матчей.</div>'}</div>
      </div>
      <div class="grid g2" style="margin-top:16px">
        <div class="card"><h2>По типам сундуков</h2>
          <div class="legend"><span><i style="background:var(--gold)"></i>мои</span><span><i style="background:rgba(63,184,175,.45)"></i>с отрядом</span></div>
          <div class="bars">${s.byType.map((t) => `
            <div class="bar"><div>${esc(t.label)}</div>
              <div class="track"><div class="fill squad" style="width:${100 * t.squad / maxType}%"></div><div class="fill me" style="width:${100 * t.mine / maxType}%"></div></div>
              <div class="num"><b>${n(t.mine)}</b> / ${n(t.squad)}</div></div>`).join('')}
          </div></div>
        <div class="card"><h2>Как засчитано</h2>
          <div class="kv">
            <div>Сломано неоткрытыми</div><div><b class="rust">${n(s.broken.mine)}</b> мои · ${n(s.broken.squad)} с отрядом <span class="muted">(игра считает такой сундук открытым)</span></div>
            <div>По эвристике</div><div><b>${n(s.heuristic.mine)}</b> мои · ${n(s.heuristic.squad)} с отрядом <span class="muted">— кто сломал, в реплее не записано; засчитано тому, кто стоял ближе 15 м</span></div>
            <div>Пропущено матчей</div><div>${n(s.skipped)} <span class="muted">(другие режимы и сезоны — список в «Истории»)</span></div>
          </div></div>
      </div>`;
  };
  await draw();
  timer = setInterval(() => draw().catch(() => {}), 5000);
}

// ---------------------------------------------------------------- charts (tiny, offline)

function barChart(el, rows, { keyA, keyB, colorA, colorB, labelA, labelB }) {
  const W = 1000, H = 260, L = 44, R = 12, T = 12, B = 34;
  const max = Math.max(1, ...rows.map((r) => Math.max(r[keyA], r[keyB] ?? 0)));
  const step = niceStep(max);
  const top = Math.ceil(max / step) * step;
  const bw = (W - L - R) / Math.max(rows.length, 1);
  const y = (v) => T + (H - T - B) * (1 - v / top);
  let svg = `<svg viewBox="0 0 ${W} ${H}" role="img">`;
  for (let v = 0; v <= top; v += step) {
    svg += `<g class="grid"><line x1="${L}" x2="${W - R}" y1="${y(v)}" y2="${y(v)}"/></g><g class="axis"><text x="${L - 6}" y="${y(v) + 4}" text-anchor="end">${n(v)}</text></g>`;
  }
  rows.forEach((r, i) => {
    const x = L + i * bw;
    const w = Math.max(2, bw * 0.7);
    if (keyB) svg += `<rect x="${x + (bw - w) / 2}" y="${y(r[keyB])}" width="${w}" height="${y(0) - y(r[keyB])}" fill="${colorB}" rx="3" data-i="${i}"/>`;
    svg += `<rect x="${x + (bw - w) / 2 + w * 0.18}" y="${y(r[keyA])}" width="${w * 0.64}" height="${y(0) - y(r[keyA])}" fill="${colorA}" rx="2" data-i="${i}"/>`;
    if (rows.length <= 40 || i % Math.ceil(rows.length / 30) === 0) {
      svg += `<g class="axis"><text x="${x + bw / 2}" y="${H - 12}" text-anchor="middle">${esc(shortDate(r.date))}</text></g>`;
    }
  });
  svg += '</svg>';
  el.innerHTML = `<div class="legend"><span><i style="background:${colorA}"></i>${labelA}</span>${keyB ? `<span><i style="background:${colorB}"></i>${labelB}</span>` : ''}</div>${svg}`;
  el.querySelectorAll('rect').forEach((rect) => {
    rect.addEventListener('mousemove', (e) => {
      const r = rows[+rect.dataset.i];
      showTip(e, `<b>${esc(longDate(r.date))}</b><br>${labelA}: ${n(r[keyA])}${keyB ? `<br>${labelB}: ${n(r[keyB])}` : ''}<br>матчей: ${n(r.matches)}`);
    });
    rect.addEventListener('mouseleave', hideTip);
  });
}

function lineChart(el, rows, series) {
  const W = 1000, H = 260, L = 52, R = 12, T = 12, B = 34;
  const max = Math.max(1, ...rows.flatMap((r) => series.map((s) => r[s.key])));
  const step = niceStep(max);
  const top = Math.ceil(max / step) * step;
  const x = (i) => L + (W - L - R) * (rows.length <= 1 ? 0.5 : i / (rows.length - 1));
  const y = (v) => T + (H - T - B) * (1 - v / top);
  let svg = `<svg viewBox="0 0 ${W} ${H}" role="img">`;
  for (let v = 0; v <= top; v += step) {
    svg += `<g class="grid"><line x1="${L}" x2="${W - R}" y1="${y(v)}" y2="${y(v)}"/></g><g class="axis"><text x="${L - 6}" y="${y(v) + 4}" text-anchor="end">${n(v)}</text></g>`;
  }
  rows.forEach((r, i) => {
    if (rows.length <= 40 || i % Math.ceil(rows.length / 30) === 0) svg += `<g class="axis"><text x="${x(i)}" y="${H - 12}" text-anchor="middle">${esc(shortDate(r.date))}</text></g>`;
  });
  for (const s of series) {
    const pts = rows.map((r, i) => `${x(i)},${y(r[s.key])}`).join(' ');
    svg += `<polyline points="${pts}" fill="none" stroke="${s.color}" stroke-width="3" stroke-linejoin="round"/>`;
    rows.forEach((r, i) => { svg += `<circle cx="${x(i)}" cy="${y(r[s.key])}" r="4" fill="${s.color}" data-i="${i}"/>`; });
  }
  svg += '</svg>';
  el.innerHTML = `<div class="legend">${series.map((s) => `<span><i style="background:${s.color}"></i>${s.label}</span>`).join('')}</div>${svg}`;
  el.querySelectorAll('circle').forEach((c) => {
    c.addEventListener('mousemove', (e) => {
      const r = rows[+c.dataset.i];
      showTip(e, `<b>${esc(longDate(r.date))}</b><br>${series.map((s) => `${s.label}: ${n(r[s.key])}`).join('<br>')}`);
    });
    c.addEventListener('mouseleave', hideTip);
  });
}

function niceStep(max) {
  const raw = max / 5;
  const p = Math.pow(10, Math.floor(Math.log10(raw)));
  const m = raw / p;
  return (m <= 1 ? 1 : m <= 2 ? 2 : m <= 5 ? 5 : 10) * p;
}
const shortDate = (d) => { const [, m, day] = d.split('-'); return `${day}.${m}`; };
const longDate = (d) => new Date(`${d}T12:00:00`).toLocaleDateString('ru-RU', { weekday: 'short', day: 'numeric', month: 'long' });

// ---------------------------------------------------------------- history

async function renderHistory() {
  const [daily, matches] = await Promise.all([api('/api/daily'), api('/api/matches')]);
  const counted = matches.filter((m) => m.status === 'counted');
  const skipped = matches.filter((m) => m.status !== 'counted');
  view.innerHTML = `
    <h1>История</h1>
    <div class="grid g2">
      <div class="card chart"><h2>Сундуки по дням</h2><div id="c1"></div></div>
      <div class="card chart"><h2>Нарастающий итог</h2><div id="c2"></div></div>
    </div>
    <div class="card" style="margin-top:16px"><h2>Матчи <span class="muted" style="font-size:14px">(${n(counted.length)})</span></h2>
      <div class="tablewrap"><table>
        <thead><tr><th>Дата</th><th>Режим</th><th class="n">Длительность</th><th class="n">Место</th><th class="n">Мои</th><th class="n">Отряд</th><th class="n">из них сломано</th><th class="n">эвристика</th><th>Замечания</th></tr></thead>
        <tbody>${counted.map((m) => `<tr>
          <td>${dt(m.startedUtc)}</td><td>${esc(m.mode)}</td><td class="n">${n1(m.durationMin)} мин</td><td class="n">${m.placement ? `#${m.placement}` : '—'}</td>
          <td class="n gold"><b>${n(m.mine)}</b></td><td class="n sea">${n(m.squad)}</td>
          <td class="n">${m.mineBroken || m.squadBroken ? `${n(m.mineBroken)} / ${n(m.squadBroken)}` : '—'}</td>
          <td class="n">${m.mineHeuristic || m.squadHeuristic ? `${n(m.mineHeuristic)} / ${n(m.squadHeuristic)}` : '—'}</td>
          <td class="muted">${m.unattributed ? `${n(m.unattributed)} без атрибуции` : ''}</td></tr>`).join('') || '<tr><td colspan="9" class="empty">Матчей пока нет</td></tr>'}
        </tbody></table></div>
      <p class="muted" style="font-size:13px">«из них сломано» и «эвристика» — мои / с отрядом. Ошибки разбора — на вкладке <a href="#/status">«Состояние»</a>.</p>
    </div>
    <div class="card" style="margin-top:16px"><h2>Пропущено <span class="muted" style="font-size:14px">(${n(skipped.length)})</span></h2>
      ${skipped.length ? `<div class="tablewrap"><table><thead><tr><th>Дата</th><th>Режим</th><th>Билд</th><th>Причина</th></tr></thead><tbody>
        ${skipped.map((m) => `<tr><td>${dt(m.startedUtc, true)}</td><td>${esc(m.playlist || '—')}</td><td>${esc(m.build || '')}</td><td class="wrap">${esc(m.skipReason)}</td></tr>`).join('')}
      </tbody></table></div>` : '<div class="muted">Ничего не пропущено.</div>'}
    </div>`;
  if (daily.length) {
    barChart($('#c1'), daily, { keyA: 'mine', keyB: 'squad', colorA: 'var(--gold)', colorB: 'rgba(63,184,175,.45)', labelA: 'мои', labelB: 'с отрядом' });
    lineChart($('#c2'), daily, [{ key: 'cumSquad', color: 'var(--sea)', label: 'с отрядом' }, { key: 'cumMine', color: 'var(--gold)', label: 'мои' }]);
  } else {
    $('#c1').innerHTML = $('#c2').innerHTML = '<div class="empty">Нет данных</div>';
  }
}

// ---------------------------------------------------------------- efficiency

async function renderEfficiency() {
  const e = await api('/api/efficiency');
  const o = e.overall;
  view.innerHTML = `
    <h1>Эффективность фарма</h1>
    <div class="grid g4">
      <div class="card stat"><div class="v gold">${n1(o.cpm)}</div><div class="k">сундуков в минуту <span class="muted">(от приземления до смерти или конца матча)</span></div></div>
      <div class="card stat"><div class="v">${n1(o.perMatch)}</div><div class="k">сундуков за матч в среднем</div></div>
      <div class="card stat"><div class="v">${n1(o.beforeDeath)}</div><div class="k">до первой смерти в среднем</div></div>
      <div class="card stat"><div class="v">${n(o.best)}</div><div class="k">лучший матч</div></div>
    </div>
    <div class="card" style="margin-top:16px"><h2>Рейтинг мест высадки</h2>
      <p class="muted" style="margin-top:-4px;font-size:13px">Место высадки — первая точка приземления. Оно привязывается к ближайшему POI, иначе к району 300×300 м. Названия POI можно поменять на вкладке «Карта».</p>
      <div class="tablewrap"><table>
        <thead><tr><th>Место</th><th class="n">Матчей</th><th class="n">В минуту</th><th class="n">За матч</th><th class="n">До смерти</th><th class="n">Лучший</th></tr></thead>
        <tbody>${e.ranking.map((r) => `<tr><td>${r.isPoi ? '📍 ' : ''}${esc(r.name)}</td><td class="n">${n(r.matches)}</td><td class="n gold"><b>${n1(r.cpm)}</b></td>
          <td class="n">${n1(r.avgMine)}</td><td class="n">${n1(r.avgBeforeDeath)}</td><td class="n">${n(r.best)}</td></tr>`).join('') || '<tr><td colspan="6" class="empty">Нет данных</td></tr>'}
        </tbody></table></div></div>
    <div class="card" style="margin-top:16px"><h2>По матчам</h2>
      <div class="tablewrap"><table>
        <thead><tr><th>Дата</th><th>Режим</th><th>Высадка</th><th class="n">Активно</th><th class="n">Мои</th><th class="n">До смерти</th><th class="n">В минуту</th></tr></thead>
        <tbody>${e.matches.map((m) => `<tr><td>${dt(m.startedUtc)}</td><td>${esc(m.mode)}</td><td>${esc(m.place)}</td><td class="n">${n1(m.minutes)} мин</td>
          <td class="n gold">${n(m.mine)}</td><td class="n">${n(m.beforeDeath)}</td><td class="n">${n1(m.cpm)}</td></tr>`).join('')}
        </tbody></table></div></div>`;
}

// ---------------------------------------------------------------- map

async function renderMap() {
  const [matches, status, atlas, pois, calib] = await Promise.all([
    api('/api/matches'), api('/api/status'), api('/api/map/atlas'), api('/api/pois'), api('/api/map/calibration'),
  ]);
  const counted = matches.filter((m) => m.status === 'counted');
  view.innerHTML = `
    <h1>Карта</h1>
    <div class="toolbar">
      <select id="mMatch"><option value="">Все матчи</option>${counted.map((m) => `<option value="${esc(m.id)}">${dt(m.startedUtc)} · ${esc(m.mode)} · ${n(m.mine)}</option>`).join('')}</select>
      <label class="chk"><input type="checkbox" id="lMe" checked> <span class="gold">●</span> открыто мной</label>
      <label class="chk"><input type="checkbox" id="lSquad" checked> <span class="sea">●</span> открыто отрядом</label>
      <label class="chk"><input type="checkbox" id="lBroken" checked> <span class="rust">✕</span> сломано</label>
      <label class="chk"><input type="checkbox" id="lHeat"> тепловая карта (всё время)</label>
      <label class="chk"><input type="checkbox" id="lPoi" checked> POI</label>
      <div style="flex:1"></div>
      <button id="calBtn">Калибровка</button>
    </div>
    <div class="mapbox" id="mapbox"><canvas id="mapc" width="1200" height="1200"></canvas>
      <div class="credit">${status.hasMap ? 'Карта: Fortnite © Epic Games' : 'Карта не добавлена — показаны только точки'}</div>
      <div class="hint" id="mapHint" style="display:none"></div></div>
    <div class="card" style="margin-top:16px" id="poiCard"><h2>POI</h2>
      <p class="muted" style="font-size:13px;margin-top:-4px">Места с названиями используются в рейтинге высадок. Названия можно поправить.</p>
      <div class="tablewrap"><table><thead><tr><th>Название</th><th class="n">X, м</th><th class="n">Y, м</th><th></th></tr></thead><tbody>
      ${pois.map((p) => `<tr><td><input type="text" value="${esc(p.name)}" data-id="${esc(p.id)}" style="width:260px"></td><td class="n">${n(Math.round(p.x / 100))}</td><td class="n">${n(Math.round(p.y / 100))}</td><td><button data-save="${esc(p.id)}">Сохранить</button></td></tr>`).join('') || '<tr><td colspan="4" class="empty">Список POI пуст</td></tr>'}
      </tbody></table></div></div>`;

  view.querySelectorAll('button[data-save]').forEach((b) => b.addEventListener('click', async () => {
    const input = view.querySelector(`input[data-id="${CSS.escape(b.dataset.save)}"]`);
    await post('/api/pois/rename', { id: b.dataset.save, name: input.value });
    b.textContent = 'Сохранено';
    setTimeout(() => { b.textContent = 'Сохранить'; }, 1500);
  }));

  const mv = new MapView($('#mapc'), status.hasMap, atlas, pois, calib);
  current = mv;
  const reload = async () => {
    const id = $('#mMatch').value;
    mv.points = await api(`/api/map/points${id ? `?match=${encodeURIComponent(id)}` : ''}`);
    if ($('#lHeat').checked) mv.heatPoints = await api('/api/map/points');
    mv.draw();
  };
  const sync = () => {
    mv.layers = { me: $('#lMe').checked, squad: $('#lSquad').checked, broken: $('#lBroken').checked, heat: $('#lHeat').checked, poi: $('#lPoi').checked };
    reload();
  };
  ['mMatch', 'lMe', 'lSquad', 'lBroken', 'lHeat', 'lPoi'].forEach((id) => $(`#${id}`).addEventListener('change', sync));
  $('#calBtn').addEventListener('click', () => {
    if (mv.calibrating) {
      mv.finishCalibration(true);
      $('#calBtn').textContent = 'Калибровка';
    } else {
      mv.startCalibration($('#mapHint'));
      $('#calBtn').textContent = 'Сохранить калибровку';
    }
  });
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
  constructor(canvas, hasMap, atlas, pois, calib) {
    this.c = canvas;
    this.ctx = canvas.getContext('2d');
    this.atlas = atlas;
    this.pois = pois;
    this.points = [];
    this.heatPoints = [];
    this.layers = { me: true, squad: true, broken: true, heat: false, poi: true };
    this.img = null;
    this.calibrating = false;
    this.ready = new Promise((resolve) => {
      if (!hasMap) { resolve(); return; }
      const img = new Image();
      img.onload = () => { this.img = img; this.c.width = img.naturalWidth; this.c.height = img.naturalHeight; resolve(); };
      img.onerror = () => resolve();
      img.src = '/map.png';
    });
    this.ready.then(() => {
      this.calib = calib && calib.w1 ? calib : this.autoFit();
      this.setTransform(this.calib);
    });
  }

  /** Initial guess: Unreal X points north (up), Y east (right); fit the known chests into the image. */
  autoFit() {
    const pts = this.atlas.length ? this.atlas : this.pois;
    if (!pts.length) return { w1: { x: 0, y: 0 }, p1: { x: this.c.width / 2, y: this.c.height / 2 }, w2: { x: 0, y: 100000 }, p2: { x: this.c.width / 2 + 300, y: this.c.height / 2 } };
    const xs = pts.map((p) => p.x), ys = pts.map((p) => p.y);
    const minX = Math.min(...xs), maxX = Math.max(...xs), minY = Math.min(...ys), maxY = Math.max(...ys);
    const pad = 0.08;
    const s = Math.min(this.c.width * (1 - 2 * pad) / Math.max(1, maxY - minY), this.c.height * (1 - 2 * pad) / Math.max(1, maxX - minX));
    const ox = (this.c.width - s * (maxY - minY)) / 2, oy = (this.c.height - s * (maxX - minX)) / 2;
    const w1 = { x: maxX, y: minY }, w2 = { x: minX, y: maxY };
    return { w1, p1: { x: ox, y: oy }, w2, p2: { x: ox + s * (maxY - minY), y: oy + s * (maxX - minX) } };
  }

  setTransform(k) {
    const w1 = [k.w1.x, k.w1.y], w2 = [k.w2.x, k.w2.y], p1 = [k.p1.x, k.p1.y], p2 = [k.p2.x, k.p2.y];
    this.a = C.div(C.sub(p2, p1), C.sub(w2, w1));
    this.b = C.sub(p1, C.mul(this.a, w1));
  }

  toPx(x, y) { const p = C.add(C.mul(this.a, [x, y]), this.b); return { x: p[0], y: p[1] }; }

  draw() {
    const { ctx, c } = this;
    ctx.clearRect(0, 0, c.width, c.height);
    if (this.img) ctx.drawImage(this.img, 0, 0);
    else this.drawGrid();
    const scale = c.width / 1200;

    if (this.calibrating || !this.img) {
      ctx.fillStyle = 'rgba(233,227,211,.35)';
      for (const p of this.atlas) { const q = this.toPx(p.x, p.y); ctx.fillRect(q.x - 1.5 * scale, q.y - 1.5 * scale, 3 * scale, 3 * scale); }
    }
    if (this.layers.heat && this.heatPoints.length) this.drawHeat(this.heatPoints.filter((p) => this.visible(p)));
    if (!this.layers.heat) {
      for (const p of this.points) {
        if (!this.visible(p)) continue;
        const q = this.toPx(p.x, p.y);
        const r = (p.by === 'me' ? 5 : 4) * scale;
        if (p.broken) {
          ctx.strokeStyle = '#d9694a'; ctx.lineWidth = 2.5 * scale;
          ctx.beginPath(); ctx.moveTo(q.x - r, q.y - r); ctx.lineTo(q.x + r, q.y + r); ctx.moveTo(q.x + r, q.y - r); ctx.lineTo(q.x - r, q.y + r); ctx.stroke();
        } else {
          ctx.fillStyle = p.by === 'me' ? '#e9b44c' : '#3fb8af';
          ctx.strokeStyle = 'rgba(0,0,0,.6)'; ctx.lineWidth = 1.2 * scale;
          ctx.beginPath(); ctx.arc(q.x, q.y, r, 0, Math.PI * 2); ctx.fill(); ctx.stroke();
        }
      }
    }
    if (this.layers.poi) {
      ctx.font = `${Math.round(13 * scale)}px Segoe UI, sans-serif`;
      ctx.textAlign = 'center';
      for (const p of this.pois) {
        const q = this.toPx(p.x, p.y);
        ctx.fillStyle = 'rgba(0,0,0,.55)';
        const w = ctx.measureText(p.name).width + 10 * scale;
        ctx.fillRect(q.x - w / 2, q.y - 16 * scale, w, 18 * scale);
        ctx.fillStyle = '#f6d58a';
        ctx.fillText(p.name, q.x, q.y - 3 * scale);
      }
    }
    if (this.calibrating) this.drawHandles();
  }

  visible(p) {
    if (p.broken && !this.layers.broken) return false;
    if (!p.broken && p.by === 'me' && !this.layers.me) return false;
    if (!p.broken && p.by !== 'me' && !this.layers.squad) return false;
    return true;
  }

  drawGrid() {
    const { ctx, c } = this;
    ctx.fillStyle = '#0b1a24'; ctx.fillRect(0, 0, c.width, c.height);
    ctx.strokeStyle = 'rgba(63,184,175,.10)'; ctx.lineWidth = 1;
    for (let i = 0; i <= 12; i++) {
      const v = (i / 12) * c.width;
      ctx.beginPath(); ctx.moveTo(v, 0); ctx.lineTo(v, c.height); ctx.moveTo(0, v); ctx.lineTo(c.width, v); ctx.stroke();
    }
  }

  drawHeat(pts) {
    const { c } = this;
    const off = document.createElement('canvas');
    off.width = c.width; off.height = c.height;
    const o = off.getContext('2d');
    const r = 18 * (c.width / 1200);
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
      // sea → gold → rust
      const col = t < 0.5 ? lerp([63, 184, 175], [233, 180, 76], t * 2) : lerp([233, 180, 76], [217, 105, 74], (t - 0.5) * 2);
      d[i] = col[0]; d[i + 1] = col[1]; d[i + 2] = col[2]; d[i + 3] = Math.min(230, 60 + a * 400);
    }
    o.putImageData(data, 0, 0);
    this.ctx.drawImage(off, 0, 0);
  }

  // ---- calibration: two handles pinned to two far-apart known chests; drag them onto the map.
  startCalibration(hint) {
    this.calibrating = true;
    this.hint = hint;
    const pts = this.atlas.length >= 2 ? this.atlas : this.pois;
    if (pts.length < 2) {
      hint.style.display = 'block';
      hint.textContent = 'Нужно хотя бы 2 известные точки: сыграйте матч и откройте сундуки.';
      return;
    }
    let a = pts[0], b = pts[1], best = -1;
    for (const p of pts) for (const q of [pts[0], ...pts.slice(-50)]) {
      const d = (p.x - q.x) ** 2 + (p.y - q.y) ** 2;
      if (d > best) { best = d; a = p; b = q; }
    }
    this.w1 = { x: a.x, y: a.y }; this.w2 = { x: b.x, y: b.y };
    this.h1 = this.toPx(a.x, a.y); this.h2 = this.toPx(b.x, b.y);
    hint.style.display = 'block';
    hint.innerHTML = 'Светлые точки — все известные сундуки. Перетащите <b class="gold">①</b> и <b class="sea">②</b> так, чтобы точки легли на сундуки и постройки карты, затем «Сохранить калибровку».';
    this.onDown = (e) => {
      const m = this.mouse(e);
      const d1 = Math.hypot(m.x - this.h1.x, m.y - this.h1.y), d2 = Math.hypot(m.x - this.h2.x, m.y - this.h2.y);
      const lim = 30 * (this.c.width / this.c.clientWidth);
      this.drag = d1 < lim ? 1 : d2 < lim ? 2 : 0;
    };
    this.onMove = (e) => {
      if (!this.drag) return;
      const m = this.mouse(e);
      if (this.drag === 1) this.h1 = m; else this.h2 = m;
      this.setTransform({ w1: this.w1, p1: this.h1, w2: this.w2, p2: this.h2 });
      this.draw();
    };
    this.onUp = () => { this.drag = 0; };
    this.c.addEventListener('mousedown', this.onDown);
    window.addEventListener('mousemove', this.onMove);
    window.addEventListener('mouseup', this.onUp);
    this.draw();
  }

  async finishCalibration(save) {
    this.calibrating = false;
    this.c.removeEventListener('mousedown', this.onDown);
    window.removeEventListener('mousemove', this.onMove);
    window.removeEventListener('mouseup', this.onUp);
    if (this.hint) this.hint.style.display = 'none';
    if (save && this.w1) {
      this.calib = { w1: this.w1, p1: this.h1, w2: this.w2, p2: this.h2 };
      await post('/api/map/calibration', this.calib);
    }
    this.draw();
  }

  drawHandles() {
    const { ctx } = this;
    const s = this.c.width / 1200;
    [[this.h1, '#e9b44c', '①'], [this.h2, '#3fb8af', '②']].forEach(([h, col, label]) => {
      if (!h) return;
      ctx.strokeStyle = col; ctx.lineWidth = 3 * s;
      ctx.beginPath(); ctx.arc(h.x, h.y, 14 * s, 0, Math.PI * 2); ctx.stroke();
      ctx.beginPath(); ctx.moveTo(h.x - 22 * s, h.y); ctx.lineTo(h.x + 22 * s, h.y); ctx.moveTo(h.x, h.y - 22 * s); ctx.lineTo(h.x, h.y + 22 * s); ctx.stroke();
      ctx.fillStyle = col; ctx.font = `bold ${Math.round(18 * s)}px Segoe UI`; ctx.fillText(label, h.x + 26 * s, h.y - 14 * s);
    });
  }

  mouse(e) {
    const r = this.c.getBoundingClientRect();
    return { x: (e.clientX - r.left) * (this.c.width / r.width), y: (e.clientY - r.top) * (this.c.height / r.height) };
  }

  dispose() { if (this.calibrating) this.finishCalibration(false); }
}

const lerp = (a, b, t) => a.map((v, i) => Math.round(v + (b[i] - v) * t));

// ---------------------------------------------------------------- status

async function renderStatus() {
  const draw = async () => {
    const s = await api('/api/status');
    const p = s.progress;
    view.innerHTML = `
      <h1>Состояние</h1>
      ${s.recordingWarning ? `<div class="banner"><div>⚠</div><div><b>${esc(s.recordingWarning)}</b></div></div>` : ''}
      <div class="grid g2">
        <div class="card"><h2>Программа</h2><div class="kv">
          <div>Версия</div><div><b>${esc(s.version)}</b></div>
          <div>Обновления</div><div>${esc(s.update.status)}${s.update.checkedUtc ? ` <span class="muted">(проверено ${ago(s.update.checkedUtc)})</span>` : ''} <button id="upd" style="margin-left:6px;padding:3px 10px">Проверить</button></div>
          <div>Репозиторий</div><div><a href="https://github.com/${esc(s.repo)}" target="_blank" rel="noopener">github.com/${esc(s.repo)}</a></div>
          <div>Автозапуск</div><div>${s.autostart ? 'включён' : 'выключен (включается в меню значка в трее)'}</div>
          <div>Данные</div><div class="mono">${esc(s.dataDir)}</div>
          <div>Атлас сундуков</div><div>${n(s.atlas)} известных мест</div>
        </div></div>
        <div class="card"><h2>Реплеи</h2><div class="kv">
          <div>Папка</div><div class="mono">${esc(s.demosDir)}</div>
          <div>Найдено</div><div>${s.demosFound ? `${n(s.replayFiles)} файлов` : '<span class="rust">папки нет</span>'}</div>
          <div>Самый свежий</div><div>${s.newestReplayUtc ? `${dt(s.newestReplayUtc)} (${ago(s.newestReplayUtc)})` : '—'}</div>
          <div>Fortnite</div><div>${s.fortniteRunning ? 'запущен (идёт игра — реплей посчитается после матча)' : 'не запущен'}</div>
          <div>Очередь</div><div>${p.busy ? `считаю ${n(p.done)} из ${n(p.total || p.pending)}${p.current ? ` · ${esc(p.current)}` : ''}` : 'пусто'}${p.live ? ` · ${n(p.live)} ещё пишется` : ''}</div>
        </div>
        <div style="margin-top:14px;display:flex;gap:10px;flex-wrap:wrap">
          <button class="primary" id="recount">Пересчитать всё</button>
        </div></div>
      </div>
      <div class="card" style="margin-top:16px"><h2>Очередь и ошибки разбора</h2>
        ${s.queue.length ? `<div class="tablewrap"><table><thead><tr><th>Файл</th><th>Состояние</th><th>Билд Fortnite</th><th>Ошибка</th><th class="n">Попыток</th></tr></thead><tbody>
          ${s.queue.map((q) => `<tr><td class="mono">${esc(q.file)}</td><td>${esc(q.status)}</td><td>${esc(q.build || '')}</td><td class="wrap">${esc(q.error || '')}</td><td class="n">${n(q.attempts)}</td></tr>`).join('')}
        </tbody></table></div><p class="muted" style="font-size:13px">Реплеи с ошибкой пересчитаются сами после обновления программы — ничего не потеряется.</p>` : '<div class="muted">Ошибок нет.</div>'}
      </div>
      <div class="grid g2" style="margin-top:16px">
        <div class="card"><h2>Как включить запись повторов</h2>
          <ol class="steps">
            <li>В Fortnite откройте меню (в лобби — значок профиля / меню справа вверху) → <b>Настройки</b>.</li>
            <li>Вкладка <b>«Игра»</b> (шестерёнка) → раздел <b>«Повторы»</b>.</li>
            <li>Включите <b>запись повторов</b> для Королевской битвы (Record Replays).</li>
            <li>Сыграйте матч. После матча реплей появится в <span class="mono">%LOCALAPPDATA%\\FortniteGame\\Saved\\Demos</span>, а счётчик обновится сам.</li>
          </ol>
          <p class="muted" style="font-size:13px">Fortnite хранит ограниченное число последних повторов и удаляет старые — программа запоминает уже посчитанные матчи, так что удаление реплеев счётчик не уменьшит.</p>
        </div>
        <div class="card"><h2>Настройки подсчёта</h2>
          <div class="kv">
            <div>Радиус эвристики</div><div><input type="number" id="rad" min="1" max="100" step="1" value="${s.heuristicRadiusM}" style="width:80px"> м <button id="saveRad" style="margin-left:6px;padding:3px 10px">Сохранить</button></div>
          </div>
          <p class="muted" style="font-size:13px">Эвристика нужна, только когда сундук уничтожен неоткрытым, а кто его сломал, в реплее не записано: тогда он засчитывается тому из отряда, кто был не дальше этого радиуса в окне ±2 с. Новый радиус применится к новым матчам и после «Пересчитать всё».</p>
        </div>
      </div>`;
    $('#ver').textContent = `версия ${s.version}`;
    $('#recount').addEventListener('click', async (e) => { e.target.disabled = true; await post('/api/recount'); setTimeout(draw, 1500); });
    $('#upd').addEventListener('click', async (e) => { e.target.disabled = true; e.target.textContent = 'Проверяю…'; await post('/api/update/check'); draw(); });
    $('#saveRad').addEventListener('click', async (e) => { await post('/api/settings', { heuristicRadiusM: +$('#rad').value }); e.target.textContent = 'Сохранено'; });
  };
  await draw();
  timer = setInterval(() => {
    if (document.activeElement?.id === 'rad') return;
    draw().catch(() => {});
  }, 5000);
}

route();
