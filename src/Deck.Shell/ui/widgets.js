const q = (tile, selector) => tile.querySelector(selector);
const setText = (tile, selector, value) => { q(tile, selector).textContent = value ?? ''; };
const clampPercent = (value) => Math.max(0, Math.min(100, Math.round(value)));

const ICONS = {
  record: `<svg class="np-record" viewBox="0 0 24 24">
      <circle cx="12" cy="12" r="11.2" fill="#0e1116" stroke="#333c48" stroke-width="0.8" />
      <circle cx="12" cy="12" r="8.6" fill="none" stroke="#2a313b" stroke-width="0.6" />
      <circle cx="12" cy="12" r="6.9" fill="none" stroke="#2a313b" stroke-width="0.6" />
      <circle cx="12" cy="12" r="5.2" fill="none" stroke="#2a313b" stroke-width="0.6" />
      <circle cx="12" cy="12" r="3.4" fill="currentColor" />
      <circle cx="12" cy="12" r="0.9" fill="#0b0d10" />
      <path d="M12 1.2a10.8 10.8 0 0 1 7.6 3.2" fill="none" stroke="#4a5666" stroke-width="0.9" stroke-linecap="round" />
    </svg>`,
  micLive: `<svg class="icon-live" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round">
      <rect x="9" y="2" width="6" height="12" rx="3" />
      <path d="M5 11a7 7 0 0 0 14 0" />
      <line x1="12" y1="18" x2="12" y2="22" />
    </svg>`,
  micMuted: `<svg class="icon-muted" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round">
      <rect x="9" y="2" width="6" height="12" rx="3" />
      <path d="M5 11a7 7 0 0 0 14 0" />
      <line x1="12" y1="18" x2="12" y2="22" />
      <line x1="3" y1="3" x2="21" y2="21" />
    </svg>`,
  cameraLive: `<svg class="icon-live" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round">
      <rect x="2" y="6" width="14" height="12" rx="2.5" />
      <path d="M16 10.5 22 7v10l-6-3.5z" />
    </svg>`,
  cameraOff: `<svg class="icon-off" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round">
      <rect x="2" y="6" width="14" height="12" rx="2.5" />
      <path d="M16 10.5 22 7v10l-6-3.5z" />
      <line x1="3" y1="3" x2="21" y2="21" />
    </svg>`
};

function horizontalDrag(handle, measure, onValue, onEnd) {
  let active = false;
  const valueAt = (x) => {
    const box = measure().getBoundingClientRect();
    return clampPercent(((x - box.left) / box.width) * 100);
  };

  handle.addEventListener('pointerdown', (e) => {
    e.preventDefault();
    e.stopPropagation();
    active = true;
    handle.setPointerCapture(e.pointerId);
    onValue(valueAt(e.clientX));
  });
  handle.addEventListener('pointermove', (e) => {
    if (active) onValue(valueAt(e.clientX));
  });

  const end = (e) => {
    if (!active) return;
    active = false;
    try { handle.releasePointerCapture(e.pointerId); } catch (_) {}
    onEnd();
  };
  handle.addEventListener('pointerup', end);
  handle.addEventListener('pointercancel', end);
  handle.addEventListener('click', (e) => e.stopPropagation());

  return { get active() { return active; } };
}

function setStat(tile, name, value) {
  const fill = q(tile, '.f-' + name);
  fill.style.width = value + '%';
  fill.classList.toggle('mid', value >= 60 && value < 85);
  fill.classList.toggle('high', value >= 85);
  setText(tile, '.v-' + name, value + '%');
}

function renderNoise(tile) {
  const s = tile.noise;
  tile.classList.toggle('disarmed', !s.armed);
  tile.classList.toggle('dead', !s.running);
  q(tile, '.tick').style.left = s.threshold + '%';
  setText(tile, '.room-state', !s.running
    ? (s.error || 'not listening')
    : (s.armed ? 'armed · limit ' + s.threshold : 'ALERTS OFF · limit ' + s.threshold));
}

