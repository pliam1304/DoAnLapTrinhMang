// Logic điều khiển giao diện Nega Server Dashboard (Chuẩn UI/UX kế thừa từ natri-reborn)

let currentView = 'grid';
let statusFilter = 'ALL';
let searchQuery = '';
let workstationsCache = [];
let zoomedClientId = null;
let frameTick = 0;
let isAuthenticated = false;
let pollTimer = null;

function escapeHtml(str) {
  return String(str ?? '')
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
    .replace(/'/g, '&#39;');
}

function showToast(message) {
  const container = document.getElementById('toast-container');
  const el = document.createElement('div');
  el.className = 'toast';
  el.textContent = message;
  container.appendChild(el);
  setTimeout(() => {
    el.remove();
  }, 3200);
}

function toggleTheme() {
  const html = document.documentElement;
  const isDark = html.classList.toggle('dark');
  localStorage.setItem('nega-theme', isDark ? 'dark' : 'light');
  showToast(isDark ? 'Đã chuyển sang Chế độ Tối (Dark Mode)' : 'Đã chuyển sang Chế độ Sáng (Light Mode)');
}

function initTheme() {
  const saved = localStorage.getItem('nega-theme');
  if (saved === 'light') {
    document.documentElement.classList.remove('dark');
  } else {
    document.documentElement.classList.add('dark');
  }
}

function switchView(viewName) {
  currentView = viewName;
  ['grid', 'table', 'alerts', 'simulator'].forEach((v) => {
    const sec = document.getElementById('view-' + v);
    const nav = document.getElementById('nav-' + v);
    if (sec) sec.style.display = v === viewName ? 'block' : 'none';
    if (nav) nav.classList.toggle('active', v === viewName);
  });
}

function setStatusFilter(filter) {
  statusFilter = filter;
  ['ALL', 'Online', 'Locked', 'Offline'].forEach((f) => {
    const btn = document.getElementById('filter-' + f);
    if (btn) {
      btn.className = f === filter ? 'btn btn-sm btn-primary' : 'btn btn-sm btn-secondary';
    }
  });
  renderWorkstations(workstationsCache, false);
}

function onSearchInput(val) {
  searchQuery = (val || '').trim().toLowerCase();
  renderWorkstations(workstationsCache, false);
}

function matchesFilter(ws) {
  if (statusFilter !== 'ALL' && ws.status !== statusFilter) {
    return false;
  }
  if (searchQuery) {
    const hay = `${ws.hostname} ${ws.ipAddress} ${ws.osDescription} ${ws.clientId}`.toLowerCase();
    if (!hay.includes(searchQuery)) return false;
  }
  return true;
}

async function fetchOverview() {
  if (!isAuthenticated) return;
  try {
    const res = await fetch('/api/overview', { cache: 'no-store' });
    if (res.status === 401) {
      isAuthenticated = false;
      document.getElementById('auth-modal').classList.add('open');
      return;
    }
    if (!res.ok) return;
    const data = await res.json();
    frameTick++;

    document.getElementById('server-host-badge').textContent =
      `${data.serverName} (${data.serverIp}) • UDP :${data.udpPort} • TCP :${data.tcpPort}`;

    document.getElementById('badge-online-count').textContent = data.metrics.onlineCount;
    document.getElementById('badge-total-count').textContent = data.metrics.totalWorkstations;
    document.getElementById('badge-alert-count').textContent = (data.alerts || []).length;
    document.getElementById('badge-sim-count').textContent = data.simulatedCount;

    document.getElementById('metric-bw').textContent = `${data.metrics.totalBandwidthKbps} KB/s`;
    document.getElementById('metric-fps').textContent = `${data.metrics.totalFps} FPS`;
    document.getElementById('metric-udp').textContent = data.metrics.udpPackets;
    document.getElementById('metric-tcp').textContent = data.metrics.tcpFrames;
    document.getElementById('metric-cmd-count').textContent = `${data.metrics.commandsSent} lệnh`;

    workstationsCache = data.workstations || [];
    renderWorkstations(workstationsCache, true);
    renderTable(workstationsCache);
    renderAlertsHistory(data.alerts || []);
    renderActivityLogs(data.logs || []);
    updateZoomModalIfOpen();
  } catch {
    // Ignore transient network poll error
  }
}

function renderWorkstations(list, refreshImages) {
  const container = document.getElementById('workstation-grid-container');
  const filtered = list.filter(matchesFilter);
  const validIds = new Set(filtered.map((w) => w.clientId));

  // Xóa các thẻ không còn trong bộ lọc
  Array.from(container.children).forEach((child) => {
    if (!validIds.has(child.dataset.clientId)) {
      child.remove();
    }
  });

  filtered.forEach((ws) => {
    let card = document.getElementById(`ws-card-${ws.clientId}`);
    if (!card) {
      card = document.createElement('div');
      card.id = `ws-card-${ws.clientId}`;
      card.dataset.clientId = ws.clientId;
      card.className = 'nega-card ws-card';
      card.innerHTML = `
        <div class="ws-card-header">
          <div class="ws-title-group">
            <div class="ws-hostname">
              <span class="ws-name-text"></span>
              <span class="ws-sim-tag" style="font-size:0.65rem;padding:0.1rem 0.4rem;border-radius:999px;background:hsl(var(--secondary));color:hsl(var(--muted-foreground));"></span>
            </div>
            <div class="ws-ip"></div>
          </div>
          <span class="status-pill"></span>
        </div>
        <div class="ws-screen-box" onclick="openZoomModal('${ws.clientId}')" title="Bấm để phóng to màn hình HD (Zoom View)">
          <img class="ws-img" alt="Screen Stream" />
          <div class="ws-overlay-meta"></div>
        </div>
        <div class="ws-card-footer">
          <div style="font-size:0.71rem;color:hsl(var(--muted-foreground));overflow:hidden;text-overflow:ellipsis;white-space:nowrap;" class="ws-ack-text"></div>
          <div style="display:flex;gap:0.35rem;flex-shrink:0;">
            <button class="btn btn-secondary btn-sm ws-lock-btn"></button>
            <button class="btn btn-secondary btn-sm" onclick="openAlertModal('${ws.clientId}')" title="Gửi thông báo khẩn tới máy này">TB</button>
            <button class="btn btn-destructive btn-sm" onclick="openShutdownModal('${ws.clientId}')" title="Tắt máy hoặc hủy tắt máy">Tắt</button>
          </div>
        </div>
      `;
      container.appendChild(card);
    }

    card.classList.toggle('locked', ws.isLocked);
    card.querySelector('.ws-name-text').textContent = ws.hostname;
    card.querySelector('.ws-sim-tag').textContent = ws.isSimulated ? 'Lab Agent' : 'Host thật';
    card.querySelector('.ws-ip').textContent = `${ws.ipAddress} • ${ws.osDescription}`;

    const pill = card.querySelector('.status-pill');
    if (ws.status === 'Locked') {
      pill.className = 'status-pill status-locked';
      pill.textContent = 'ĐÃ KHÓA';
    } else if (ws.status === 'Online') {
      pill.className = 'status-pill status-online';
      pill.textContent = 'ONLINE';
    } else {
      pill.className = 'status-pill status-offline';
      pill.textContent = 'OFFLINE';
    }

    card.querySelector('.ws-overlay-meta').textContent =
      `${ws.streamMode === 'zoom' ? 'HD 960×540' : '480×270'} • ${ws.currentFps} FPS • ${ws.bandwidthKbps} KB/s • ${ws.pingMs}ms`;

    card.querySelector('.ws-ack-text').textContent = ws.lastAckDetail || 'Sẵn sàng';

    const lockBtn = card.querySelector('.ws-lock-btn');
    lockBtn.textContent = ws.isLocked ? 'Mở khóa' : 'Khóa';
    lockBtn.onclick = () => sendLockCommand(ws.clientId, !ws.isLocked);

    if (refreshImages && ws.hasFrame) {
      const img = card.querySelector('.ws-img');
      img.src = `/api/frame/${encodeURIComponent(ws.clientId)}?t=${frameTick}`;
    }
  });
}

function renderTable(list) {
  const tbody = document.getElementById('workstation-table-body');
  if (!tbody) return;
  const filtered = list.filter(matchesFilter);
  tbody.innerHTML = filtered
    .map(
      (ws) => `
    <tr>
      <td><b>${escapeHtml(ws.hostname)}</b><div style="font-size:0.7rem;color:hsl(var(--muted-foreground));">${escapeHtml(ws.clientId)}</div></td>
      <td>${escapeHtml(ws.ipAddress)}</td>
      <td>${escapeHtml(ws.osDescription)}</td>
      <td><span class="status-pill ${ws.status === 'Locked' ? 'status-locked' : ws.status === 'Online' ? 'status-online' : 'status-offline'}">${escapeHtml(ws.status)}</span></td>
      <td>${escapeHtml(ws.streamMode).toUpperCase()}</td>
      <td>${Number(ws.currentFps)} FPS • ${Number(ws.bandwidthKbps)} KB/s</td>
      <td>${Number(ws.pingMs)} ms</td>
      <td>
        <div style="display:flex;gap:0.35rem;">
          <button class="btn btn-secondary btn-sm" onclick="openZoomModal('${escapeHtml(ws.clientId)}')">Zoom HD</button>
          <button class="btn btn-secondary btn-sm" onclick="sendLockCommand('${escapeHtml(ws.clientId)}', ${!ws.isLocked})">${ws.isLocked ? 'Mở khóa' : 'Khóa'}</button>
          <button class="btn btn-secondary btn-sm" onclick="openAlertModal('${escapeHtml(ws.clientId)}')">Thông báo</button>
          <button class="btn btn-destructive btn-sm" onclick="openShutdownModal('${escapeHtml(ws.clientId)}')">Tắt máy</button>
        </div>
      </td>
    </tr>
  `
    )
    .join('');
}

function renderAlertsHistory(alerts) {
  const el = document.getElementById('alerts-history-list');
  if (!el) return;
  if (!alerts.length) {
    el.innerHTML = `<div style="padding:1rem;border-radius:0.75rem;background:hsl(var(--secondary));font-size:0.8rem;color:hsl(var(--muted-foreground));">Chưa có thông báo khẩn nào được phát trong phiên này.</div>`;
    return;
  }

  el.innerHTML = alerts
    .map(
      (a) => `
    <div style="padding:0.9rem 1rem;border-radius:0.85rem;background:hsl(var(--secondary));border:1px solid hsl(var(--border));display:flex;flex-direction:column;gap:0.4rem;">
      <div style="display:flex;justify-content:space-between;align-items:center;">
        <span style="font-weight:700;font-size:0.88rem;">${escapeHtml(a.title)} <span style="font-size:0.72rem;color:hsl(var(--muted-foreground));">(${escapeHtml(a.sentAtText)})</span></span>
        <span class="status-pill ${a.ackCount >= a.targetCount ? 'status-online' : 'status-locked'}">
          Đã xác nhận đọc (ACK): ${Number(a.ackCount)} / ${Number(a.targetCount)} máy
        </span>
      </div>
      <div style="font-size:0.8rem;">${escapeHtml(a.message)}</div>
      <div style="font-size:0.73rem;color:hsl(var(--muted-foreground));">
        Máy đã bấm xác nhận: ${a.ackHostnames && a.ackHostnames.length ? a.ackHostnames.map(escapeHtml).join(', ') : 'Đang chờ máy trạm phản hồi gói 0x07 ACK...'}
      </div>
    </div>
  `
    )
    .join('');
}

function renderActivityLogs(logs) {
  const feed = document.getElementById('activity-log-feed');
  if (!feed) return;
  feed.innerHTML = logs
    .map(
      (l) => `
    <div class="log-item">
      <div style="display:flex;justify-content:space-between;font-weight:700;font-size:0.7rem;color:hsl(var(--muted-foreground));">
        <span>[${escapeHtml(l.category)}]</span>
        <span>${escapeHtml(l.timestampText)}</span>
      </div>
      <div style="margin-top:0.15rem;">${escapeHtml(l.message)}</div>
    </div>
  `
    )
    .join('');
}

async function triggerUdpScan() {
  const res = await fetch('/api/discover', { method: 'POST' });
  const data = await res.json();
  showToast(data.message || 'Đã phát gói UDP Broadcast DISCOVER');
  fetchOverview();
}

async function sendLockCommand(targetClientId, locked) {
  const res = await fetch('/api/control/lock', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ targetClientId, locked })
  });
  const data = await res.json();
  showToast(data.message || 'Đã gửi lệnh điều khiển khóa');
  fetchOverview();
}

