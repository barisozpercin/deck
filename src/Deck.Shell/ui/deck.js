document.body.innerHTML = '<div id="stage"><main id="grid"></main></div>';

const bridge = window.chrome.webview;
const post = (message) => bridge.postMessage(message);
const grid = document.getElementById('grid');
const cache = new Map();
let layout = { columns: 6, rows: 3, editing: false, placements: [], library: [] };

const keyOf = (kind, ref) => kind + '|' + (ref ?? '');
const layoutOp = (op) => post('layout:' + JSON.stringify(op));
const senderFor = (kind, ref) => (msg) => post('widget:' + JSON.stringify({ kind, ref: ref ?? null, msg }));
const pick = (value, variant) => (typeof value === 'function' ? value(variant) : value);

function buildTile(p) {
  const def = Widgets[p.kind];
  const click = pick(def.click, p.variant);
  const context = pick(def.context, p.variant);
  const send = senderFor(p.kind, p.ref);

  const tile = document.createElement('div');
  tile.className = `tile w-${p.kind} v-${p.variant}` + (click ? ' pressable' : '');
  tile.style.gridColumn = `${p.col + 1} / span ${p.w}`;
  tile.style.gridRow = `${p.row + 1} / span ${p.h}`;
  tile.dataset.key = keyOf(p.kind, p.ref);
  tile.innerHTML = def.template(p.variant);
  if (def.bind) def.bind(tile, send, p.variant);

  tile.addEventListener('click', () => {
    if (!layout.editing && click) send(click);
  });
  tile.addEventListener('contextmenu', (e) => {
    e.preventDefault();
    if (layout.editing) return;
    if (def.deletable) layoutOp({ op: 'delete', kind: p.kind, ref: p.ref });
    else if (context) send(context);
  });

  return tile;
}

function buildEmpty(col, row) {
  const cell = document.createElement('div');
  cell.className = 'tile blank';
  cell.style.gridColumn = String(col + 1);
  cell.style.gridRow = String(row + 1);
  return cell;
}

function applyData(tile, p, data) {
  if (data.failed) {
    tile.classList.add('failed');
    tile.classList.remove('pressable');
    tile.innerHTML = '<div class="label"></div><div class="sub">failed to start</div>';
    setText(tile, '.label', p.kind.toUpperCase());
    return;
  }
  Widgets[p.kind].update(tile, data, p.variant, senderFor(p.kind, p.ref));
}

function render() {
  grid.innerHTML = '';
  const taken = new Set();

  for (const p of layout.placements) {
    if (!Widgets[p.kind]) continue;
    for (let c = p.col; c < p.col + p.w; c++) {
      for (let r = p.row; r < p.row + p.h; r++) taken.add(c + ',' + r);
    }
    const tile = buildTile(p);
    grid.append(tile);
    const cached = cache.get(tile.dataset.key);
    if (cached) applyData(tile, p, cached);
  }

  for (let r = 0; r < layout.rows; r++) {
    for (let c = 0; c < layout.columns; c++) {
      if (taken.has(c + ',' + r)) continue;
      grid.append(buildEmpty(c, r));
    }
  }
}

function onLayout(message) {
  layout = message;
  const live = new Set(layout.placements.map((p) => keyOf(p.kind, p.ref)));
  for (const key of [...cache.keys()]) {
    if (!live.has(key)) cache.delete(key);
  }
  render();
}

function onWidget(message) {
  const key = keyOf(message.kind, message.ref);
  cache.set(key, { ...(cache.get(key) || {}), ...message.data });

  const p = layout.placements.find((x) => keyOf(x.kind, x.ref) === key);
  if (!p) return;
  const tile = grid.querySelector(`[data-key="${CSS.escape(key)}"]`);
  if (tile) applyData(tile, p, message.data);
}

bridge.addEventListener('message', (ev) => {
  const m = ev.data;
  if (m.type === 'layout') onLayout(m);
  else if (m.type === 'widget') onWidget(m);
});
