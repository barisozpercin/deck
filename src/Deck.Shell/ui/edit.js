const Edit = (() => {
  const stage = document.getElementById('stage');

  const library = document.createElement('section');
  library.id = 'library';
  library.hidden = true;
  stage.append(library);

  const drop = document.createElement('div');
  drop.id = 'drop';
  drop.hidden = true;

  const bar = document.createElement('footer');
  bar.id = 'editbar';
  bar.innerHTML = '<span>Drag a tile to move it · × sends it to the library · + adds a widget</span><button class="done">Done</button>';
  bar.querySelector('.done').addEventListener('click', () => layoutOp({ op: 'done' }));
  document.body.append(bar);

  let libraryCell = null;

  const sameWidget = (a, b) => keyOf(a.kind, a.ref) === keyOf(b.kind, b.ref);

  function occupied(except) {
    const cells = new Map();
    for (const p of layout.placements) {
      if (except && sameWidget(p, except)) continue;
      for (let c = p.col; c < p.col + p.w; c++) {
        for (let r = p.row; r < p.row + p.h; r++) cells.set(c + ',' + r, p);
      }
    }
    return cells;
  }

  function fits(w, h, col, row, except) {
    if (col < 0 || row < 0 || col + w > layout.columns || row + h > layout.rows) return false;
    const cells = occupied(except);
    for (let c = col; c < col + w; c++) {
      for (let r = row; r < row + h; r++) {
        if (cells.has(c + ',' + r)) return false;
      }
    }
    return true;
  }

  function dropOutcome(p, col, row) {
    if (col === p.col && row === p.row) return 'none';
    if (fits(p.w, p.h, col, row, p)) return 'move';
    const other = occupied(p).get(col + ',' + row);
    return other && other.w === p.w && other.h === p.h ? 'swap' : 'bad';
  }

  function cellAt(x, y) {
    const box = grid.getBoundingClientRect();
    const style = getComputedStyle(grid);
    const padX = parseFloat(style.paddingLeft);
    const padY = parseFloat(style.paddingTop);
    const gapX = parseFloat(style.columnGap) || 0;
    const gapY = parseFloat(style.rowGap) || 0;
    const cellW = (box.width - 2 * padX - (layout.columns - 1) * gapX) / layout.columns;
    const cellH = (box.height - 2 * padY - (layout.rows - 1) * gapY) / layout.rows;
    return {
      col: Math.floor((x - box.left - padX + gapX / 2) / (cellW + gapX)),
      row: Math.floor((y - box.top - padY + gapY / 2) / (cellH + gapY))
    };
  }

  function showDrop(p, target, outcome) {
    const inside = target.col >= 0 && target.row >= 0
      && target.col + p.w <= layout.columns && target.row + p.h <= layout.rows;
    if (outcome === 'none' || !inside) {
      drop.hidden = true;
      return;
    }
    drop.hidden = false;
    drop.className = outcome === 'bad' ? 'bad' : 'ok';
    drop.style.gridColumn = `${target.col + 1} / span ${p.w}`;
    drop.style.gridRow = `${target.row + 1} / span ${p.h}`;
  }

  function startDrag(e, tile, p, handle) {
    e.preventDefault();
    const origin = cellAt(e.clientX, e.clientY);
    const grab = { col: origin.col - p.col, row: origin.row - p.row };
    const start = { x: e.clientX, y: e.clientY };
    let target = null;

    handle.setPointerCapture(e.pointerId);
    tile.classList.add('lifted');

    const move = (ev) => {
      tile.style.transform = `translate(${ev.clientX - start.x}px, ${ev.clientY - start.y}px)`;
      const at = cellAt(ev.clientX, ev.clientY);
      target = { col: at.col - grab.col, row: at.row - grab.row };
      showDrop(p, target, dropOutcome(p, target.col, target.row));
    };

    const end = (ev) => {
      handle.removeEventListener('pointermove', move);
      handle.removeEventListener('pointerup', end);
      handle.removeEventListener('pointercancel', end);
      try { handle.releasePointerCapture(ev.pointerId); } catch (_) {}
      tile.classList.remove('lifted');
      tile.style.transform = '';
      drop.hidden = true;

      const outcome = target ? dropOutcome(p, target.col, target.row) : 'none';
      if (ev.type === 'pointerup' && (outcome === 'move' || outcome === 'swap')) {
        layoutOp({ op: 'move', kind: p.kind, ref: p.ref, col: target.col, row: target.row });
      }
    };

    handle.addEventListener('pointermove', move);
    handle.addEventListener('pointerup', end);
    handle.addEventListener('pointercancel', end);
  }

  function decorateTile(tile, p) {
    if (!layout.editing) return;

    const shield = document.createElement('div');
    shield.className = 'shield';
    shield.addEventListener('pointerdown', (e) => startDrag(e, tile, p, shield));

    const remove = document.createElement('button');
    remove.className = 'remove';
    remove.textContent = '×';
    remove.title = 'Send to the library';
    remove.addEventListener('pointerdown', (e) => e.stopPropagation());
    remove.addEventListener('click', (e) => {
      e.stopPropagation();
      layoutOp({ op: 'remove', kind: p.kind, ref: p.ref });
    });

    tile.append(shield, remove);
  }

  function decorateEmpty(cell, col, row) {
    if (layout.editing) {
      cell.classList.add('add');
      cell.innerHTML = '<div class="label">+</div>';
      cell.title = 'Add a widget here';
      cell.addEventListener('click', () => {
        libraryCell = { col, row };
        renderLibrary();
      });
    } else {
      cell.title = 'Click to edit the deck';
      cell.addEventListener('click', () => layoutOp({ op: 'edit' }));
    }
  }

  function closeLibrary() {
    libraryCell = null;
    library.hidden = true;
  }

  function needs(w, h) {
    return w === 2 && h === 2 ? 'needs a 2×2 space here' : 'needs ' + (w * h) + ' free cells here';
  }

  function card(title, detail, disabled, onPick, onDelete) {
    const el = document.createElement('div');
    el.className = 'card' + (disabled ? ' disabled' : '');
    el.innerHTML = '<div class="card-title"></div><div class="card-detail"></div>';
    el.querySelector('.card-title').textContent = title;
    el.querySelector('.card-detail').textContent = detail;
    if (!disabled) el.addEventListener('click', onPick);
    el.addEventListener('contextmenu', (e) => {
      e.preventDefault();
      if (onDelete) onDelete();
    });
    return el;
  }

  function renderLibrary() {
    if (!libraryCell || !layout.editing) {
      closeLibrary();
      return;
    }

    const { col, row } = libraryCell;
    library.hidden = false;
    library.innerHTML = '<header><button class="back">‹ Back</button><span>Add to the deck</span></header><div class="cards"></div>';
    library.querySelector('.back').addEventListener('click', closeLibrary);
    const cards = library.querySelector('.cards');

    cards.append(card('New preset', 'save the current window layout', false, () => {
      closeLibrary();
      layoutOp({ op: 'new-preset', col, row });
    }, null));

    for (const item of layout.library) {
      for (const v of item.variants) {
        const ok = fits(v.w, v.h, col, row, null);
        cards.append(card(
          item.title,
          ok ? (item.group || v.label) : needs(v.w, v.h),
          !ok,
          () => {
            closeLibrary();
            layoutOp({ op: 'place', kind: item.kind, variant: v.variant, ref: item.ref, col, row });
          },
          item.ref ? () => layoutOp({ op: 'delete', kind: item.kind, ref: item.ref }) : null));
      }
    }
  }

  function afterRender() {
    document.body.classList.toggle('editing', layout.editing);
    drop.hidden = true;
    grid.append(drop);
    renderLibrary();
  }

  return { decorateTile, decorateEmpty, afterRender };
})();
