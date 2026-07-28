"use strict";

const bridge = window.chrome && window.chrome.webview;
let state = { devices: [], sessions: [], profiles: [], settings: {}, mirrorActive: false };
const expanded = new Set();      // pids with volume slider open
let interacting = false;         // true while dragging a slider (pause re-render)
let dragPid = null;

function send(msg) { if (bridge) bridge.postMessage(msg); }

let lastJson = "";
let pendingRender = false;
if (bridge) {
  bridge.addEventListener("message", (e) => {
    const d = e.data;
    if (d && d.type === "closePops") { closePops(); return; }
    state = d;
    const json = JSON.stringify(state);
    if (json === lastJson) return;   // nothing changed → no flicker
    lastJson = json;
    if (!interacting) render();
    else pendingRender = true;
  });
}

// ---- icons (FontAwesome) ----
function deviceIcon(bus) {
  if (bus === "HDMI") return '<i class="fa-solid fa-tv"></i>';
  if (bus === "USB") return '<i class="fa-solid fa-headphones"></i>';
  if (bus === "BT") return '<i class="fa-brands fa-bluetooth-b"></i>';
  return '<i class="fa-solid fa-volume-high"></i>';
}

// ---- render ----
function render() {
  applyTheme();
  renderHealth();
  renderBoard();
  renderProfiles();
  renderSettings();
}

function renderHealth() {
  const line = document.getElementById("healthLine");
  const def = state.devices.find(d => d.isDefault);
  line.textContent = def ? `по умолчанию: ${def.name}` : "нет устройства по умолчанию";
}

function applyTheme() {
  document.documentElement.dataset.theme = state.settings.theme || "neu";
}

function sessionsForDevice(dev) {
  return state.sessions.filter(s =>
    s.outputDeviceId === dev.id || (s.outputDeviceId === "" && dev.isDefault));
}

function renderBoard() {
  const board = document.getElementById("board");
  board.innerHTML = "";

  state.devices.forEach(dev => {
    const col = document.createElement("div");
    col.className = "column" + (dev.isDefault ? " default" : "");
    col.dataset.deviceId = dev.id;

    // Mirror target reflects the engine's real state, not just stored settings.
    const isMirrorTarget = state.mirrorActive && state.mirrorTargetDeviceId === dev.id;

    col.innerHTML = `
      <div class="col-head">
        <span class="col-icon">${deviceIcon(dev.bus)}</span>
        <span class="col-name">${escapeHtml(dev.name)}</span>
        ${dev.isDefault ? '<span class="badge">по умолч.</span>' : ""}
        ${isMirrorTarget ? '<span class="badge mirror">дубль</span>' : ""}
      </div>
      <div class="col-sub">${dev.volume}%${dev.muted ? " · выкл" : ""}</div>
      <div class="col-vol"></div>
      <div class="col-tools"></div>
      <div class="cards"></div>
    `;

    // device volume slider on its own row
    const dslider = buildSlider(dev.volume, (v) => {
      send({ cmd: "setDeviceVolume", deviceId: dev.id, value: v });
    });
    dslider.style.flex = "1";
    col.querySelector(".col-vol").appendChild(dslider);

    const tools = col.querySelector(".col-tools");
    if (!dev.isDefault) {
      const setDef = document.createElement("button");
      setDef.className = "mini";
      setDef.innerHTML = '<i class="fa-solid fa-star"></i> сделать осн.';
      setDef.onclick = () => send({ cmd: "setDefault", deviceId: dev.id });
      tools.appendChild(setDef);

      const mir = document.createElement("button");
      mir.className = "mini" + (isMirrorTarget ? " active" : "");
      mir.innerHTML = isMirrorTarget
        ? '<i class="fa-solid fa-minus"></i> дубль'
        : '<i class="fa-solid fa-clone"></i> дубль';
      mir.title = "Дублировать сюда основной звук (и там, и там сразу)";
      mir.onclick = () => send({ cmd: "setMirror", deviceId: isMirrorTarget ? null : dev.id });
      tools.appendChild(mir);
    } else {
      tools.style.display = "none";
    }

    const cards = col.querySelector(".cards");
    const list = sessionsForDevice(dev);
    if (list.length === 0) {
      cards.innerHTML = '<div class="empty">перетащи приложение сюда</div>';
    } else {
      list.forEach(s => cards.appendChild(buildCard(s)));
    }

    setupDrop(col, dev);
    board.appendChild(col);
  });
}