function renderMixer(tile, rows, total, send) {
  const host = q(tile, '.mx-rows');
  host.innerHTML = '';

  setText(tile, '.mx-empty',
    rows.length === 0 ? 'no audio apps'
    : total > rows.length ? (total - rows.length) + ' more · right-click'
    : '');

  for (const r of rows) {
    const row = document.createElement('div');
    row.className = 'mx-row'
      + (r.muted ? ' muted' : '')
      + (r.active ? '' : ' quiet')
      + (r.running === false ? ' offline' : '');

    const icon = document.createElement(r.icon ? 'img' : 'span');
    icon.className = 'mx-icon';
    if (r.icon) {
      icon.src = r.icon;
      icon.alt = '';
      icon.draggable = false;
    }

    const name = document.createElement('span');
    name.className = 'mx-name';
    name.textContent = r.label || r.name;

    const tip = (r.running === false
      ? r.name + ' — not running; this level applies when it next opens'
      : r.name + (r.muted ? ' — muted, click to unmute' : ' — click to mute'))
      + '\nRight-click to remove it from the mixer';
    icon.title = tip;
    name.title = tip;

    row.addEventListener('contextmenu', (e) => {
      e.preventDefault();
      e.stopPropagation();
      send('forget:' + r.name);
    });

    if (r.running !== false) {
      const toggleMute = () => send('set:' + JSON.stringify({ name: r.name, muted: !r.muted }));
      icon.addEventListener('click', toggleMute);
      name.addEventListener('click', toggleMute);
    }

    const wrap = document.createElement('span');
    wrap.className = 'mx-barwrap';
    const bar = document.createElement('span');
    bar.className = 'mx-bar';
    const fill = document.createElement('span');
    fill.className = 'mx-fill';
    fill.style.width = r.volume + '%';
    bar.append(fill);
    wrap.append(bar);

    const value = document.createElement('span');
    value.className = 'mx-val';
    value.textContent = r.volume;

    horizontalDrag(wrap, () => bar, (v) => {
      tile.mixerDragging = true;
      fill.style.width = v + '%';
      value.textContent = v;
      send('set:' + JSON.stringify({ name: r.name, volume: v }));
    }, () => {
      tile.mixerDragging = false;
      send('commit');
    });

    row.append(icon, name, wrap, value);
    host.append(row);
  }
}

