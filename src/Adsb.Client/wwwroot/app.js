const aircraft = new Map();
const activity = [];
const earthRadiusMeters = 6371008.8;
const metersPerNauticalMile = 1852;
let clientConfig = {
  defaultServerUrl: "http://127.0.0.1:5087",
  receiver: {
    label: "Receiver",
    latitude: null,
    longitude: null,
    rangeNauticalMiles: 150
  }
};
let connection = null;
let statusTimer = null;
let renderQueued = false;
let watchlistHits = 0;

const elements = {
  serverUrl: document.querySelector("#serverUrl"),
  connectButton: document.querySelector("#connectButton"),
  disconnectButton: document.querySelector("#disconnectButton"),
  filterInput: document.querySelector("#filterInput"),
  connectionDot: document.querySelector("#connectionDot"),
  connectionState: document.querySelector("#connectionState"),
  aircraftCount: document.querySelector("#aircraftCount"),
  framesDecoded: document.querySelector("#framesDecoded"),
  eventsPublished: document.querySelector("#eventsPublished"),
  deviceStatus: document.querySelector("#deviceStatus"),
  lastEvent: document.querySelector("#lastEvent"),
  radarStatus: document.querySelector("#radarStatus"),
  positionCount: document.querySelector("#positionCount"),
  watchlistCount: document.querySelector("#watchlistCount"),
  tableStatus: document.querySelector("#tableStatus"),
  aircraftBody: document.querySelector("#aircraftBody"),
  activityList: document.querySelector("#activityList"),
  plotCanvas: document.querySelector("#plotCanvas")
};

elements.connectButton.addEventListener("click", () => connect().catch(showConnectionError));
elements.disconnectButton.addEventListener("click", disconnect);
elements.filterInput.addEventListener("input", requestRender);
window.addEventListener("resize", requestRender);
window.addEventListener("orientationchange", () => setTimeout(requestRender, 250));
window.visualViewport?.addEventListener("resize", requestRender);

initialize();

async function initialize() {
  await loadClientConfig();

  const savedServer = localStorage.getItem("adsb.serverUrl");
  const query = new URLSearchParams(location.search);
  const queryServer = query.get("server");
  elements.serverUrl.value = queryServer || savedServer || clientConfig.defaultServerUrl || elements.serverUrl.value;

  updateRadarStatus();
  requestRender();

  connect().catch(() => {
    setConnectionState("Disconnected", "idle");
  });
}

async function loadClientConfig() {
  try {
    const response = await fetch("/api/config", { cache: "no-store" });
    if (!response.ok) {
      throw new Error(`Config request failed: ${response.status}`);
    }

    const config = await response.json();
    clientConfig = {
      ...clientConfig,
      ...config,
      receiver: {
        ...clientConfig.receiver,
        ...(config.receiver || {})
      }
    };
  } catch (error) {
    addActivity("Config", error.message || "Unable to load client config");
  }
}

async function connect() {
  if (connection) {
    await disconnect();
  }

  const baseUrl = normalizeBaseUrl(elements.serverUrl.value);
  localStorage.setItem("adsb.serverUrl", baseUrl);
  setConnectionState("Connecting", "warn");

  connection = new signalR.HubConnectionBuilder()
    .withUrl(`${baseUrl}/hubs/adsb`, { withCredentials: false })
    .withAutomaticReconnect([0, 2000, 5000, 10000])
    .build();

  connection.on("aircraftUpdated", upsertAircraft);
  connection.on("watchlistTelemetry", onWatchlistTelemetry);
  connection.onreconnecting(() => setConnectionState("Reconnecting", "warn"));
  connection.onreconnected(() => {
    setConnectionState("Connected", "live");
    refreshSnapshot();
    refreshStatus();
  });
  connection.onclose(error => {
    stopStatusTimer();
    setConnectionState(error ? "Disconnected with error" : "Disconnected", error ? "error" : "idle");
    setControls(false);
  });

  await connection.start();
  setControls(true);
  setConnectionState("Connected", "live");
  await refreshSnapshot();
  await refreshStatus();
  startStatusTimer();
}