function buildCard(s) {
  const card = document.createElement("div");
  card.className = "card" + (expanded.has(s.pid) ? " expanded" : "");
  card.draggable = false;   // enabled only when the grab starts off the slider
  card.dataset.pid = s.pid;

  const ico = s.iconB64
    ? `<img src="${s.iconB64}" alt="" />`
    : escapeHtml((s.name || "?").charAt(0).toUpperCase());

  const row = document.createElement("div");
  row.className = "card-row";
  row.innerHTML = `
    <span class="grip" title="Перетащи на другое устройство"><i class="fa-solid fa-grip-vertical"></i></span>
    <div class="app-ico">${ico}</div>
    <div class="app-meta">
      <div class="app-name">${escapeHtml(s.name)}</div>
      <div class="app-hint">${s.volume}%</div>
    </div>
    <button class="mute-btn ${s.muted ? "muted" : ""}" title="Заглушить"><i class="fa-solid ${s.muted ? "fa-volume-xmark" : "fa-volume-high"}"></i></button>
  `;
  row.querySelector(".mute-btn").onclick = (e) => {
    e.stopPropagation();
    send({ cmd: "setSessionMute", pid: s.pid, value: !s.muted });
  };
  row.onclick = () => {
    if (expanded.has(s.pid)) expanded.delete(s.pid); else expanded.add(s.pid);
    card.classList.toggle("expanded");
  };
  card.appendChild(row);

  const wrap = document.createElement("div");
  wrap.className = "slider-wrap";
  const slider = buildSlider(s.volume, (v) => {
    send({ cmd: "setSessionVolume", pid: s.pid, value: v });
  });
  const val = document.createElement("span");
  val.className = "slider-val";
  val.textContent = s.volume + "%";
  slider._valEl = val;
  wrap.appendChild(slider);
  wrap.appendChild(val);
  card.appendChild(wrap);

  // Enable native drag only when the grab starts on the card body (row),
  // never on the volume slider or mute button.
  row.addEventListener("pointerdown", (e) => {
    card.draggable = !e.target.closest(".mute-btn");
  });
  wrap.addEventListener("pointerdown", () => { card.draggable = false; }, true);

  card.addEventListener("dragstart", (e) => {
    if (!card.draggable) { e.preventDefault(); return; }
    dragPid = s.pid;
    card.classList.add("dragging");
    e.dataTransfer.effectAllowed = "move";
    e.dataTransfer.setData("text/plain", String(s.pid));
  });
  card.addEventListener("dragend", () => {
    dragPid = null;
    card.draggable = false;
    card.classList.remove("dragging");
  });

  return card;
}

function setupDrop(col, dev) {
  col.addEventListener("dragover", (e) => { e.preventDefault(); col.classList.add("drop"); });
  col.addEventListener("dragleave", () => col.classList.remove("drop"));
  col.addEventListener("drop", (e) => {
    e.preventDefault();
    col.classList.remove("drop");
    const pid = dragPid || parseInt(e.dataTransfer.getData("text/plain"), 10);
    if (!pid) return;
    send({ cmd: "route", pid: pid, deviceId: dev.isDefault ? "" : dev.id });
  });
}

// ---- custom slider with a draggable dot ----
function buildSlider(value, onChange) {
  const el = document.createElement("div");
  el.className = "slider";
  el.innerHTML = '<div class="track"></div><div class="fill"></div><div class="knob"></div>';
  const fill = el.querySelector(".fill");
  const knob = el.querySelector(".knob");
  let val = clamp(value);

  function paint() {
    fill.style.width = val + "%";
    knob.style.left = val + "%";
    // Shift the knob back by its own fraction so it never spills past the track.
    knob.style.transform = `translate(${-val}%, -50%)`;
    if (el._valEl) el._valEl.textContent = val + "%";
  }
  paint();

  function setFromEvent(e) {
    const r = el.getBoundingClientRect();
    const x = (e.touches ? e.touches[0].clientX : e.clientX) - r.left;
    val = clamp(Math.round((x / r.width) * 100));
    paint();
    onChange(val, false);
  }

  el.addEventListener("pointerdown", (e) => {
    e.stopPropagation();
    interacting = true;
    el.classList.add("drag");
    el.setPointerCapture(e.pointerId);
    setFromEvent(e);
  });
  el.addEventListener("pointermove", (e) => {
    if (!el.classList.contains("drag")) return;
    setFromEvent(e);
  });
  el.addEventListener("pointerup", (e) => {
    el.classList.remove("drag");
    interacting = false;
    onChange(val, true);
    if (pendingRender) { pendingRender = false; render(); }
  });
  // clicking the slider shouldn't toggle the card
  el.addEventListener("click", (e) => e.stopPropagation());

  return el;
}

