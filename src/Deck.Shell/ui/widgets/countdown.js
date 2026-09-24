Widgets.countdown = {
  editable: true,
  template: () => `
    <div class="label cd-label"></div>
    <div class="cd-value">–</div>
    <div class="device cd-date"></div>`,
  update(tile, d) {
    for (const phase of ['ahead', 'soon', 'reached', 'past']) tile.classList.toggle(phase, d.phase === phase);
    setText(tile, '.cd-label', d.label);
    setText(tile, '.cd-value', d.value);
    setText(tile, '.cd-date', d.date);
    tile.title = 'Right-click to edit';
  }
};