// --- ZOOM MODAL ---
async function openZoomModal(clientId) {
  zoomedClientId = clientId;
  document.getElementById('zoom-modal').classList.add('open');
  await fetch('/api/control/stream-mode', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ clientId, mode: 'zoom' })
  });
  updateZoomModalIfOpen();
}

async function closeZoomModal() {
  const prevId = zoomedClientId;
  zoomedClientId = null;
  document.getElementById('zoom-modal').classList.remove('open');
  if (prevId) {
    await fetch('/api/control/stream-mode', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ clientId: prevId, mode: 'grid' })
    });
  }
}

function updateZoomModalIfOpen() {
  if (!zoomedClientId) return;
  const ws = workstationsCache.find((w) => w.clientId === zoomedClientId);
  if (!ws) return;

  document.getElementById('zoom-title').textContent =
    `Giám sát trực tiếp: ${ws.hostname} (${ws.ipAddress})`;
  document.getElementById('zoom-subtitle').textContent =
    `Chế độ ${ws.streamMode.toUpperCase()} • ${ws.currentFps} FPS • ${ws.bandwidthKbps} KB/s • Ping ${ws.pingMs} ms`;
  document.getElementById('zoom-ack-status').textContent = ws.lastAckDetail;

  if (ws.hasFrame) {
    document.getElementById('zoom-stream-img').src =
      `/api/frame/${encodeURIComponent(ws.clientId)}?t=${frameTick}`;
  }
}