const Widgets = {
  claude: {
    click: 'press',
    context: 'notify-toggle',
    template: () => `
      <div class="label">CLAUDE</div>
      <div class="cc-count">–</div>
      <div class="sub cc-state">no sessions</div>
      <div class="device cc-names"></div>
      <div class="device cc-muted cc-alerts"></div>`,
    update(tile, d) {
      const total = d.waiting + d.working;
      tile.classList.toggle('waiting', d.waiting > 0);
      tile.classList.toggle('working', d.waiting === 0 && d.working > 0);
      tile.classList.toggle('idle', total === 0);
      setText(tile, '.cc-count', total === 0 ? '–' : (d.waiting > 0 ? d.waiting : d.working));
      setText(tile, '.cc-state',
        total === 0 ? 'no sessions'
        : d.waiting > 0 ? 'waiting on you' + (d.working ? ' · ' + d.working + ' working' : '')
        : 'working');
      setText(tile, '.cc-names', d.names);
      setText(tile, '.cc-alerts', d.notify ? '' : 'alerts muted');
      tile.title = 'Click to jump to a session · right-click to '
        + (d.notify ? 'mute' : 'unmute') + ' notifications';
    }
  },

  weather: {
    template: () => `
      <div class="label">ANKARA</div>
      <div class="wx-now"><span class="wx-icon">–</span><span class="wx-temp">–</span></div>
      <div class="sub wx-label">loading…</div>
      <div class="wx-stats">
        <div><span class="wx-k">FEELS</span><span class="wx-v wx-feels">–</span></div>
        <div><span class="wx-k">HIGH</span><span class="wx-v wx-high">–</span></div>
        <div><span class="wx-k">LOW</span><span class="wx-v wx-low">–</span></div>
      </div>`,
    update(tile, d) {
      tile.classList.toggle('stale', d.stale);
      setText(tile, '.wx-icon', d.icon);
      setText(tile, '.wx-temp', d.temp);
      setText(tile, '.wx-label', d.error || d.label);
      setText(tile, '.wx-feels', d.available ? d.feels : '–');
      setText(tile, '.wx-high', d.available ? d.high : '–');
      setText(tile, '.wx-low', d.available ? d.low : '–');
    }
  },

  nowplaying: {
    click: 'press',
    context: 'next',
    template: () => `
      ${ICONS.record}
      <div class="np-title">nothing</div>
      <div class="sub np-artist"></div>
      <div class="device np-app"></div>`,
    update(tile, d) {
      tile.classList.toggle('playing', d.playing);
      tile.classList.toggle('idle', !d.hasSession);
      setText(tile, '.np-title', d.hasSession ? (d.title || 'untitled') : 'nothing playing');
      setText(tile, '.np-artist', !d.hasSession ? '' : d.playing ? (d.artist || 'playing') : 'paused');
      setText(tile, '.np-app', d.app);
    }
  },

  system: {
    template: () => ['cpu', 'ram', 'gpu'].map((k) => `
      <div class="stat">
        <span class="k">${k.toUpperCase()}</span>
        <span class="bar"><span class="fill f-${k}"></span></span>
        <span class="v v-${k}">–</span>
      </div>`).join(''),
    update(tile, d) {
      setStat(tile, 'cpu', d.cpu);
      setStat(tile, 'ram', d.ram);
      if (d.gpuAvailable) {
        setStat(tile, 'gpu', d.gpu);
      } else {
        q(tile, '.f-gpu').style.width = '0%';
        setText(tile, '.v-gpu', 'n/a');
      }
    }
  },

  noise: {
    click: 'press',
    context: 'calibrate',
    template: () => `
      <div class="label">NOISE</div>
      <div class="meter-wrap">
        <div class="meter">
          <div class="track"><div class="fill"></div></div>
          <div class="tick"></div>
        </div>
      </div>
      <div class="reading"><span class="room-level">0</span><span class="unit">/100</span></div>
      <div class="sub room-state">starting…</div>
      <div class="device room-device">—</div>`,
    bind(tile, send) {
      const state = { armed: true, running: false, error: null, threshold: 60 };
      tile.noise = state;
      state.drag = horizontalDrag(q(tile, '.meter-wrap'), () => q(tile, '.meter'), (v) => {
        tile.classList.add('dragging');
        state.threshold = v;
        renderNoise(tile);
        send('threshold-set:' + v);
      }, () => {
        tile.classList.remove('dragging');
        send('threshold-commit');
      });
    },
    update(tile, d) {
      const state = tile.noise;
      if ('armed' in d) {
        state.armed = d.armed;
        state.running = d.running;
        state.error = d.error;
        if (!state.drag.active) state.threshold = d.threshold;
        setText(tile, '.room-device', d.device || 'no sensor selected');
        renderNoise(tile);
      }
      if ('level' in d) {
        q(tile, '.meter .fill').style.width = d.level + '%';
        setText(tile, '.room-level', Math.round(d.level));
        const over = d.level > state.threshold;
        tile.classList.toggle('over', over);
        tile.classList.toggle('alerting', over && state.armed);
      }
    }
  },

  mic: {
    click: 'press',
    template: () => `
      ${ICONS.micLive}${ICONS.micMuted}
      <div class="label mute-label">MIC LIVE</div>
      <div class="sub mute-sub"></div>
      <div class="device mute-device">—</div>`,
    update(tile, d) {
      tile.classList.toggle('muted', d.muted);
      setText(tile, '.mute-label', d.muted ? 'MUTED' : 'MIC LIVE');
      setText(tile, '.mute-device', d.devices.length ? d.devices.join(' · ') : 'no device selected');
      setText(tile, '.mute-sub', d.apps);
    }
  },

  camera: {
    template: () => `
      ${ICONS.cameraLive}${ICONS.cameraOff}
      <div class="label">CAMERA</div>
      <div class="sub camera-state">off</div>`,
    update(tile, d) {
      tile.classList.toggle('live', d.inUse);
      setText(tile, '.camera-state', d.inUse ? (d.apps || 'in use') : 'off');
    }
  },

  clock: {
    template: () => `<div class="wc-rows"></div>`,
    update(tile, d) {
      const host = q(tile, '.wc-rows');
      host.innerHTML = '';
      for (const c of d.cities) {
        const row = document.createElement('div');
        row.className = 'wc-row' + (c.local ? ' local' : '');
        row.innerHTML = '<span class="wc-city"></span><span class="wc-day"></span><span class="wc-time"></span>';
        row.children[0].textContent = c.label;
        row.children[1].textContent = c.day === 0 ? '' : (c.day > 0 ? '+' + c.day : String(c.day));
        row.children[2].textContent = c.time;
        host.append(row);
      }
    }
  },

  mixer: {
    context: 'open',
    template: () => `<div class="mx-rows"></div><div class="device mx-empty">no audio apps</div>`,
    update(tile, d, variant, send) {
      if (!tile.mixerDragging) renderMixer(tile, d.rows, d.apps, send);
    }
  },

  pomodoro: {
    click: 'press',
    context: 'reset',
    template: () => `<div class="label">POMODORO</div><div class="time">25:00</div><div class="sub">ready</div>`,
    update(tile, p) {
      tile.classList.toggle('work', p.phase === 'work');
      tile.classList.toggle('break', p.phase === 'break');
      tile.classList.toggle('awaiting', p.awaiting);
      const blocks = p.blocks ? ` · ${p.blocks} done` : '';
      setText(tile, '.time', p.phase === 'idle' ? '25:00' : p.remaining);
      setText(tile, '.sub',
        p.phase === 'work' ? 'work' + blocks
        : p.phase === 'break' ? 'break — step away' + blocks
        : p.awaiting ? 'break over · click to resume' + blocks
        : 'ready' + blocks);
    }
  },

  stopwatch: {
    click: 'press',
    context: 'reset',
    template: () => `<div class="label">STOPWATCH</div><div class="time">00:00</div><div class="sub">click to start</div>`,
    update(tile, w) {
      tile.classList.toggle('running', w.running);
      setText(tile, '.time', w.elapsed);
      setText(tile, '.sub', w.running ? 'running' : (w.hasElapsed ? 'stopped · right-click to reset' : 'click to start'));
    }
  },

  preset: {
    click: 'press',
    deletable: true,
    template: () => `<div class="label"></div><div class="sub"></div>`,
    update(tile, d) {
      tile.classList.toggle('busy', d.state === 'running');
      setText(tile, '.label', d.name);
      setText(tile, '.sub', d.state === 'running' ? 'working…'
        : (d.summary || `${d.count} window${d.count === 1 ? '' : 's'}`));
    }
  },

  shortcut: {
    click: 'press',
    deletable: true,
    template: () => `<div class="label"></div><div class="sub"></div>`,
    update(tile, d) {
      setText(tile, '.label', d.label);
      setText(tile, '.sub', d.note);
    }
  }
};
