const DICE_PIPS = { 1: [4], 2: [0, 8], 3: [0, 4, 8], 4: [0, 2, 6, 8], 5: [0, 2, 4, 6, 8], 6: [0, 2, 3, 5, 6, 8] };
const DICE_LABELS = { d6: 'D6', d20: 'D20', coin: 'COIN' };

function diceRandom(max) {
  const buf = new Uint32Array(1);
  const limit = Math.floor(0x100000000 / max) * max;
  do {
    crypto.getRandomValues(buf);
  } while (buf[0] >= limit);
  return (buf[0] % max) + 1;
}

function diceValue(mode) {
  if (mode === 'coin') return diceRandom(2) === 1 ? 'heads' : 'tails';
  return String(diceRandom(mode === 'd20' ? 20 : 6));
}

function drawDiceFace(tile, mode, value) {
  const face = q(tile, '.dice-face');
  face.innerHTML = '';
  face.className = 'dice-face ' + (value == null ? 'empty' : mode);
  if (value == null) {
    face.textContent = '?';
    return;
  }
  if (mode === 'd6') {
    const on = new Set(DICE_PIPS[value] || []);
    for (let i = 0; i < 9; i++) {
      const pip = document.createElement('span');
      pip.className = on.has(i) ? 'pip' : 'pip off';
      face.append(pip);
    }
  } else if (mode === 'coin') {
    face.textContent = value === 'heads' ? 'H' : 'T';
  } else {
    face.textContent = value;
  }
}

function showDiceResult(tile, mode, value) {
  drawDiceFace(tile, mode, value);
  setText(tile, '.dice-result',
    value == null ? 'click to roll'
    : mode === 'coin' ? (value === 'heads' ? 'Heads' : 'Tails')
    : '');
}

function rollDice(tile, send) {
  const state = tile.dice;
  if (state.rolling) return;
  state.rolling = true;
  tile.classList.add('rolling');
  setText(tile, '.dice-result', '');
  const flicker = setInterval(() => drawDiceFace(tile, state.mode, diceValue(state.mode)), 70);
  setTimeout(() => {
    clearInterval(flicker);
    const value = diceValue(state.mode);
    state.rolling = false;
    tile.classList.remove('rolling');
    showDiceResult(tile, state.mode, value);
    send('rolled:' + value);
  }, 900);
}

Widgets.dice = {
  context: 'mode',
  template: () => `
    <div class="dice-hit"></div>
    <div class="label dice-mode">D6</div>
    <div class="dice-face empty">?</div>
    <div class="sub dice-result">click to roll</div>`,
  bind(tile, send) {
    tile.dice = { mode: 'd6', rolling: false };
    q(tile, '.dice-hit').addEventListener('click', () => rollDice(tile, send));
  },
  update(tile, d) {
    tile.dice.mode = d.mode;
    setText(tile, '.dice-mode', DICE_LABELS[d.mode] || 'D6');
    if (!tile.dice.rolling) showDiceResult(tile, d.mode, d.last);
    tile.title = 'Click to roll · right-click to switch between d6, d20 and a coin';
  }
};
