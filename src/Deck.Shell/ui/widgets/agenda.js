function agendaNote(d, first) {
  if (d.status === 'none') return 'connect a calendar: tray → Calendar…';
  if (d.status === 'offline') return 'calendar offline';
  if (!first) return 'nothing in the next 7 days';
  return first.link ? 'click to join' : 'click to open';
}

Widgets.agenda = {
  click: 'press',
  context: 'next',
  template: (variant) => variant === 'wide'
    ? `<div class="label">AGENDA</div><div class="ag-rows"></div><div class="device ag-note"></div>`
    : `<div class="label">AGENDA</div>
       <div class="ag-main">–</div>
       <div class="sub ag-main-when"></div>
       <div class="device ag-note"></div>`,
  update(tile, d, variant) {
    const first = d.events[0];
    for (const s of ['now', 'soon', 'later']) tile.classList.toggle(s, !!first && first.state === s);
    setText(tile, '.ag-note', agendaNote(d, first));

    if (variant === 'wide') {
      const host = q(tile, '.ag-rows');
      host.innerHTML = '';
      d.events.forEach((e, i) => {
        const row = document.createElement('div');
        row.className = 'ag-row ' + e.state + (i === 0 ? ' focus' : '');
        row.innerHTML = '<span class="ag-title"></span><span class="ag-when"></span>';
        row.children[0].textContent = (e.link ? '● ' : '') + e.title;
        row.children[1].textContent = e.when;
        host.append(row);
      });
    } else {
      setText(tile, '.ag-main', first ? first.title : '');
      setText(tile, '.ag-main-when', first ? first.when : '');
    }

    tile.title = d.total > 1
      ? 'Click to join · right-click for the next event (' + d.total + ' coming up)'
      : 'Click to join';
  }
};
