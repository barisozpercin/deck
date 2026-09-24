Widgets.display = {
  click: 'press',
  template: () => `
    <div class="label">DISPLAY</div>
    <div class="meter-wrap disp-meter">
      <div class="meter"><div class="track"><div class="fill disp-fill"></div></div></div>
    </div>
    <div class="disp-level">…</div>
    <div class="sub disp-tint">tint off</div>
    <div class="device disp-note"></div>`,
  bind(tile, send) {
    tile.display = {
      drag: horizontalDrag(q(tile, '.disp-meter'), () => q(tile, '.meter'), (v) => {
        q(tile, '.disp-fill').style.width = v + '%';
        setText(tile, '.disp-level', v + '%');
        send('brightness:' + v);
      }, () => send('brightness-commit'))
    };
  },
  update(tile, d) {
    tile.classList.toggle('tint', d.tint);
    tile.classList.toggle('unavailable', d.ready && d.monitors === 0);
    setText(tile, '.disp-tint', d.tint ? 'warm tint on' : 'tint off');
    if (!tile.display.drag.active) {
      q(tile, '.disp-fill').style.width = (d.ready ? d.level : 0) + '%';
      setText(tile, '.disp-level', !d.ready ? '…' : d.monitors === 0 ? 'n/a' : d.level + '%');
    }
    setText(tile, '.disp-note',
      d.unsupported.length ? 'no brightness: ' + d.unsupported.join(', ')
      : d.monitors > 1 ? d.monitors + ' screens'
      : '');
    tile.title = 'Drag the bar to dim or brighten · click to toggle the warm reading tint';
  }
};