function zoomToggleLock(locked) {
  if (zoomedClientId) sendLockCommand(zoomedClientId, locked);
}
function zoomOpenAlert() {
  if (zoomedClientId) openAlertModal(zoomedClientId);
}
function zoomOpenShutdown() {
  if (zoomedClientId) openShutdownModal(zoomedClientId);
}

// --- EMERGENCY ALERT MODAL ---
function openAlertModal(targetClientId) {
  document.getElementById('alert-target').value = targetClientId || 'ALL';
  document.getElementById('alert-modal').classList.add('open');
}
function closeAlertModal() {
  document.getElementById('alert-modal').classList.remove('open');
}
function fillAlertPreset(text) {
  document.getElementById('alert-message').value = text;
}
async function submitAlertCommand() {
  const targetClientId = document.getElementById('alert-target').value;
  const severity = document.getElementById('alert-severity').value;
  const title = document.getElementById('alert-title').value;
  const message = document.getElementById('alert-message').value;

  const res = await fetch('/api/control/alert', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ targetClientId, title, message, severity })
  });
  const data = await res.json();
  closeAlertModal();
  showToast(data.message || 'Đã phát thông báo khẩn cấp');
  fetchOverview();
}

// --- SHUTDOWN MODAL ---
function openShutdownModal(targetClientId) {
  document.getElementById('shutdown-target').value = targetClientId || 'ALL';
  document.getElementById('shutdown-modal').classList.add('open');
}
function closeShutdownModal() {
  document.getElementById('shutdown-modal').classList.remove('open');
}
async function submitShutdownCommand(action) {
  const targetClientId = document.getElementById('shutdown-target').value;
  const delaySeconds = parseInt(document.getElementById('shutdown-delay').value, 10) || 15;
  const reason = document.getElementById('shutdown-reason').value;
  const dryRun = document.getElementById('shutdown-dryrun').checked;

  const res = await fetch('/api/control/shutdown', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ targetClientId, action, delaySeconds, dryRun, reason })
  });
  const data = await res.json();
  closeShutdownModal();
  showToast(data.message || 'Đã gửi lệnh tắt máy');
  fetchOverview();
}

