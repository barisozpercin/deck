Widgets.network = {
  template: () => `
    <div class="label">NETWORK</div>
    <div class="net-ping">–</div>
    <div class="net-rates"><span class="net-down">↓ –</span><span class="net-up">↑ –</span></div>`,
  update(tile, d) {
    for (const s of ['good', 'slow', 'bad', 'offline']) tile.classList.toggle(s, d.state === s);
    setText(tile, '.net-ping', d.ping);
    setText(tile, '.net-down', '↓ ' + d.down);
    setText(tile, '.net-up', '↑ ' + d.up);
  }
};
