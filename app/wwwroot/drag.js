"use strict";

// Pointer-driven drag & drop for app cards.
//
// Native HTML5 DnD can't auto-scroll our board, can't style the drag image and drops
// the "far column" case entirely. Here the card lifts into a ghost that follows the
// pointer, the board scrolls when you hold near its edge, and — whenever some columns
// are off-screen — a dock with every device appears at the bottom as a drop target.
const Drag = (() => {
  const THRESHOLD = 5;      // px of movement before a press becomes a drag
  const EDGE = 40;          // px from the board edge where auto-scroll kicks in
  let s = null;             // current gesture
  let swallowClick = false; // the click that follows a drop must not toggle the card

  function attach(card, row, session, fromDeviceId) {
    row.addEventListener("pointerdown", (e) => {
      if (e.button !== 0 || e.target.closest(".mute-btn")) return;
      s = { card, row, pid: session.pid, from: fromDeviceId,
            x0: e.clientX, y0: e.clientY, x: e.clientX, y: e.clientY, active: false };
      row.setPointerCapture(e.pointerId);
    });
    row.addEventListener("pointermove", (e) => { if (s && s.row === row) move(e); });
    row.addEventListener("pointerup", () => { if (s && s.row === row) finish(false); });
    row.addEventListener("pointercancel", () => { if (s && s.row === row) finish(true); });
  }

  function begin() {
    const r = s.card.getBoundingClientRect();
    s.offX = s.x0 - r.left;
    s.offY = s.y0 - r.top;
    const ghost = s.card.cloneNode(true);
    ghost.classList.add("drag-ghost");
    ghost.classList.remove("expanded");
    ghost.style.width = r.width + "px";
    document.body.appendChild(ghost);
    s.ghost = ghost;
    s.card.classList.add("drag-source");
    document.body.classList.add("is-dragging");
    interacting = true;
    showDock(s.from);
    s.active = true;
    s.raf = requestAnimationFrame(tick);
  }

  function move(e) {
    s.x = e.clientX; s.y = e.clientY;
    if (!s.active) {
      if (Math.hypot(s.x - s.x0, s.y - s.y0) < THRESHOLD) return;
      begin();
    }
    s.ghost.style.transform = `translate(${s.x - s.offX}px, ${s.y - s.offY}px) rotate(1.5deg)`;
    setTarget(hitTest(s.x, s.y));
  }

  // Auto-scroll runs every frame so holding still near the edge keeps scrolling.
  function tick() {
    if (!s || !s.active) return;
    const board = document.getElementById("board");
    const r = board.getBoundingClientRect();
    const dock = document.getElementById("dock");
    const bottom = dock.classList.contains("show") ? dock.getBoundingClientRect().top : r.bottom;
    let v = 0;
    if (s.y < r.top + EDGE) v = -(r.top + EDGE - s.y);
    else if (s.y > bottom - EDGE && s.y < bottom) v = s.y - (bottom - EDGE);
    if (v) {
      board.scrollTop += v * 0.35;
      setTarget(hitTest(s.x, s.y));
    }
    s.raf = requestAnimationFrame(tick);
  }

  function hitTest(x, y) {
    const el = document.elementFromPoint(x, y);
    if (!el) return null;
    const hit = el.closest(".dock-chip, .column");
    return hit ? hit.dataset.deviceId : null;
  }

  function setTarget(id) {
    if (s.target === id) return;
    s.target = id;
    document.querySelectorAll(".column, .dock-chip").forEach(el =>
      el.classList.toggle("drop", !!id && el.dataset.deviceId === id && id !== s.from));
  }

  function finish(cancel) {
    const g = s;
    s = null;
    if (!g.active) return;          // plain click: let the row's click handler run
    swallowClick = true;
    setTimeout(() => { swallowClick = false; }, 0);
    cancelAnimationFrame(g.raf);

    const ghostRect = g.ghost.getBoundingClientRect();
    g.ghost.remove();
    g.card.classList.remove("drag-source");
    document.body.classList.remove("is-dragging");
    document.querySelectorAll(".drop").forEach(el => el.classList.remove("drop"));
    hideDock();
    interacting = false;

    if (!cancel && g.target && g.target !== g.from) {
      routeOptimistic(g.pid, g.target, ghostRect);
    } else if (pendingRender) {
      pendingRender = false;
      render();
    }
  }

  // Move the card now; the backend confirms within a tick. lastJson is reset so the
  // next state from the app re-renders even if the route silently failed.
  function routeOptimistic(pid, deviceId, fromRect) {
    const dev = state.devices.find(d => d.id === deviceId);
    if (!dev) return;
    const target = dev.isDefault ? "" : dev.id;
    const sess = state.sessions.find(x => x.pid === pid);
    if (sess) sess.outputDeviceId = target;
    lastJson = "";
    pendingRender = false;
    Motion.seed(pid, fromRect);
    render();
    send({ cmd: "route", pid: pid, deviceId: target });
  }

  // ---- dock: drop targets for columns that are scrolled out of view ----
  function showDock(fromId) {
    const board = document.getElementById("board");
    if (board.scrollHeight <= board.clientHeight + 4) return;   // everything visible
    const dock = document.getElementById("dock");
    dock.innerHTML = `<span class="dock-label">${t("drop_on")}</span>`;
    visibleDevices().filter(d => d.id !== fromId).forEach(d => {
      const chip = document.createElement("div");
      chip.className = "dock-chip";
      chip.dataset.deviceId = d.id;
      chip.innerHTML = `${deviceIcon(d.bus)}<span>${escapeHtml(d.name)}</span>`;
      dock.appendChild(chip);
    });
    dock.classList.add("show");
  }
  function hideDock() { document.getElementById("dock").classList.remove("show"); }

  document.addEventListener("keydown", (e) => {
    if (e.key === "Escape" && s && s.active) { s.target = null; finish(true); }
  });

  return {
    attach,
    isDragging: () => !!(s && s.active),
    consumeClick: () => swallowClick,
  };
})();
