const state = {
  requests: [],
  selectedRequestId: null
};

const requestList = document.getElementById('requestList');
const detailEmpty = document.getElementById('detailEmpty');
const detailCard = document.getElementById('detailCard');
const totalCount = document.getElementById('totalCount');
const mostRecentSource = document.getElementById('mostRecentSource');
const latestCaptureTime = document.getElementById('latestCaptureTime');

const detailSource = document.getElementById('detailSource');
const detailId = document.getElementById('detailId');
const detailReceivedAt = document.getElementById('detailReceivedAt');
const detailHeaders = document.getElementById('detailHeaders');
const detailBody = document.getElementById('detailBody');

function formatTimestamp(value) {
  if (!value) {
    return '—';
  }

  try {
    return new Date(value).toLocaleString(undefined, {
      dateStyle: 'medium',
      timeStyle: 'medium'
    });
  } catch {
    return value;
  }
}

function escapeHtml(value) {
  return String(value)
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
    .replace(/'/g, '&#039;');
}

function renderSummary() {
  totalCount.textContent = String(state.requests.length);
  const latest = state.requests[0];
  mostRecentSource.textContent = latest ? latest.source : '—';
  latestCaptureTime.textContent = latest ? formatTimestamp(latest.receivedAtUtc) : '—';
}

function renderList() {
  if (state.requests.length === 0) {
    requestList.innerHTML = '<div class="empty-state">No captured requests yet. Send one to <code>/capture/{source}</code> to begin inspecting webhook traffic.</div>';
    return;
  }

  requestList.innerHTML = state.requests
    .map((request) => {
      const selectedClass = request.id === state.selectedRequestId ? 'is-selected' : '';
      const preview = request.body ? String(request.body).slice(0, 120).replace(/\s+/g, ' ') : '';

      return `
        <button class="request-item ${selectedClass}" type="button" data-request-id="${escapeHtml(request.id)}">
          <div class="request-item-header">
            <span class="request-source">${escapeHtml(request.source)}</span>
            <span class="request-time">${escapeHtml(formatTimestamp(request.receivedAtUtc))}</span>
          </div>
          <div class="request-item-body">
            <span class="request-id">${escapeHtml(request.id)}</span>
          </div>
          <div class="request-preview">${escapeHtml(preview || '(empty body)')}</div>
        </button>
      `;
    })
    .join('');

  requestList.querySelectorAll('.request-item').forEach((button) => {
    button.addEventListener('click', () => {
      const requestId = button.dataset.requestId;
      selectRequest(requestId);
    });
  });
}

function renderHeaders(headers) {
  const entries = Object.entries(headers ?? {});

  if (!entries.length) {
    detailHeaders.innerHTML = '<div class="empty-state">No headers captured.</div>';
    return;
  }

  detailHeaders.innerHTML = entries
    .map(([name, values]) => {
      const valueText = Array.isArray(values) ? values.join(', ') : String(values ?? '');
      return `
        <div class="header-row">
          <div class="header-name">${escapeHtml(name)}</div>
          <div class="header-value">${escapeHtml(valueText)}</div>
        </div>
      `;
    })
    .join('');
}

function prettyPrintBody(rawBody) {
  if (!rawBody) {
    return '';
  }

  try {
    const parsed = JSON.parse(rawBody);
    return JSON.stringify(parsed, null, 2);
  } catch {
    return rawBody;
  }
}

function selectRequest(requestId) {
  const request = state.requests.find((item) => item.id === requestId);
  if (!request) {
    return;
  }

  state.selectedRequestId = requestId;

  detailEmpty.classList.add('hidden');
  detailCard.classList.remove('hidden');
  detailSource.textContent = request.source;
  detailId.textContent = request.id;
  detailReceivedAt.textContent = formatTimestamp(request.receivedAtUtc);
  renderHeaders(request.headers);

  const prettyBody = prettyPrintBody(request.body);
  detailBody.textContent = prettyBody || '(empty body)';
  detailBody.dataset.rawBody = request.body ?? '';

  renderList();
}

async function loadRequests() {
  try {
    const response = await fetch('/requests');
    if (!response.ok) {
      throw new Error(`Failed to load requests: ${response.status}`);
    }

    const requests = await response.json();
    state.requests = Array.isArray(requests) ? requests : [];

    if (state.selectedRequestId && !state.requests.some((request) => request.id === state.selectedRequestId)) {
      state.selectedRequestId = state.requests[0]?.id ?? null;
    }

    if (!state.selectedRequestId && state.requests.length > 0) {
      state.selectedRequestId = state.requests[0].id;
    }

    renderSummary();
    renderList();

    if (state.selectedRequestId) {
      selectRequest(state.selectedRequestId);
    } else {
      detailEmpty.classList.remove('hidden');
      detailCard.classList.add('hidden');
    }
  } catch (error) {
    requestList.innerHTML = `<div class="empty-state">Unable to load captured requests. ${escapeHtml(String(error.message))}</div>`;
  }
}

async function copyTextToClipboard(value) {
  try {
    await navigator.clipboard.writeText(value);
  } catch {
    // fall back silently when clipboard is unavailable
  }
}

document.querySelectorAll('.copy-button').forEach((button) => {
  button.addEventListener('click', () => {
    const target = button.dataset.copyTarget;
    const value = target === 'request-id' ? detailId.textContent : detailBody.dataset.rawBody ?? detailBody.textContent;
    copyTextToClipboard(value);
  });
});

loadRequests();