// --- SIMULATOR ---
async function setSimulatedCount(count) {
  const res = await fetch('/api/simulator', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ count })
  });
  const data = await res.json();
  showToast(data.message || `Đã đặt ${count} máy trạm giả lập`);
  fetchOverview();
}

async function bootstrapDashboard() {
  initTheme();
  try {
    const res = await fetch('/api/auth/status', { cache: 'no-store' });
    const status = await res.json();
    if (status.authenticated) {
      isAuthenticated = true;
      startPolling();
    } else {
      document.getElementById('auth-modal').classList.add('open');
      setTimeout(() => document.getElementById('auth-pin-input')?.focus(), 80);
    }
  } catch {
    isAuthenticated = true;
    startPolling();
  }
}

function startPolling() {
  fetchOverview();
  if (!pollTimer) {
    pollTimer = setInterval(fetchOverview, 450);
  }
}

async function submitAdminPin() {
  const input = document.getElementById('auth-pin-input');
  const errEl = document.getElementById('auth-error-msg');
  const pin = (input?.value || '').trim();
  errEl.textContent = '';

  try {
    const res = await fetch('/api/auth/verify', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ pin })
    });
    const data = await res.json();
    if (!res.ok || !data.ok) {
      errEl.textContent = data.message || 'Mã PIN không đúng.';
      return;
    }

    isAuthenticated = true;
    if (input) input.value = '';
    document.getElementById('auth-modal').classList.remove('open');
    showToast('Xác thực quản trị thành công!');
    startPolling();
  } catch {
    errEl.textContent = 'Không thể kết nối tới Server.';
  }
}

bootstrapDashboard();