async function disconnect() {
  stopStatusTimer();
  const current = connection;
  connection = null;
  if (current) {
    await current.stop();
  }

  setControls(false);
  setConnectionState("Disconnected", "idle");
}

async function refreshSnapshot() {
  if (!connection) {
    return;
  }

  const snapshot = await connection.invoke("GetAircraftSnapshot");
  aircraft.clear();
  for (const item of snapshot) {
    upsertAircraft(item, false);
  }
  requestRender();
}

async function refreshStatus() {
  if (!connection) {
    return;
  }

  const status = await connection.invoke("GetStatus");
  elements.framesDecoded.textContent = formatNumber(status.framesDecoded);
  elements.eventsPublished.textContent = formatNumber(status.eventsPublished);
  elements.deviceStatus.textContent = status.deviceConnected
    ? "Connected"
    : status.captureEnabled
      ? "Offline"
      : "Capture Off";
  elements.lastEvent.textContent = status.lastEventAtUtc ? formatTime(status.lastEventAtUtc) : "None";

  if (status.lastError) {
    addActivity("Status", status.lastError);
  }
}

function upsertAircraft(update, shouldLog = true) {
  const key = getAircraftKey(update);
  if (!key) {
    return;
  }

  aircraft.set(key, { ...update, _updatedAt: Date.now() });
  if (shouldLog) {
    addActivity(update.icao || update.tailNumber || update.flightNumber || "Aircraft", update.description || "Updated");
  }
  requestRender();
}

function onWatchlistTelemetry(event) {
  watchlistHits += 1;
  elements.watchlistCount.textContent = `${watchlistHits} watchlist hits`;
  if (event.telemetry) {
    upsertAircraft(event.telemetry, false);
  }

  const label = event.watchlistLabel || event.identifierValue || "Watchlist";
  addActivity(label, `Matched ${event.identifierType}`);
}

function requestRender() {
  if (renderQueued) {
    return;
  }

  renderQueued = true;
  requestAnimationFrame(() => {
    renderQueued = false;
    render();
  });
}

function render() {
  const rows = getFilteredAircraft();
  elements.aircraftCount.textContent = formatNumber(rows.length);
  elements.tableStatus.textContent = rows.length === 0 ? "Waiting for data" : `${rows.length} aircraft shown`;
  renderTable(rows);
  renderActivity();
  renderPlot(rows);
}

function renderTable(rows) {
  elements.aircraftBody.replaceChildren();

  if (rows.length === 0) {
    const row = document.createElement("tr");
    row.className = "empty-row";
    const cell = document.createElement("td");
    cell.colSpan = 11;
    cell.textContent = connection ? "No matching aircraft yet" : "Connect to start receiving aircraft";
    row.appendChild(cell);
    elements.aircraftBody.appendChild(row);
    return;
  }

  for (const item of rows) {
    const row = document.createElement("tr");
    row.append(
      cell(formatTime(item.receivedAtUtc), freshnessClass(item.receivedAtUtc)),
      cell(item.icao),
      cell(item.tailNumber),
      cell(item.flightNumber),
      cell(item.callsign),
      cell(formatFeet(item.altitudeFeet), "numeric"),
      cell(formatKnots(item.groundSpeedKnots || item.airspeedKnots), "numeric"),
      cell(formatDegrees(item.trackDegrees || item.headingDegrees), "numeric"),
      cell(formatPosition(item.latitude, item.longitude)),
      cell(formatSignal(item.signalDb), "numeric"),
      cell(item.description || `DF ${item.downlinkFormat}`)
    );
    elements.aircraftBody.appendChild(row);
  }
}