// ---- profiles ----
function renderProfiles() {
  const active = state.profiles.find(p => p.id === state.settings.activeProfileId);
  document.getElementById("activeProfileName").textContent = active ? active.name : "Профили";
  document.getElementById("profilesBtn").classList.toggle("has-active", !!active);

  const list = document.getElementById("profilesList");
  list.innerHTML = "";
  if (state.profiles.length === 0) {
    const hint = document.createElement("div");
    hint.className = "pop-empty";
    hint.textContent = "Пока нет профилей. Сохрани текущую раскладку кнопкой ниже.";
    list.appendChild(hint);
  }
  state.profiles.forEach(p => {
    const el = document.createElement("div");
    el.className = "prof-item" + (state.settings.activeProfileId === p.id ? " active" : "");
    el.innerHTML = `
      <i class="fa-solid ${state.settings.activeProfileId === p.id ? "fa-circle-check" : "fa-circle"} tick"></i>
      <span class="prof-name">${escapeHtml(p.name)}</span>
      <button class="prof-del" title="Удалить"><i class="fa-solid fa-trash"></i></button>`;
    el.querySelector(".prof-name").onclick = () => { send({ cmd: "applyProfile", id: p.id }); closePops(); };
    el.querySelector(".tick").onclick = () => { send({ cmd: "applyProfile", id: p.id }); closePops(); };
    el.querySelector(".prof-del").onclick = (e) => { e.stopPropagation(); send({ cmd: "deleteProfile", id: p.id }); };
    list.appendChild(el);
  });
}

const THEMES = [
  { id: "neu",        name: "Необрутализм",   sw: "linear-gradient(135deg,#ffd64b 55%,#14110b 55%)" },
  { id: "glass",      name: "Тёмное стекло",  sw: "linear-gradient(135deg,#6d8bff,#a06dff)" },
  { id: "win11",      name: "Windows 11",     sw: "linear-gradient(135deg,#f6f6f6 55%,#005fb8 55%)" },
  { id: "ios",        name: "iOS стекло",     sw: "linear-gradient(135deg,#5ac8fa,#af52de)" },
  { id: "material",   name: "Material",       sw: "linear-gradient(135deg,#fafafa 55%,#6200ee 55%)" },
  { id: "paper",      name: "Бумага",         sw: "linear-gradient(135deg,#ffffff 55%,#1f6feb 55%)" },
  { id: "solarized",  name: "Solarized",      sw: "linear-gradient(135deg,#fdf6e3 55%,#268bd2 55%)" },
  { id: "dracula",    name: "Dracula",        sw: "linear-gradient(135deg,#282a36 45%,#bd93f9 45%)" },
  { id: "nord",       name: "Nord",           sw: "linear-gradient(135deg,#2e3440 45%,#88c0d0 45%)" },
  { id: "gruvbox",    name: "Gruvbox",        sw: "linear-gradient(135deg,#282828 45%,#fabd2f 45%)" },
  { id: "monokai",    name: "Monokai",        sw: "linear-gradient(135deg,#272822 45%,#a6e22e 45%)" },
  { id: "catppuccin", name: "Catppuccin",     sw: "linear-gradient(135deg,#1e1e2e 45%,#cba6f7 45%)" },
  { id: "rose",       name: "Rosé Pine",      sw: "linear-gradient(135deg,#191724 45%,#ebbcba 45%)" },
  { id: "oled",       name: "OLED чёрный",    sw: "linear-gradient(135deg,#000000 55%,#2f81f7 55%)" },
  { id: "matrix",     name: "Matrix",         sw: "linear-gradient(135deg,#000600 45%,#00ff66 45%)" },
  { id: "forest",     name: "Лес",            sw: "linear-gradient(135deg,#0f1f17 45%,#4ade80 45%)" },
  { id: "ocean",      name: "Океан",          sw: "linear-gradient(135deg,#06243b 45%,#38bdf8 45%)" },
  { id: "sunset",     name: "Закат",          sw: "linear-gradient(135deg,#ff8a42,#ff5c96 55%,#8c3cb4)" },
  { id: "synthwave",  name: "Synthwave",      sw: "linear-gradient(135deg,#ff4fd8,#4fd1ff)" },
  { id: "cyberpunk",  name: "Cyberpunk",      sw: "linear-gradient(135deg,#0a0e17 40%,#fcee0a 40%,#00f0ff)" },
];

