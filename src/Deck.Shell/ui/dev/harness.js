(() => {
  const CATALOG = {
    claude: ['Claude', { standard: [1, 1, '1×1'] }],
    weather: ['Weather', { standard: [1, 1, '1×1'], compact: [1, 1, '1×1 compact'], hourly: [2, 1, '2×1 · next hours'] }],
    nowplaying: ['Now Playing', { standard: [1, 1, '1×1'], wide: [2, 1, '2×1 · art & controls'] }],
    system: ['System', { standard: [1, 1, '1×1'] }],
    noise: ['Noise', { standard: [1, 1, '1×1'] }],
    mic: ['Mic', { standard: [1, 1, '1×1'] }],
    camera: ['Camera', { standard: [1, 1, '1×1'] }],
    clock: ['World Clock', { standard: [1, 1, '1×1'] }],
    mixer: ['Mixer', { tall: [2, 2, '2×2 · 6 apps'], short: [2, 1, '2×1 · 3 apps'] }],
    pomodoro: ['Pomodoro', { standard: [1, 1, '1×1'] }],
    stopwatch: ['Stopwatch', { standard: [1, 1, '1×1'] }],
    dice: ['Dice', { standard: [1, 1, '1×1'] }],
    agenda: ['Agenda', { standard: [1, 1, '1×1 · next event'], wide: [2, 1, '2×1 · next 3'] }],
    month: ['Month', { standard: [2, 2, '2×2'] }],
    network: ['Network', { standard: [1, 1, '1×1'] }],
    display: ['Display', { standard: [1, 1, '1×1'] }]
  };
  const ITEMS = { preset: 'Preset', shortcut: 'Shortcut', countdown: 'Countdown' };
  const SIZES = {};
  for (const [kind, [, variants]] of Object.entries(CATALOG)) {
    SIZES[kind] = {};
    for (const [variant, [w, h]] of Object.entries(variants)) SIZES[kind][variant] = [w, h];
  }
  for (const kind of Object.keys(ITEMS)) SIZES[kind] = { standard: [1, 1] };
  const ART = 'data:image/svg+xml;utf8,' + encodeURIComponent(
    '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 10 10"><rect width="10" height="10" fill="#2b4a6b"/><circle cx="5" cy="5" r="3" fill="#7cc4ff"/></svg>');

  const SAMPLE = {
    claude: { waiting: 2, working: 0, names: 'Process queue · PMI Case Study', notify: false },
    weather: {
      available: true, icon: '☁️', label: 'Overcast', temp: '19°', feels: '17°', high: '20°', low: '14°', stale: false, error: null,
      hourly: [['15', '☁️', '19°'], ['16', '⛅', '18°'], ['17', '⛅', '17°'], ['18', '🌤️', '16°'], ['19', '☀️', '15°'], ['20', '☀️', '14°']]
        .map(([hour, icon, temp]) => ({ hour, icon, temp }))
    },
    nowplaying: { hasSession: true, playing: true, title: 'Teardrop', artist: 'Massive Attack', app: 'Spotify', art: ART },
    system: { cpu: 47, ram: 66, gpuAvailable: true, gpu: 22 },
    noise: { armed: true, running: false, threshold: 60, error: 'room sensor not found', device: null, level: 0 },
    mic: { muted: false, devices: ['Focusrite USB Audio'], apps: 'super-squad' },
    camera: { inUse: true, apps: 'super-squad' },
    clock: {
      cities: [
        { label: 'London', time: '13:35', day: 0, local: false },
        { label: 'Berlin', time: '14:35', day: 0, local: false },
        { label: 'Ankara', time: '15:35', day: 0, local: true },
        { label: 'Melbourne', time: '22:35', day: 0, local: false }
      ]
    },
    mixer: {
      apps: 6,
      rows: [
        { name: 'brave', label: 'Brave Browser', icon: null, volume: 85, muted: false, active: false, running: false },
        { name: 'chrome', label: 'Google Chrome', icon: null, volume: 81, muted: false, active: true, running: true },
        { name: 'Discord', label: 'Discord', icon: null, volume: 78, muted: false, active: false, running: false },
        { name: 'Gather', label: 'Gather', icon: null, volume: 95, muted: false, active: false, running: false },
        { name: 'Spotify', label: 'Spotify', icon: null, volume: 53, muted: false, active: false, running: false },
        { name: 'System', label: 'Volume Mixer', icon: null, volume: 19, muted: false, active: false, running: true }
      ]
    },
    pomodoro: { phase: 'idle', remaining: '25:00', blocks: 0, awaiting: false },
    stopwatch: { running: false, elapsed: '00:00', hasElapsed: false },
    dice: { mode: 'd6', last: '5' },
    network: { state: 'good', ping: '18 ms', down: '12.4 Mb/s', up: '0.8 Mb/s' },
    display: { ready: true, tint: false, level: 38, monitors: 3, unsupported: [] },
    agenda: {
      status: 'ok', total: 4,
      events: [
        { title: 'Standup', when: 'in 4 min', state: 'soon', link: true },
        { title: 'Design review', when: '16:00', state: 'later', link: true },
        { title: '1:1 with Ayşe', when: 'Tomorrow 09:00', state: 'later', link: false }
      ]
    },
    month: {
      title: 'September 2026', status: 'ok',
      days: Array.from({ length: 42 }, (_, i) => {
        const date = new Date(2026, 7, 31 + i);
        const events = [3, 10, 17, 24].includes(date.getDate()) && date.getMonth() === 8 ? ['11:00 Standup'] : [];
        return { day: date.getDate(), inMonth: date.getMonth() === 8, today: date.getMonth() === 8 && date.getDate() === 24, events };
      })
    }
  };

  const presets = [{ id: 'p1', name: 'WORK', count: 5 }, { id: 'p2', name: 'GAMING', count: 2 }];
  const countdowns = [{ id: 'c1', label: 'VACATION', value: '42 days', phase: 'ahead', date: 'Fri 6 Nov 2026' }];
  let editing = false;
  let placements = [
    ['preset', 'standard', 'p1', 0, 0], ['claude', 'standard', null, 4, 0], ['weather', 'standard', null, 5, 0],
    ['nowplaying', 'standard', null, 0, 1], ['system', 'standard', null, 1, 1], ['noise', 'standard', null, 2, 1],
    ['mic', 'standard', null, 3, 1], ['mixer', 'tall', null, 4, 1], ['clock', 'standard', null, 2, 2],
    ['camera', 'standard', null, 3, 2]
  ].map(([kind, variant, ref, col, row]) => ({ kind, variant, ref, col, row }));

  const listeners = [];
  const log = [];
  const emit = (data) => listeners.forEach((fn) => fn({ data }));
  const size = (p) => SIZES[p.kind][p.variant];
  const same = (p, kind, ref) => p.kind === kind && (p.ref ?? null) === (ref ?? null);
  const covers = (p, c, r) => {
    const [w, h] = size(p);
    return c >= p.col && c < p.col + w && r >= p.row && r < p.row + h;
  };
  const at = (c, r) => placements.find((p) => covers(p, c, r));
  const fits = (kind, variant, col, row, ignore) => {
    const dims = SIZES[kind] && SIZES[kind][variant];
    if (!dims) return false;
    const [w, h] = dims;
    if (col < 0 || row < 0 || col + w > 6 || row + h > 3) return false;
    for (let c = col; c < col + w; c++) {
      for (let r = row; r < row + h; r++) {
        const other = at(c, r);
        if (other && other !== ignore) return false;
      }
    }
    return true;
  };

  function sendLayout() {
    const placed = (kind, ref) => placements.some((p) => same(p, kind, ref));
    const builtIns = Object.entries(CATALOG).filter(([k]) => !placed(k, null)).map(([k, [title, variants]]) => ({
      kind: k, ref: null, title, group: null,
      variants: Object.entries(variants).map(([variant, [w, h, label]]) => ({ variant, label, w, h }))
    }));
    const items = presets.filter((p) => !placed('preset', p.id)).map((p) => ({
      kind: 'preset', ref: p.id, title: p.name, group: 'Preset',
      variants: [{ variant: 'standard', label: '1×1', w: 1, h: 1 }]
    }));
    emit({
      type: 'layout', editing, columns: 6, rows: 3,
      placements: placements.map((p) => { const [w, h] = size(p); return { ...p, w, h }; }),
      library: builtIns.concat(items, countdowns.filter((c) => !placed('countdown', c.id)).map((c) => ({
        kind: 'countdown', ref: c.id, title: c.label, group: 'Countdown',
        variants: [{ variant: 'standard', label: '1×1', w: 1, h: 1 }]
      })))
    });
  }

  function sendData() {
    for (const [kind, data] of Object.entries(SAMPLE)) emit({ type: 'widget', kind, ref: null, data });
    for (const p of presets) {
      emit({ type: 'widget', kind: 'preset', ref: p.id, data: { name: p.name, count: p.count, state: 'idle', summary: null } });
    }
    for (const c of countdowns) {
      emit({ type: 'widget', kind: 'countdown', ref: c.id, data: { label: c.label, value: c.value, phase: c.phase, date: c.date } });
    }
  }

  function handleLayout(op) {
    const found = placements.find((p) => same(p, op.kind, op.ref));
    if (op.op === 'edit') editing = true;
    else if (op.op === 'done') editing = false;
    else if (op.op === 'remove') placements = placements.filter((p) => p !== found);
    else if (op.op === 'place' && !found && fits(op.kind, op.variant, op.col, op.row, null)) {
      placements.push({ kind: op.kind, variant: op.variant, ref: op.ref ?? null, col: op.col, row: op.row });
      sendData();
    } else if (op.op === 'move' && found && !(found.col === op.col && found.row === op.row)) {
      if (fits(found.kind, found.variant, op.col, op.row, found)) {
        found.col = op.col;
        found.row = op.row;
      } else {
        const other = at(op.col, op.row);
        if (other && other !== found && String(size(other)) === String(size(found))) {
          const { col, row } = found;
          found.col = other.col;
          found.row = other.row;
          other.col = col;
          other.row = row;
        }
      }
    } else if (op.op === 'new-preset') {
      const id = 'p' + (presets.length + 1);
      presets.push({ id, name: 'NEW', count: 3 });
      if (fits('preset', 'standard', op.col, op.row, null)) {
        placements.push({ kind: 'preset', variant: 'standard', ref: id, col: op.col, row: op.row });
      }
      sendData();
    } else if (op.op === 'new-countdown') {
      const id = 'c' + (countdowns.length + 1);
      countdowns.push({ id, label: 'NEW', value: 'tomorrow', phase: 'soon', date: 'Fri 25 Sep 2026' });
      if (fits('countdown', 'standard', op.col, op.row, null)) {
        placements.push({ kind: 'countdown', variant: 'standard', ref: id, col: op.col, row: op.row });
      }
      sendData();
    } else if (op.op === 'delete') {
      const index = presets.findIndex((p) => p.id === op.ref);
      if (index >= 0) presets.splice(index, 1);
      const cdIndex = countdowns.findIndex((c) => c.id === op.ref);
      if (cdIndex >= 0) countdowns.splice(cdIndex, 1);
      placements = placements.filter((p) => !same(p, op.kind, op.ref));
    }
    sendLayout();
  }

  Object.defineProperty(window, 'chrome', {
    configurable: true,
    value: {
      webview: {
        addEventListener: (type, fn) => listeners.push(fn),
        postMessage: (message) => {
          log.push(message);
          if (message.startsWith('layout:')) handleLayout(JSON.parse(message.slice('layout:'.length)));
        }
      }
    }
  });

  window.harness = { log, emit, sendLayout, sendData, placements: () => placements };

  document.addEventListener('DOMContentLoaded', () => {
    sendData();
    sendLayout();
  });
})();