function renderActivity() {
  elements.activityList.replaceChildren();
  for (const item of activity.slice(0, 24)) {
    const row = document.createElement("li");
    const time = document.createElement("time");
    time.textContent = formatTime(item.at);
    const text = document.createElement("span");
    text.textContent = `${item.title}: ${item.detail}`;
    row.append(time, text);
    elements.activityList.appendChild(row);
  }
}

function renderPlot(rows) {
  const canvas = elements.plotCanvas;
  const rect = canvas.getBoundingClientRect();
  const scale = window.devicePixelRatio || 1;
  canvas.width = Math.max(1, Math.floor(rect.width * scale));
  canvas.height = Math.max(1, Math.floor(rect.height * scale));

  const ctx = canvas.getContext("2d");
  ctx.setTransform(scale, 0, 0, scale, 0, 0);
  ctx.clearRect(0, 0, rect.width, rect.height);
  const receiver = getReceiverConfig();
  drawRadarBackground(ctx, rect.width, rect.height, receiver);

  const positioned = rows.filter(item => Number.isFinite(item.latitude) && Number.isFinite(item.longitude));
  if (!receiver.configured) {
    elements.positionCount.textContent = `${positioned.length} positioned`;
    drawCenteredText(ctx, rect.width, rect.height, "Configure receiver latitude and longitude");
    return;
  }

  if (positioned.length === 0) {
    elements.positionCount.textContent = "0 positioned";
    drawCenteredText(ctx, rect.width, rect.height, "No positions yet");
    return;
  }

  const centerX = rect.width / 2;
  const centerY = rect.height / 2;
  const radius = getRadarRadius(rect.width, rect.height);
  let inRange = 0;

  for (const item of positioned) {
    const polar = getBearingAndDistance(receiver.latitude, receiver.longitude, item.latitude, item.longitude);
    if (polar.distanceNm > receiver.rangeNauticalMiles) {
      continue;
    }

    inRange += 1;
    const bearingRad = toRadians(polar.bearingDegrees);
    const distanceRatio = polar.distanceNm / receiver.rangeNauticalMiles;
    const x = centerX + Math.sin(bearingRad) * distanceRatio * radius;
    const y = centerY - Math.cos(bearingRad) * distanceRatio * radius;
    drawAircraftPoint(ctx, x, y, item, polar);
  }

  elements.positionCount.textContent = `${inRange} in range / ${positioned.length} positioned`;
  if (inRange === 0) {
    drawCenteredText(ctx, rect.width, rect.height, "No positioned aircraft within radar range");
  }
}

function drawRadarBackground(ctx, width, height, receiver) {
  const centerX = width / 2;
  const centerY = height / 2;
  const radius = getRadarRadius(width, height);

  ctx.fillStyle = "#0d0f11";
  ctx.fillRect(0, 0, width, height);

  ctx.strokeStyle = "rgba(78, 160, 255, 0.15)";
  ctx.lineWidth = 1;
  for (let bearing = 0; bearing < 360; bearing += 30) {
    const bearingRad = toRadians(bearing);
    ctx.beginPath();
    ctx.moveTo(centerX, centerY);
    ctx.lineTo(centerX + Math.sin(bearingRad) * radius, centerY - Math.cos(bearingRad) * radius);
    ctx.stroke();
  }

  ctx.strokeStyle = "rgba(78, 160, 255, 0.18)";
  ctx.lineWidth = 1;
  for (const ratio of [0.25, 0.5, 0.75, 1]) {
    ctx.beginPath();
    ctx.arc(centerX, centerY, radius * ratio, 0, Math.PI * 2);
    ctx.stroke();
  }

  ctx.strokeStyle = "rgba(243, 245, 247, 0.32)";
  ctx.beginPath();
  ctx.moveTo(centerX, centerY - radius);
  ctx.lineTo(centerX, centerY + radius);
  ctx.moveTo(centerX - radius, centerY);
  ctx.lineTo(centerX + radius, centerY);
  ctx.stroke();

  ctx.fillStyle = "#a7b0bb";
  ctx.font = "12px system-ui, sans-serif";
  ctx.textAlign = "center";
  ctx.fillText("N", centerX, centerY - radius - 10);
  ctx.fillText("S", centerX, centerY + radius + 18);
  ctx.fillText("E", centerX + radius + 12, centerY + 4);
  ctx.fillText("W", centerX - radius - 12, centerY + 4);

  if (receiver.configured) {
    for (const ratio of [0.25, 0.5, 0.75, 1]) {
      const rangeLabel = `${Math.round(receiver.rangeNauticalMiles * ratio)} nm`;
      ctx.fillText(rangeLabel, centerX + 6, centerY - (radius * ratio) + 14);
    }
  }

  ctx.fillStyle = "#4ea0ff";
  ctx.strokeStyle = "rgba(255, 255, 255, 0.75)";
  ctx.lineWidth = 1;
  ctx.beginPath();
  ctx.arc(centerX, centerY, 6, 0, Math.PI * 2);
  ctx.fill();
  ctx.stroke();

  ctx.fillStyle = "#f3f5f7";
  ctx.textAlign = "left";
  ctx.fillText(receiver.label || "Receiver", centerX + 10, centerY - 10);
}

