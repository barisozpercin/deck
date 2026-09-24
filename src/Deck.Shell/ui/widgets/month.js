Widgets.month = {
  template: (variant) => `
    <div class="mo-head">${variant === 'compact'
      ? '<span class="mo-title"></span>'
      : `<button class="mo-btn" data-msg="prev" title="Previous month">‹</button>
      <button class="mo-title" data-msg="today" title="Back to this month"></button>
      <button class="mo-btn" data-msg="next" title="Next month">›</button>`}
    </div>
    <div class="mo-grid">${['M', 'T', 'W', 'T', 'F', 'S', 'S'].map((d) => `<span class="mo-dow">${d}</span>`).join('')}</div>
    <div class="device mo-hint"></div>`,
  bind(tile, send) {
    for (const button of tile.querySelectorAll('[data-msg]')) {
      button.addEventListener('click', (e) => {
        e.stopPropagation();
        send(button.dataset.msg);
      });
    }
  },
  update(tile, d) {
    setText(tile, '.mo-title', d.title);
    const grid = q(tile, '.mo-grid');
    for (const cell of grid.querySelectorAll('.mo-day')) cell.remove();
    for (const day of d.days) {
      const cell = document.createElement('span');
      cell.className = 'mo-day' + (day.inMonth ? '' : ' out') + (day.today ? ' today' : '') + (day.events.length ? ' has' : '');
      cell.textContent = day.day;
      if (day.events.length) cell.title = day.events.join('\n');
      grid.append(cell);
    }
    setText(tile, '.mo-hint',
      d.status === 'none' ? 'connect a calendar: tray → Calendar…'
      : d.status === 'offline' ? 'calendar offline'
      : '');
  }
};