function renderSettings() {
  setSwitch(document.getElementById("autohealSwitch"), !!state.settings.autoHeal);
  setSwitch(document.getElementById("autostartSwitch"), !!state.settings.autostart);

  const theme = state.settings.theme || "neu";
  const cur = THEMES.find(t => t.id === theme) || THEMES[0];
  document.getElementById("themeCurrent").textContent = cur.name;
  document.getElementById("themeSw").style.background = cur.sw;

  const menu = document.getElementById("themeDropMenu");
  menu.innerHTML = "";
  THEMES.forEach(t => {
    const o = document.createElement("div");
    o.className = "theme-opt" + (t.id === theme ? " active" : "");
    o.setAttribute("role", "option");
    o.innerHTML = `<span class="sw" style="background:${t.sw}"></span>
      <span class="theme-opt-name">${t.name}</span>
      ${t.id === theme ? '<i class="fa-solid fa-check ok"></i>' : ""}`;
    o.onclick = () => {
      send({ cmd: "setTheme", value: t.id });
      menu.hidden = true;
    };
    menu.appendChild(o);
  });
}

function setSwitch(el, on) { el.classList.toggle("on", on); }

// ---- wiring ----
document.getElementById("closeBtn").onclick = () => send({ cmd: "hide" });

const settingsPop = document.getElementById("settingsPop");
const profilesPop = document.getElementById("profilesPop");
function closePops() {
  settingsPop.hidden = true;
  profilesPop.hidden = true;
  const tm = document.getElementById("themeDropMenu");
  if (tm) tm.hidden = true;
}

document.getElementById("settingsBtn").onclick = (e) => {
  e.stopPropagation();
  const show = settingsPop.hidden;
  closePops();
  settingsPop.hidden = !show;
};
document.getElementById("profilesBtn").onclick = (e) => {
  e.stopPropagation();
  const show = profilesPop.hidden;
  closePops();
  profilesPop.hidden = !show;
};
document.addEventListener("click", (e) => {
  if (e.target.closest("#settingsBtn, #profilesBtn, .settings-pop")) return;
  closePops();
});

document.getElementById("autohealSwitch").onclick = () =>
  send({ cmd: "setAutoHeal", value: !state.settings.autoHeal });
document.getElementById("autostartSwitch").onclick = () =>
  send({ cmd: "setAutostart", value: !state.settings.autostart });

const themeMenu = document.getElementById("themeDropMenu");
document.getElementById("themeDropBtn").onclick = (e) => {
  e.stopPropagation();
  themeMenu.hidden = !themeMenu.hidden;
};
document.addEventListener("click", (e) => {
  if (!e.target.closest("#themeDrop")) themeMenu.hidden = true;
});

document.getElementById("saveProfileBtn").onclick = () => {
  const name = window.prompt("Название профиля:", "Профиль");
  if (name !== null) send({ cmd: "saveProfile", name: name });
};

// ---- utils ----
function clamp(v) { return Math.max(0, Math.min(100, v | 0)); }
function escapeHtml(s) {
  return String(s).replace(/[&<>"']/g, c =>
    ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[c]));
}

send({ cmd: "getState" });

// Dev mock: when opened outside the app (no C# bridge), render sample data so the
// board can be inspected/tested in a plain browser.
if (!bridge) {
  state = {
    type: "state",
    devices: [
      { id: "hp", name: "Наушники (2- FIFINE 683 Microphone)", volume: 74, muted: false, isDefault: true, bus: "USB" },
      { id: "tv", name: "LED TV (NVIDIA High Definition Audio)", volume: 100, muted: false, isDefault: false, bus: "HDMI" },
    ],
    sessions: [
      { pid: 1, name: "Microsoft Edge", exe: "msedge.exe", iconB64: "", volume: 99, muted: false, outputDeviceId: "tv", mirrored: false },
      { pid: 2, name: "Google Chrome", exe: "chrome.exe", iconB64: "", volume: 100, muted: false, outputDeviceId: "", mirrored: false },
      { pid: 3, name: "Steam", exe: "steam.exe", iconB64: "", volume: 100, muted: false, outputDeviceId: "", mirrored: false },
      { pid: 4, name: "Telegram Desktop", exe: "telegram.exe", iconB64: "", volume: 100, muted: false, outputDeviceId: "", mirrored: false },
    ],
    profiles: [{ id: "p1", name: "Default" }],
    settings: { autoHeal: true, autostart: false, theme: "neu", activeProfileId: "p1" },
    mirrorActive: false,
  };
  render();
}