function drawAircraftPoint(ctx, x, y, item, polar) {
  const age = Date.now() - new Date(item.receivedAtUtc).getTime();
  ctx.fillStyle = age < 15000 ? "#24c487" : "#f3b548";
  ctx.strokeStyle = "rgba(255, 255, 255, 0.7)";
  ctx.lineWidth = 1;
  ctx.beginPath();
  ctx.arc(x, y, 5, 0, Math.PI * 2);
  ctx.fill();
  ctx.stroke();

  const label = item.flightNumber || item.tailNumber || item.icao || "";
  if (label) {
    ctx.fillStyle = "#f3f5f7";
    ctx.font = "12px system-ui, sans-serif";
    ctx.fillText(label, x + 9, y - 8);
  }

  if (polar) {
    ctx.fillStyle = "#a7b0bb";
    ctx.font = "11px system-ui, sans-serif";
    ctx.fillText(`${Math.round(polar.distanceNm)} nm ${Math.round(polar.bearingDegrees)}°`, x + 9, y + 8);
  }
}

function drawCenteredText(ctx, width, height, text) {
  ctx.fillStyle = "#a7b0bb";
  ctx.font = "14px system-ui, sans-serif";
  ctx.textAlign = "center";
  ctx.fillText(text, width / 2, height / 2);
  ctx.textAlign = "left";
}

function getFilteredAircraft() {
  const filter = elements.filterInput.value.trim().toUpperCase();
  return [...aircraft.values()]
    .filter(item => !filter || [
      item.icao,
      item.tailNumber,
      item.flightNumber,
      item.callsign,
      item.description
    ].some(value => (value || "").toUpperCase().includes(filter)))
    .sort((a, b) => new Date(b.receivedAtUtc) - new Date(a.receivedAtUtc));
}

function addActivity(title, detail) {
  activity.unshift({ at: new Date().toISOString(), title, detail });
  if (activity.length > 80) {
    activity.pop();
  }
  requestRender();
}

function setConnectionState(text, tone) {
  elements.connectionState.textContent = text;
  elements.connectionDot.className = `dot dot-${tone}`;
}

function setControls(connected) {
  elements.connectButton.disabled = connected;
  elements.disconnectButton.disabled = !connected;
  elements.serverUrl.disabled = connected;
}

function startStatusTimer() {
  stopStatusTimer();
  statusTimer = setInterval(() => refreshStatus().catch(showConnectionError), 2000);
}

function stopStatusTimer() {
  if (statusTimer) {
    clearInterval(statusTimer);
    statusTimer = null;
  }
}

