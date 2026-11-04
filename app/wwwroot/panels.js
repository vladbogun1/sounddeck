"use strict";

// Settings panels added on top of the basic toggles: device visibility and updates.

// ---- device visibility ----
// The default device can't be hidden: apps that follow "default" would vanish with it.
function isHidden(dev) {
  return !dev.isDefault && (state.settings.hiddenDeviceIds || []).includes(dev.id);
}
function visibleDevices() { return state.devices.filter(d => !isHidden(d)); }

function renderDevicesSettings() {
  const box = document.getElementById("devicesList");
  box.innerHTML = "";
  state.devices.forEach(d => {
    const row = document.createElement("div");
    row.className = "set-row dev-row" + (isHidden(d) ? " is-hidden" : "");
    row.innerHTML = `
      <span class="dev-ico">${deviceIcon(d.bus)}</span>
      <div class="dev-meta">
        <div class="set-title">${escapeHtml(d.name)}</div>
        ${d.isDefault ? `<div class="set-desc">${t("always_shown")}</div>` : ""}
      </div>
      <button class="switch" role="switch" ${d.isDefault ? "disabled" : ""}><span></span></button>`;
    const sw = row.querySelector(".switch");
    setSwitch(sw, !isHidden(d));
    sw.setAttribute("aria-checked", String(!isHidden(d)));
    if (!d.isDefault) sw.onclick = () => send({ cmd: "setDeviceHidden", deviceId: d.id, value: !isHidden(d) });
    box.appendChild(row);
  });
}

// ---- updates ----
function renderUpdates() {
  const u = state.update || {};
  setSwitch(document.getElementById("autoupdateSwitch"), state.settings.autoUpdate !== false);
  setText("t_updates", t("updates_t"));
  setText("t_autoupdate", t("autoupdate_t"));
  setText("d_autoupdate", t("autoupdate_d"));
  setText("updVersion", `${t("version")} ${u.currentVersion || "—"}`);

  const v = u.latestVersion || "";
  const status = {
    Checking: t("checking"),
    UpToDate: t("up_to_date"),
    Available: t("update_avail", { v }),
    Downloading: t("downloading", { p: u.progress || 0 }),
    Ready: t("update_ready", { v }),
    Error: t("update_err"),
  }[u.status] || "";
  setText("updStatus", status);
  const err = document.getElementById("updError");
  err.textContent = u.status === "Error" ? (u.error || "") : (u.status === "Available" && !u.canInstall ? t("cant_install") : "");
  err.hidden = !err.textContent;

  const bar = document.getElementById("updProgress");
  bar.hidden = u.status !== "Downloading";
  bar.firstElementChild.style.width = (u.progress || 0) + "%";

  const actions = document.getElementById("updActions");
  actions.innerHTML = "";
  const button = (label, icon, onClick, primary) => {
    const b = document.createElement("button");
    b.className = "ghost" + (primary ? " accent" : "");
    b.innerHTML = `<i class="fa-solid ${icon}"></i> ${label}`;
    b.onclick = onClick;
    actions.appendChild(b);
    return b;
  };
  const canInstall = !!u.canInstall;
  if (u.status === "Available" && canInstall) button(t("install"), "fa-download", () => send({ cmd: "installUpdate" }), true);
  if (u.status === "Ready") button(t("restart_install"), "fa-rotate", () => send({ cmd: "installUpdate" }), true);
  if (u.status !== "Downloading" && u.status !== "Ready") {
    const b = button(t("check_now"), "fa-arrows-rotate", () => send({ cmd: "checkUpdate" }));
    b.disabled = u.status === "Checking";
  }
  if (u.releaseUrl && (u.status === "Error" || (u.status === "Available" && !canInstall)))
    button(t("open_release"), "fa-arrow-up-right-from-square", () => send({ cmd: "openUrl", url: u.releaseUrl }));

  // Top-bar pill: visible whenever there is something to install.
  const pill = document.getElementById("updateBtn");
  const show = (u.status === "Available" && canInstall) || u.status === "Downloading" || u.status === "Ready";
  pill.hidden = !show;
  if (show) {
    pill.title = u.status === "Ready" ? t("restart_install") : status;
    pill.innerHTML = u.status === "Downloading"
      ? `<i class="fa-solid fa-circle-notch fa-spin"></i> ${u.progress || 0}%`
      : `<i class="fa-solid fa-circle-arrow-up"></i> ${escapeHtml(v)}`;
    pill.onclick = () => { if (u.status !== "Downloading") send({ cmd: "installUpdate" }); };
  }
}

document.getElementById("autoupdateSwitch").onclick = () =>
  send({ cmd: "setAutoUpdate", value: state.settings.autoUpdate === false });
