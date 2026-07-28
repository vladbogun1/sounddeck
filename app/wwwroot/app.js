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
    if (d && d.type === "closePops") { closePops(); setSettings(false); return; }
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
  applyI18n();
  renderHealth();
  renderBoard();
  renderProfiles();
  renderSettings();
}

function setText(id, s) { const el = document.getElementById(id); if (el) el.textContent = s; }
function setTitle(id, s) { const el = document.getElementById(id); if (el) el.title = s; }

function applyI18n() {
  document.documentElement.lang = currentLang();
  setText("t_autoheal", t("autoheal_t"));  setText("d_autoheal", t("autoheal_d"));
  setText("t_autostart", t("autostart_t")); setText("d_autostart", t("autostart_d"));
  setText("t_group", t("group_t"));        setText("d_group", t("group_d"));
  setText("t_theme", t("theme_t"));         setText("t_lang", t("language_t"));
  setText("saveProfileLbl", t("save_layout"));
  setText("settingsTitle", t("settings"));
  setTitle("settingsBtn", settingsOpen ? t("back") : t("settings"));
  setTitle("closeBtn", t("minimize"));
  setTitle("profilesBtn", t("profiles"));
}

function renderHealth() {
  const line = document.getElementById("healthLine");
  const def = state.devices.find(d => d.isDefault);
  line.textContent = def ? `${t("default_prefix")} ${def.name}` : t("no_default");
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
        ${dev.isDefault ? `<span class="badge">${t("badge_default")}</span>` : ""}
        ${isMirrorTarget ? `<span class="badge mirror">${t("badge_mirror")}</span>` : ""}
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
      setDef.innerHTML = `<i class="fa-solid fa-star"></i> ${t("set_default")}`;
      setDef.onclick = () => send({ cmd: "setDefault", deviceId: dev.id });
      tools.appendChild(setDef);

      const mir = document.createElement("button");
      mir.className = "mini" + (isMirrorTarget ? " active" : "");
      mir.innerHTML = isMirrorTarget
        ? `<i class="fa-solid fa-minus"></i> ${t("mirror")}`
        : `<i class="fa-solid fa-clone"></i> ${t("mirror")}`;
      mir.onclick = () => send({ cmd: "setMirror", deviceId: isMirrorTarget ? null : dev.id });
      tools.appendChild(mir);
    } else {
      tools.style.display = "none";
    }

    const cards = col.querySelector(".cards");
    const list = sessionsForDevice(dev);
    if (list.length === 0) {
      cards.innerHTML = `<div class="empty">${t("drop_here")}</div>`;
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
    <span class="grip" title="${t("drag_hint")}"><i class="fa-solid fa-grip-vertical"></i></span>
    <div class="app-ico">${ico}</div>
    <div class="app-meta">
      <div class="app-name">${escapeHtml(s.name)}</div>
      <div class="app-hint">${s.volume}%</div>
    </div>
    <button class="mute-btn ${s.muted ? "muted" : ""}" title="${t("mute")}"><i class="fa-solid ${s.muted ? "fa-volume-xmark" : "fa-volume-high"}"></i></button>
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
  document.getElementById("activeProfileName").textContent = active ? active.name : t("profiles");
  document.getElementById("profilesBtn").classList.toggle("has-active", !!active);

  const list = document.getElementById("profilesList");
  list.innerHTML = "";
  if (state.profiles.length === 0) {
    const hint = document.createElement("div");
    hint.className = "pop-empty";
    hint.textContent = t("no_profiles");
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
  { id: "neu",        name: "Neubrutalism", sw: "linear-gradient(135deg,#ffd64b 55%,#14110b 55%)" },
  { id: "glass",      name: "Dark glass",   sw: "linear-gradient(135deg,#6d8bff,#a06dff)" },
  { id: "win11",      name: "Windows 11",   sw: "linear-gradient(135deg,#f6f6f6 55%,#005fb8 55%)" },
  { id: "ios",        name: "iOS glass",    sw: "linear-gradient(135deg,#5ac8fa,#af52de)" },
  { id: "material",   name: "Material",     sw: "linear-gradient(135deg,#fafafa 55%,#6200ee 55%)" },
  { id: "paper",      name: "Paper",        sw: "linear-gradient(135deg,#ffffff 55%,#1f6feb 55%)" },
  { id: "solarized",  name: "Solarized",    sw: "linear-gradient(135deg,#fdf6e3 55%,#268bd2 55%)" },
  { id: "dracula",    name: "Dracula",      sw: "linear-gradient(135deg,#282a36 45%,#bd93f9 45%)" },
  { id: "nord",       name: "Nord",         sw: "linear-gradient(135deg,#2e3440 45%,#88c0d0 45%)" },
  { id: "gruvbox",    name: "Gruvbox",      sw: "linear-gradient(135deg,#282828 45%,#fabd2f 45%)" },
  { id: "monokai",    name: "Monokai",      sw: "linear-gradient(135deg,#272822 45%,#a6e22e 45%)" },
  { id: "catppuccin", name: "Catppuccin",   sw: "linear-gradient(135deg,#1e1e2e 45%,#cba6f7 45%)" },
  { id: "rose",       name: "Rosé Pine",    sw: "linear-gradient(135deg,#191724 45%,#ebbcba 45%)" },
  { id: "oled",       name: "OLED",         sw: "linear-gradient(135deg,#000000 55%,#2f81f7 55%)" },
  { id: "matrix",     name: "Matrix",       sw: "linear-gradient(135deg,#000600 45%,#00ff66 45%)" },
  { id: "forest",     name: "Forest",       sw: "linear-gradient(135deg,#0f1f17 45%,#4ade80 45%)" },
  { id: "ocean",      name: "Ocean",        sw: "linear-gradient(135deg,#06243b 45%,#38bdf8 45%)" },
  { id: "sunset",     name: "Sunset",       sw: "linear-gradient(135deg,#ff8a42,#ff5c96 55%,#8c3cb4)" },
  { id: "synthwave",  name: "Synthwave",    sw: "linear-gradient(135deg,#ff4fd8,#4fd1ff)" },
  { id: "cyberpunk",  name: "Cyberpunk",    sw: "linear-gradient(135deg,#0a0e17 40%,#fcee0a 40%,#00f0ff)" },
];

// ---- i18n ----
const LANGS = [
  { id: "en", name: "English" },
  { id: "ru", name: "Русский" },
  { id: "uk", name: "Українська" },
  { id: "zh", name: "中文" },
  { id: "es", name: "Español" },
  { id: "de", name: "Deutsch" },
  { id: "fr", name: "Français" },
  { id: "ja", name: "日本語" },
];
const I18N = {
  en: { default_prefix:"default:", no_default:"no default device", set_default:"make default", mirror:"mirror", badge_default:"default", badge_mirror:"mirror", drop_here:"drop an app here", profiles:"Profiles", save_layout:"Save current layout", no_profiles:"No profiles yet. Save the current layout with the button below.", mute:"Mute", drag_hint:"Drag to another device", settings:"Settings", minimize:"Minimize to tray", autoheal_t:"Auto-heal", autoheal_d:"Restores your layout if Windows resets audio", autostart_t:"Launch on login", autostart_d:"Start SoundDeck when you sign in", group_t:"Group app processes", group_d:"Show multi-process apps (Spotify, Chrome…) as one card", theme_t:"Theme", back:"Back to board", language_t:"Language", save_prompt:"Profile name:", profile_default:"Profile" },
  ru: { default_prefix:"по умолчанию:", no_default:"нет устройства по умолчанию", set_default:"сделать осн.", mirror:"дубль", badge_default:"по умолч.", badge_mirror:"дубль", drop_here:"перетащи приложение сюда", profiles:"Профили", save_layout:"Сохранить текущую раскладку", no_profiles:"Профилей пока нет. Сохрани текущую раскладку кнопкой ниже.", mute:"Заглушить", drag_hint:"Перетащи на другое устройство", settings:"Настройки", minimize:"Свернуть в трей", autoheal_t:"Авто-лечилка", autoheal_d:"Возвращает раскладку, если Windows сбросил звук", autostart_t:"Автозапуск", autostart_d:"Запускать SoundDeck при входе в систему", group_t:"Группировать процессы", group_d:"Показывать многопроцессные приложения (Spotify, Chrome…) одной карточкой", theme_t:"Тема", back:"На доску", language_t:"Язык", save_prompt:"Название профиля:", profile_default:"Профиль" },
  uk: { default_prefix:"за замовчуванням:", no_default:"немає пристрою за замовчуванням", set_default:"зробити осн.", mirror:"дубль", badge_default:"за замовч.", badge_mirror:"дубль", drop_here:"перетягни застосунок сюди", profiles:"Профілі", save_layout:"Зберегти поточне розкладання", no_profiles:"Профілів поки немає. Збережи поточне розкладання кнопкою нижче.", mute:"Вимкнути звук", drag_hint:"Перетягни на інший пристрій", settings:"Налаштування", minimize:"Згорнути в трей", autoheal_t:"Авто-відновлення", autoheal_d:"Повертає розкладання, якщо Windows скинув звук", autostart_t:"Автозапуск", autostart_d:"Запускати SoundDeck під час входу", group_t:"Групувати процеси", group_d:"Показувати багатопроцесні застосунки (Spotify, Chrome…) однією карткою", theme_t:"Тема", back:"На дошку", language_t:"Мова", save_prompt:"Назва профілю:", profile_default:"Профіль" },
  zh: { default_prefix:"默认：", no_default:"没有默认设备", set_default:"设为默认", mirror:"镜像", badge_default:"默认", badge_mirror:"镜像", drop_here:"将应用拖到这里", profiles:"配置", save_layout:"保存当前布局", no_profiles:"还没有配置。用下面的按钮保存当前布局。", mute:"静音", drag_hint:"拖到其他设备", settings:"设置", minimize:"最小化到托盘", autoheal_t:"自动修复", autoheal_d:"当 Windows 重置音频时恢复你的布局", autostart_t:"开机启动", autostart_d:"登录时启动 SoundDeck", group_t:"合并应用进程", group_d:"将多进程应用（Spotify、Chrome…）显示为一张卡片", theme_t:"主题", back:"返回主界面", language_t:"语言", save_prompt:"配置名称：", profile_default:"配置" },
  es: { default_prefix:"predeterminado:", no_default:"sin dispositivo predeterminado", set_default:"predeterminar", mirror:"duplicar", badge_default:"predet.", badge_mirror:"duplicado", drop_here:"arrastra una app aquí", profiles:"Perfiles", save_layout:"Guardar diseño actual", no_profiles:"Aún no hay perfiles. Guarda el diseño actual con el botón de abajo.", mute:"Silenciar", drag_hint:"Arrastra a otro dispositivo", settings:"Ajustes", minimize:"Minimizar a la bandeja", autoheal_t:"Auto-reparación", autoheal_d:"Restaura tu diseño si Windows reinicia el audio", autostart_t:"Iniciar con el sistema", autostart_d:"Abrir SoundDeck al iniciar sesión", group_t:"Agrupar procesos", group_d:"Mostrar apps multiproceso (Spotify, Chrome…) como una tarjeta", theme_t:"Tema", back:"Volver al tablero", language_t:"Idioma", save_prompt:"Nombre del perfil:", profile_default:"Perfil" },
  de: { default_prefix:"Standard:", no_default:"kein Standardgerät", set_default:"als Standard", mirror:"Spiegeln", badge_default:"Standard", badge_mirror:"Spiegel", drop_here:"App hierher ziehen", profiles:"Profile", save_layout:"Aktuelles Layout speichern", no_profiles:"Noch keine Profile. Speichere das aktuelle Layout mit der Schaltfläche unten.", mute:"Stumm", drag_hint:"Auf ein anderes Gerät ziehen", settings:"Einstellungen", minimize:"In die Taskleiste", autoheal_t:"Auto-Reparatur", autoheal_d:"Stellt dein Layout wieder her, wenn Windows den Ton zurücksetzt", autostart_t:"Autostart", autostart_d:"SoundDeck bei der Anmeldung starten", group_t:"Prozesse gruppieren", group_d:"Mehrprozess-Apps (Spotify, Chrome…) als eine Karte anzeigen", theme_t:"Design", back:"Zurück zum Board", language_t:"Sprache", save_prompt:"Profilname:", profile_default:"Profil" },
  fr: { default_prefix:"par défaut :", no_default:"aucun périphérique par défaut", set_default:"par défaut", mirror:"dupliquer", badge_default:"défaut", badge_mirror:"miroir", drop_here:"glissez une app ici", profiles:"Profils", save_layout:"Enregistrer la disposition", no_profiles:"Aucun profil. Enregistrez la disposition actuelle avec le bouton ci-dessous.", mute:"Couper le son", drag_hint:"Glisser vers un autre périphérique", settings:"Paramètres", minimize:"Réduire dans la barre", autoheal_t:"Auto-réparation", autoheal_d:"Restaure votre disposition si Windows réinitialise l'audio", autostart_t:"Lancer au démarrage", autostart_d:"Démarrer SoundDeck à la connexion", group_t:"Grouper les processus", group_d:"Afficher les apps multi-processus (Spotify, Chrome…) en une carte", theme_t:"Thème", back:"Retour au tableau", language_t:"Langue", save_prompt:"Nom du profil :", profile_default:"Profil" },
  ja: { default_prefix:"既定：", no_default:"既定のデバイスなし", set_default:"既定にする", mirror:"ミラー", badge_default:"既定", badge_mirror:"ミラー", drop_here:"アプリをここにドロップ", profiles:"プロファイル", save_layout:"現在の配置を保存", no_profiles:"プロファイルはまだありません。下のボタンで現在の配置を保存してください。", mute:"ミュート", drag_hint:"別のデバイスへドラッグ", settings:"設定", minimize:"トレイに最小化", autoheal_t:"自動修復", autoheal_d:"Windows が音声をリセットしたら配置を復元します", autostart_t:"ログイン時に起動", autostart_d:"サインイン時に SoundDeck を起動", group_t:"アプリのプロセスをまとめる", group_d:"複数プロセスのアプリ（Spotify、Chrome…）を1枚のカードに表示", theme_t:"テーマ", back:"ボードに戻る", language_t:"言語", save_prompt:"プロファイル名：", profile_default:"プロファイル" },
};
function currentLang() {
  const s = state.settings && state.settings.language;
  if (s && I18N[s]) return s;
  const n = (navigator.language || "en").slice(0, 2).toLowerCase();
  return I18N[n] ? n : "en";
}
function t(k) {
  const l = currentLang();
  return (I18N[l] && I18N[l][k]) || I18N.en[k] || k;
}

function renderSettings() {
  setSwitch(document.getElementById("autohealSwitch"), !!state.settings.autoHeal);
  setSwitch(document.getElementById("autostartSwitch"), !!state.settings.autostart);
  setSwitch(document.getElementById("groupSwitch"), state.settings.groupApps !== false);

  // theme grid
  const theme = state.settings.theme || "neu";
  const tg = document.getElementById("themeGrid");
  tg.innerHTML = "";
  THEMES.forEach(th => {
    const el = document.createElement("button");
    el.className = "theme-tile" + (th.id === theme ? " active" : "");
    el.innerHTML = `<span class="sw" style="background:${th.sw}"></span>` +
      `<span class="tile-name">${th.name}</span>` +
      (th.id === theme ? '<i class="fa-solid fa-check chk"></i>' : "");
    el.onclick = () => send({ cmd: "setTheme", value: th.id });
    tg.appendChild(el);
  });

  // language grid
  const lang = currentLang();
  const lg = document.getElementById("langGrid");
  lg.innerHTML = "";
  LANGS.forEach(x => {
    const el = document.createElement("button");
    el.className = "lang-chip" + (x.id === lang ? " active" : "");
    el.innerHTML = `<span>${x.name}</span>` +
      (x.id === lang ? ' <i class="fa-solid fa-check chk"></i>' : "");
    el.onclick = () => send({ cmd: "setLanguage", value: x.id });
    lg.appendChild(el);
  });
}

function setSwitch(el, on) { el.classList.toggle("on", on); }

// ---- wiring ----
document.getElementById("closeBtn").onclick = () => send({ cmd: "hide" });

const profilesPop = document.getElementById("profilesPop");
const settingsView = document.getElementById("settingsView");
let settingsOpen = false;

function closePops() { if (profilesPop) profilesPop.hidden = true; }

function setSettings(open) {
  settingsOpen = open;
  settingsView.classList.toggle("open", open);
  settingsView.setAttribute("aria-hidden", open ? "false" : "true");
  const btn = document.getElementById("settingsBtn");
  btn.innerHTML = open ? '<i class="fa-solid fa-house"></i>' : '<i class="fa-solid fa-gear"></i>';
  btn.title = open ? t("back") : t("settings");
  const pill = document.getElementById("profilesBtn");
  if (pill) pill.style.visibility = open ? "hidden" : "";
  if (open) closePops();
}

document.getElementById("settingsBtn").onclick = (e) => {
  e.stopPropagation();
  setSettings(!settingsOpen);
};
document.getElementById("profilesBtn").onclick = (e) => {
  e.stopPropagation();
  const show = profilesPop.hidden;
  closePops();
  profilesPop.hidden = !show;
};
document.addEventListener("click", (e) => {
  if (e.target.closest("#profilesBtn, #profilesPop")) return;
  closePops();
});

document.getElementById("autohealSwitch").onclick = () =>
  send({ cmd: "setAutoHeal", value: !state.settings.autoHeal });
document.getElementById("autostartSwitch").onclick = () =>
  send({ cmd: "setAutostart", value: !state.settings.autostart });
document.getElementById("groupSwitch").onclick = () =>
  send({ cmd: "setGroupApps", value: state.settings.groupApps === false });

document.getElementById("saveProfileBtn").onclick = () => {
  const name = window.prompt(t("save_prompt"), t("profile_default"));
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
    settings: { autoHeal: true, autostart: false, theme: "neu", activeProfileId: "p1", groupApps: true, language: "" },
    mirrorActive: false,
  };
  render();
}