function showConnectionError(error) {
  addActivity("Connection", error?.message || "Unable to connect");
  setConnectionState("Connection Error", "error");
  setControls(false);
}

function getAircraftKey(item) {
  return item.icao || item.tailNumber || item.flightNumber || item.callsign || item.rawHex;
}

function getReceiverConfig() {
  const receiver = clientConfig.receiver || {};
  const latitude = Number(receiver.latitude);
  const longitude = Number(receiver.longitude);
  const rangeNauticalMiles = Math.max(1, Number(receiver.rangeNauticalMiles) || 150);
  return {
    label: receiver.label || "Receiver",
    latitude,
    longitude,
    rangeNauticalMiles,
    configured: Number.isFinite(latitude) && Number.isFinite(longitude)
  };
}

function updateRadarStatus() {
  const receiver = getReceiverConfig();
  elements.radarStatus.textContent = receiver.configured
    ? `${receiver.label}: ${receiver.latitude.toFixed(4)}, ${receiver.longitude.toFixed(4)} • ${Math.round(receiver.rangeNauticalMiles)} nm`
    : "Receiver not configured";
}

function getRadarRadius(width, height) {
  return Math.max(1, Math.min(width, height) * 0.44);
}

function getBearingAndDistance(lat1, lon1, lat2, lon2) {
  const startLat = toRadians(lat1);
  const endLat = toRadians(lat2);
  const deltaLat = toRadians(lat2 - lat1);
  const deltaLon = toRadians(lon2 - lon1);

  const a = Math.sin(deltaLat / 2) ** 2 +
    Math.cos(startLat) * Math.cos(endLat) * Math.sin(deltaLon / 2) ** 2;
  const c = 2 * Math.atan2(Math.sqrt(a), Math.sqrt(1 - a));
  const distanceNm = (earthRadiusMeters * c) / metersPerNauticalMile;

  const y = Math.sin(deltaLon) * Math.cos(endLat);
  const x = Math.cos(startLat) * Math.sin(endLat) -
    Math.sin(startLat) * Math.cos(endLat) * Math.cos(deltaLon);
  const bearingDegrees = normalizeDegrees(toDegrees(Math.atan2(y, x)));

  return { bearingDegrees, distanceNm };
}

function toRadians(degrees) {
  return degrees * Math.PI / 180;
}

function toDegrees(radians) {
  return radians * 180 / Math.PI;
}

function normalizeDegrees(degrees) {
  const normalized = degrees % 360;
  return normalized < 0 ? normalized + 360 : normalized;
}

function normalizeBaseUrl(value) {
  return value.trim().replace(/\/+$/, "");
}

function cell(value, className) {
  const td = document.createElement("td");
  if (className) {
    td.className = className;
  }
  td.textContent = value ?? "";
  return td;
}

function freshnessClass(value) {
  const age = Date.now() - new Date(value).getTime();
  return age < 15000 ? "fresh" : "stale";
}

function formatNumber(value) {
  return Number(value || 0).toLocaleString();
}

function formatTime(value) {
  if (!value) {
    return "";
  }
  return new Intl.DateTimeFormat(undefined, {
    hour: "2-digit",
    minute: "2-digit",
    second: "2-digit"
  }).format(new Date(value));
}

function formatFeet(value) {
  return Number.isFinite(value) ? `${formatNumber(value)} ft` : "";
}

function formatKnots(value) {
  return Number.isFinite(value) ? `${formatNumber(value)} kt` : "";
}

function formatDegrees(value) {
  return Number.isFinite(value) ? `${Math.round(value)}°` : "";
}

function formatPosition(lat, lon) {
  return Number.isFinite(lat) && Number.isFinite(lon) ? `${lat.toFixed(5)}, ${lon.toFixed(5)}` : "";
}

function formatSignal(value) {
  return Number.isFinite(value) ? `${value.toFixed(1)} dB` : "";
}
