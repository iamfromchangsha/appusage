/* ============================================================
   AppUsage Dashboard — 前端逻辑
   ============================================================ */

const $ = (s) => document.querySelector(s);
const $$ = (s) => Array.from(document.querySelectorAll(s));

const state = {
  weeks: 53,
  palette: 'green',
  sortKey: 'total',
  sortDir: -1,
  apps: [],
  summary: null,
  heat: new Map(),
  levels: { q1: 0, q2: 0, q3: 0 },
};

/* ---------------- 工具 ---------------- */

function fmtDur(s) {
  s = Math.max(0, Math.round(s || 0));
  const h = Math.floor(s / 3600);
  const m = Math.floor((s % 3600) / 60);
  const sec = s % 60;
  if (h) return `${h} 小时 ${m} 分`;
  if (m) return `${m} 分`;
  return `${sec} 秒`;
}
function fmtHours(s) {
  const h = (s || 0) / 3600;
  if (h >= 100) return `${Math.round(h).toLocaleString()} 小时`;
  return `${h.toFixed(1)} 小时`;
}
function esc(t) {
  return String(t == null ? '' : t).replace(/[&<>"']/g, (c) =>
    ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
}
function localISO(d) {
  const p = (n) => String(n).padStart(2, '0');
  return `${d.getFullYear()}-${p(d.getMonth() + 1)}-${p(d.getDate())}`;
}
function prettyDate(iso) {
  const [y, m, d] = iso.split('-').map(Number);
  const wd = ['周日', '周一', '周二', '周三', '周四', '周五', '周六'][new Date(y, m - 1, d).getDay()];
  return `${y} 年 ${m} 月 ${d} 日 · ${wd}`;
}
function hueOf(name) {
  let h = 0;
  for (let i = 0; i < name.length; i++) h = (h * 31 + name.charCodeAt(i)) % 360;
  return h;
}
function mono(name) {
  const ch = (name || '?').trim().charAt(0).toUpperCase();
  const h = hueOf(name || '?');
  return `<span class="mono" style="background:hsl(${h} 62% 62%)">${esc(ch)}</span>`;
}
function relTime(ts) {
  if (!ts) return '—';
  const d = (Date.now() / 1000) - ts;
  if (d < 60) return '刚刚';
  if (d < 3600) return `${Math.floor(d / 60)} 分钟前`;
  if (d < 86400) return `${Math.floor(d / 3600)} 小时前`;
  const days = Math.floor(d / 86400);
  if (days < 30) return `${days} 天前`;
  return new Date(ts * 1000).toLocaleDateString('zh-CN');
}

/* ---------------- 数据加载 ---------------- */

async function load() {
  try {
    const [sum, apps, heat] = await Promise.all([
      fetch('/api/summary').then((r) => r.json()),
      fetch('/api/apps').then((r) => r.json()),
      fetch(`/api/heatmap?days=${state.weeks * 7}`).then((r) => r.json()),
    ]);
    state.summary = sum;
    state.apps = apps.apps || [];
    state.heat = new Map((heat.days || []).map((d) => [d.date, d]));
    renderStatus();
    renderCards();
    renderRanks();
    renderHeatmap();
    renderRecent();
    renderTimeline('#todayTimeline', await fetch('/api/day?date=' + localISO(new Date())).then((r) => r.json()));
    renderTable();
  } catch (err) {
    console.error(err);
    $('#statusText').textContent = '服务连接失败';
    $('#statusChip').className = 'chip chip-off';
  }
}

/* ---------------- 状态条 ---------------- */

function renderStatus() {
  const st = state.summary && state.summary.status;
  const chip = $('#statusChip');
  if (!st) return;
  if (st.online) {
    chip.className = 'chip chip-on';
    $('#statusText').textContent = '追踪器运行中';
  } else {
    chip.className = 'chip chip-off';
    $('#statusText').textContent = '追踪器未运行';
  }
}

/* ---------------- 统计卡 ---------------- */

function renderCards() {
  const s = state.summary;
  if (!s) return;
  $('#sToday').textContent = fmtDur(s.today.seconds);
  const top = (s.today.apps || [])[0];
  $('#sTodaySub').textContent = s.today.apps.length
    ? `${s.today.apps.length} 个应用 · 最多 ${top.name} ${fmtDur(top.seconds)}`
    : '今天还没有记录';

  $('#sYest').textContent = fmtDur(s.yesterday.seconds);
  const diff = s.today.seconds - s.yesterday.seconds;
  const arrow = diff > 0 ? '↑' : diff < 0 ? '↓' : '—';
  $('#sYestSub').textContent = `较昨日 ${arrow} ${fmtDur(Math.abs(diff))}`;

  $('#sWeek').textContent = fmtDur(s.week.seconds);
  $('#sWeekSub').textContent = `日均 ${fmtDur(s.week.avg)}`;
  $('#sMonth').textContent = fmtDur(s.month.seconds);
  $('#sMonthSub').textContent = `日均 ${fmtDur(s.month.avg)}`;
  $('#sTotal').textContent = fmtHours(s.total.seconds);
  $('#sTotalSub').textContent = `${s.total.days} 天活跃 · ${s.total.apps} 个应用`;
  $('#todayTotal').textContent = fmtDur(s.today.seconds);
}

/* ---------------- 排行 ---------------- */

function renderRank(el, list, total) {
  if (!list || !list.length) {
    el.innerHTML = '<div class="empty">暂无数据</div>';
    return;
  }
  const max = Math.max(...list.map((x) => x.seconds)) || 1;
  el.innerHTML = list
    .map(
      (x) => `
      <li>
        ${mono(x.name)}
        <span class="nm">${esc(x.name)}</span>
        <span class="vl">${fmtDur(x.seconds)}</span>
        <span class="bar"><i style="width:${Math.max(2, (x.seconds / max) * 100)}%"></i></span>
      </li>`
    )
    .join('');
}

function renderRanks() {
  const s = state.summary;
  if (!s) return;
  renderRank($('#todayRank'), (s.top_today || []).slice(0, 6));
  renderRank($('#totalRank'), s.top_total || []);
  const tot = (s.top_total || []).reduce((a, b) => a + b.seconds, 0);
  $('#totalSub').textContent = `${(s.top_total || []).length} 个应用 · 合计 ${fmtHours(tot)}`;
}

/* ---------------- 最近 30 天趋势 ---------------- */

function renderRecent() {
  const box = $('#recentBars');
  if (!box) return;
  const days = [];
  const base = new Date();
  base.setHours(0, 0, 0, 0);
  for (let i = 29; i >= 0; i--) {
    const d = new Date(base);
    d.setDate(base.getDate() - i);
    const iso = localISO(d);
    const rec = state.heat.get(iso);
    days.push({ iso, sec: rec ? rec.seconds : 0 });
  }
  const max = Math.max(...days.map((d) => d.sec), 1);
  box.innerHTML = days
    .map(
      (d) =>
        `<div class="rb${d.sec ? '' : ' zero'}" style="height:${d.sec ? Math.max(6, (d.sec / max) * 100) : 3}%" title="${d.iso} · ${fmtDur(d.sec)}"></div>`
    )
    .join('');
  const active = days.filter((d) => d.sec > 0).length;
  const sum = days.reduce((a, b) => a + b.sec, 0);
  $('#recentSub').textContent = `${active} 天有记录 · 日均 ${fmtDur(sum / 30)}`;
  $('#recentAxis').innerHTML = `<span>${days[0].iso.slice(5)}</span><span>今天</span>`;
}

/* ---------------- 热力图 ---------------- */

function computeLevels() {
  const vals = Array.from(state.heat.values())
    .map((d) => d.seconds)
    .filter((v) => v > 0)
    .sort((a, b) => a - b);
  if (!vals.length) {
    state.levels = { q1: 1, q2: 1, q3: 1 };
    return;
  }
  const at = (p) => vals[Math.min(vals.length - 1, Math.floor(vals.length * p))];
  state.levels = { q1: at(0.25), q2: at(0.5), q3: at(0.75) };
}

function levelOf(sec) {
  if (!sec) return 0;
  const { q1, q2, q3 } = state.levels;
  if (sec <= q1) return 1;
  if (sec <= q2) return 2;
  if (sec <= q3) return 3;
  return 4;
}

function renderHeatmap() {
  computeLevels();
  const today = new Date();
  today.setHours(0, 0, 0, 0);
  const endDow = today.getDay();
  const weekStart = new Date(today);
  weekStart.setDate(today.getDate() - endDow);
  const gridStart = new Date(weekStart);
  gridStart.setDate(weekStart.getDate() - (state.weeks - 1) * 7);

  const grid = $('#heatGrid');
  grid.innerHTML = '';
  const monthRow = $('#monthRow');
  monthRow.innerHTML = '';
  const cellSize = 15, gap = 3, stride = cellSize + gap;

  const monthLabels = [];
  let lastMonth = -1;
  const todayISO = localISO(today);

  for (let w = 0; w < state.weeks; w++) {
    for (let d = 0; d < 7; d++) {
      const dt = new Date(gridStart);
      dt.setDate(gridStart.getDate() + w * 7 + d);
      const iso = localISO(dt);
      const future = dt > today;
      const rec = state.heat.get(iso);
      const sec = rec ? rec.seconds : 0;
      const lv = future ? 0 : levelOf(sec);

      const cell = document.createElement('div');
      cell.className = 'cell lv' + lv + (future ? ' future' : '') + (iso === todayISO ? ' today' : '');
      cell.dataset.date = iso;
      cell.dataset.sec = sec;
      if (!future) {
        cell.addEventListener('mouseenter', (e) => showTip(e, iso, rec));
        cell.addEventListener('mousemove', moveTip);
        cell.addEventListener('mouseleave', hideTip);
        cell.addEventListener('click', () => openDay(iso));
      }
      grid.appendChild(cell);

      if (dt.getDate() === 1 && dt.getMonth() !== lastMonth) {
        lastMonth = dt.getMonth();
        monthLabels.push({ col: w, label: `${dt.getMonth() + 1}月` });
      }
    }
  }

  monthLabels.forEach((m) => {
    const el = document.createElement('span');
    el.textContent = m.label;
    el.style.left = m.col * stride + 'px';
    monthRow.appendChild(el);
  });

  const dow = $('#dowCol');
  dow.innerHTML = ['一', '', '三', '', '五', '', '']
    .map((t) => `<span>${t}</span>`)
    .join('');

  const totalDays = Array.from(state.heat.values()).filter((d) => d.seconds > 0).length;
  const busiest = Array.from(state.heat.values()).sort((a, b) => b.seconds - a.seconds)[0];
  $('#heatSub').textContent = busiest
    ? `${state.weeks} 周 · ${totalDays} 天有记录 · 最忙的一天 ${busiest.date}（${fmtDur(busiest.seconds)}）`
    : `${state.weeks} 周 · 暂无记录`;
}

/* ---------------- 悬浮提示 ---------------- */

const tip = $('#tip');
function showTip(e, iso, rec) {
  const sec = rec ? rec.seconds : 0;
  tip.innerHTML = `<b>${fmtDur(sec)}</b><div class="t2">${esc(prettyDate(iso))}${rec && rec.apps ? ` · ${rec.apps} 个应用` : ''}</div>`;
  tip.classList.add('show');
  moveTip(e);
}
function moveTip(e) {
  tip.style.left = e.clientX + 'px';
  tip.style.top = e.clientY - 10 + 'px';
}
function hideTip() {
  tip.classList.remove('show');
}

/* ---------------- 时间线 ---------------- */

function renderTimeline(sel, day) {
  const el = $(sel);
  if (!el) return;
  const hours = day.hours || {};
  const arr = [];
  for (let h = 0; h < 24; h++) arr.push(hours[String(h).padStart(2, '0')] || []);
  const tot = arr.map((l) => l.reduce((a, b) => a + b.seconds, 0));
  const max = Math.max(...tot, 1);
  el.innerHTML = tot
    .map((v, h) => {
      const pct = (v / max) * 100;
      const label = `${h}:00 · ${v ? fmtDur(v) : '无'}`;
      return `<div class="bar${v ? '' : ' zero'}" style="height:${Math.max(3, pct)}%" data-tip="${label}"></div>`;
    })
    .join('');
}

/* ---------------- 抽屉 ---------------- */

async function openDay(iso) {
  const d = await fetch('/api/day?date=' + iso).then((r) => r.json());
  $('#dTitle').textContent = prettyDate(iso);
  $('#dSub').textContent = `${d.apps.length} 个应用`;
  $('#dTotal').textContent = fmtDur(d.seconds);
  renderTimeline('#dTimeline', d);
  renderRank($('#dApps'), d.apps);
  $('#drawer').classList.add('show');
  $('#drawerMask').classList.add('show');
}
function closeDay() {
  $('#drawer').classList.remove('show');
  $('#drawerMask').classList.remove('show');
}
$('#dClose').addEventListener('click', closeDay);
$('#drawerMask').addEventListener('click', closeDay);
document.addEventListener('keydown', (e) => {
  if (e.key === 'Escape') closeDay();
});

/* ---------------- 应用表格 ---------------- */

function renderTable() {
  const q = ($('#searchInput').value || '').trim().toLowerCase();
  let list = state.apps.filter((a) => !q || a.name.toLowerCase().includes(q));
  const k = state.sortKey;
  list.sort((a, b) => {
    const va = a[k], vb = b[k];
    if (typeof va === 'string') return va.localeCompare(vb, 'zh') * state.sortDir;
    return ((va || 0) - (vb || 0)) * state.sortDir;
  });

  const max = Math.max(...state.apps.map((a) => a.total || 0), 1);
  $('#tableSub').textContent = `${state.apps.length} 个应用 · 累计 ${fmtHours(state.apps.reduce((s, a) => s + (a.total || 0), 0))}`;

  $('#appBody').innerHTML = list.length
    ? list
        .map(
          (a) => `
        <tr>
          <td class="name">${esc(a.name)}<small>${esc(a.exe_path || a.key)}</small></td>
          <td class="${a.today ? '' : 'zero'}">${a.today ? fmtDur(a.today) : '—'}</td>
          <td class="${a.d7 ? '' : 'zero'}">${a.d7 ? fmtDur(a.d7) : '—'}</td>
          <td class="${a.d30 ? '' : 'zero'}">${a.d30 ? fmtDur(a.d30) : '—'}</td>
          <td class="th-total">${fmtDur(a.total)}<span class="totalbar" style="width:${Math.max(
            2,
            (a.total / max) * 90
          )}px"></span></td>
          <td>${relTime(a.last_seen)}</td>
        </tr>`
        )
        .join('')
    : '<tr><td colspan="6" class="empty">没有匹配的应用</td></tr>';
}

$('#searchInput').addEventListener('input', renderTable);

$$('.tbl th[data-sort]').forEach((th) => {
  th.addEventListener('click', () => {
    const k = th.dataset.sort;
    if (state.sortKey === k) state.sortDir *= -1;
    else { state.sortKey = k; state.sortDir = (k === 'name') ? 1 : -1; }
    $$('.tbl th').forEach((x) => x.classList.remove('on'));
    th.classList.add('on');
    renderTable();
  });
});

/* ---------------- 导出 CSV ---------------- */

$('#exportBtn').addEventListener('click', () => {
  const head = ['应用', 'exe', '今日(秒)', '近7天(秒)', '近30天(秒)', '累计(秒)', '会话数', '最近使用'];
  const rows = state.apps.map((a) => [
    a.name, a.exe_path || a.key, a.today, a.d7, a.d30, a.total, a.sessions,
    a.last_seen ? new Date(a.last_seen * 1000).toLocaleString('zh-CN') : '',
  ]);
  const csv = [head, ...rows]
    .map((r) => r.map((c) => `"${String(c).replace(/"/g, '""')}"`).join(','))
    .join('\r\n');
  const blob = new Blob(['\ufeff' + csv], { type: 'text/csv;charset=utf-8' });
  const a = document.createElement('a');
  a.href = URL.createObjectURL(blob);
  a.download = `app-usage-${localISO(new Date())}.csv`;
  a.click();
  setTimeout(() => URL.revokeObjectURL(a.href), 3000);
});

/* ---------------- 控件 ---------------- */

$('#rangeSeg').addEventListener('click', (e) => {
  const b = e.target.closest('button');
  if (!b) return;
  $$('#rangeSeg button').forEach((x) => x.classList.remove('on'));
  b.classList.add('on');
  state.weeks = Number(b.dataset.weeks);
  fetch(`/api/heatmap?days=${state.weeks * 7}`).then((r) => r.json()).then((h) => {
    state.heat = new Map((h.days || []).map((d) => [d.date, d]));
    renderHeatmap();
    renderRecent();
  });
});

$('#paletteSeg').addEventListener('click', (e) => {
  const b = e.target.closest('button');
  if (!b) return;
  $$('#paletteSeg button').forEach((x) => x.classList.remove('on'));
  b.classList.add('on');
  state.palette = b.dataset.pal;
  document.body.dataset.pal = state.palette;
});

$('#refreshBtn').addEventListener('click', load);

/* ---------------- 启动 ---------------- */

document.body.dataset.pal = 'green';
load();
setInterval(load, 30000);
