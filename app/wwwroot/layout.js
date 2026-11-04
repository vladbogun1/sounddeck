"use strict";

// Board layout: how many columns, how big the window is, and how cards animate
// between renders. render() rebuilds the DOM, so continuity is faked with FLIP:
// remember where each card was, render, then animate from the old spot to the new.
const Motion = (() => {
  const reduced = () => window.matchMedia("(prefers-reduced-motion: reduce)").matches;
  let seeded = new Map();   // pid -> rect to animate from (e.g. where a drag ended)

  function capture() {
    const m = new Map();
    document.querySelectorAll("#board .card[data-pid]").forEach(c =>
      m.set(c.dataset.pid, c.getBoundingClientRect()));
    for (const [k, r] of seeded) m.set(k, r);
    seeded = new Map();
    return m;
  }

  function play(prev, firstRender) {
    if (reduced()) return;
    document.querySelectorAll("#board .card[data-pid]").forEach(c => {
      const was = prev.get(c.dataset.pid);
      if (!was) {
        if (!firstRender) c.animate(
          [{ opacity: 0, transform: "translateY(6px) scale(0.97)" }, { opacity: 1, transform: "none" }],
          { duration: 200, easing: "cubic-bezier(.2,.8,.2,1)" });
        return;
      }
      const now = c.getBoundingClientRect();
      const dx = was.left - now.left, dy = was.top - now.top;
      if (Math.abs(dx) + Math.abs(dy) < 1) return;
      c.animate(
        [{ transform: `translate(${dx}px, ${dy}px)` }, { transform: "none" }],
        { duration: 280, easing: "cubic-bezier(.2,.8,.2,1)" });
    });
  }

  return { capture, play, seed: (pid, rect) => seeded.set(String(pid), rect) };
})();

const Layout = (() => {
  const COL_W = 236, GAP = 12, PAD_X = 28, PAD_BOTTOM = 12, MAX_COLS = 4;
  let last = "";

  // Columns per row: every visible device side by side while they fit on screen,
  // then wrap into rows — never a horizontal scrollbar.
  function columns(n) {
    const fit = Math.floor((screen.availWidth - 24 - PAD_X + GAP) / (COL_W + GAP));
    return Math.max(1, Math.min(n, MAX_COLS, fit));
  }

  function apply() {
    const cols = columns(visibleDevices().length || 1);
    document.documentElement.style.setProperty("--cols", cols);
    fitWindow(cols);
  }

  // Ask the host window for exactly the size the board needs (it clamps to the
  // screen; anything taller scrolls vertically).
  function fitWindow(cols) {
    if (!bridge) return;
    const board = document.getElementById("board");
    board.classList.add("measuring");
    let bottom = 0;
    for (const col of board.children) bottom = Math.max(bottom, col.offsetTop + col.offsetHeight);
    board.classList.remove("measuring");
    const width = cols * COL_W + (cols - 1) * GAP + PAD_X;
    const height = Math.ceil(board.getBoundingClientRect().top + bottom + PAD_BOTTOM);
    const key = width + "x" + height;
    if (key === last) return;
    last = key;
    send({ cmd: "fit", width, height });
  }

  return { apply };
})();
