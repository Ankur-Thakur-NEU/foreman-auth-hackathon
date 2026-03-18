const config = window.TokenForemanConfig;
const keys = { token: config.accessTokenKey, user: config.userProfileKey };
const el = {};
const state = { auth0: null, deferredInstall: null, photos: [], stream: null };

// Production-aware: avoid logging sensitive data and show appropriate error messages.
const isProduction = () => /^(localhost|127\.0\.0\.1|\[::1\])$/i.test(window.location.hostname) === false;

function $(id) {
  const node = document.getElementById(id);
  if (!node) throw new Error(`Missing required element: ${id}`);
  el[id] = node;
  return node;
}

function h(value) {
  return String(value)
    .replaceAll('&', '&amp;')
    .replaceAll('<', '&lt;')
    .replaceAll('>', '&gt;')
    .replaceAll('"', '&quot;')
    .replaceAll("'", '&#39;');
}

function storedUser() {
  try { return JSON.parse(localStorage.getItem(keys.user) || 'null'); } catch { return null; }
}

function storedToken() {
  return localStorage.getItem(keys.token);
}

function saveSession(token, user) {
  localStorage.setItem(keys.token, token);
  if (user) localStorage.setItem(keys.user, JSON.stringify(user));
}

function clearSession() {
  localStorage.removeItem(keys.token);
  localStorage.removeItem(keys.user);
}

function setStatus(text) {
  el['last-updated'].textContent = text;
}

function renderAuth() {
  const user = storedUser();
  const badge = el['auth-badge'];
  if (user) {
    badge.className = 'badge badge-live';
    badge.innerHTML = `<span class="status-dot"></span><span>${h(user.name || user.email || 'Authenticated')}</span>`;
    el['auth-note'].textContent = `Signed in as ${user.name || user.email || 'field operator'}.`;
    el['login-button'].classList.add('hidden');
    el['logout-button'].classList.remove('hidden');
  } else {
    badge.className = 'badge badge-offline';
    badge.innerHTML = '<span class="status-dot"></span><span>Signed out</span>';
    el['auth-note'].textContent = 'Sign in with Auth0 Universal Login to enable protected actions.';
    el['login-button'].classList.remove('hidden');
    el['logout-button'].classList.add('hidden');
  }
}

function renderIdle() {
  el['response-panel'].innerHTML = `
    <div class="rounded-3xl border border-dashed border-slate-600/70 bg-slate-950/50 p-8 text-sm text-slate-400">
      Send a field command and this panel will stream the action summary, calendar link, Slack timestamp, and Procore ID in real time.
    </div>`;
}

function setPanelMessage(tone, title, message) {
  const palette = {
    info: 'border-slate-700/80 bg-slate-950/50 text-slate-200',
    success: 'border-emerald-500/20 bg-emerald-950/40 text-emerald-100',
    warning: 'border-amber-500/20 bg-amber-950/40 text-amber-100',
    danger: 'border-red-500/20 bg-red-950/40 text-red-100'
  };

  el['response-panel'].className = `rounded-3xl border p-5 ${palette[tone] || palette.info}`;
  el['response-panel'].innerHTML = `<p class="text-sm font-semibold uppercase tracking-[0.18em]">${h(title)}</p><p class="mt-2 text-sm leading-6">${h(message)}</p>`;
}

function renderError(message, challenge = '') {
  el['response-panel'].innerHTML = `
    <div class="rounded-3xl border border-red-500/30 bg-red-950/40 p-5 text-red-100">
      <p class="text-sm font-semibold uppercase tracking-[0.18em] text-red-300">Request blocked</p>
      <p class="mt-2 text-base">${h(message)}</p>
      ${challenge ? `<p class="mt-2 text-xs text-slate-400">${h(challenge)}</p>` : ''}
    </div>`;
}

function chip(label, value) {
  return value ? `<div class="rounded-2xl border border-slate-700/80 bg-slate-950/50 px-4 py-3"><p class="text-[0.65rem] font-semibold uppercase tracking-[0.2em] text-slate-400">${h(label)}</p><p class="mt-1 text-sm font-semibold text-slate-100">${h(value)}</p></div>` : '';
}

function pretty(value) {
  if (!value) return '';
  try { return JSON.stringify(typeof value === 'string' ? JSON.parse(value) : value, null, 2); } catch { return String(value); }
}

function renderResult(resp) {
  const actions = Array.isArray(resp?.actionsTaken) ? resp.actionsTaken : [];
  const hasIds = Boolean(resp?.calendarLink || resp?.slackTs || resp?.procoreId);
  const body = actions.length
    ? actions.map(a => `
      <article class="timeline-item rounded-2xl border border-slate-700/80 bg-slate-950/50 p-4 fade-in">
        <div class="flex items-start justify-between gap-3">
          <div>
            <p class="text-sm font-semibold text-amber-300">${h(a.toolName)}</p>
            <p class="mt-1 text-base font-medium text-slate-100">${h(a.intent)}</p>
          </div>
          <span class="rounded-full border border-slate-700 bg-slate-900 px-2.5 py-1 text-[0.7rem] font-bold uppercase tracking-[0.2em] text-slate-300">Action</span>
        </div>
        <pre class="mt-3 overflow-x-auto rounded-2xl border border-slate-700/70 bg-slate-950/70 p-3 text-xs leading-6 text-slate-200">${h(pretty(a.output))}</pre>
      </article>`).join('')
    : '<div class="rounded-3xl border border-slate-700/80 bg-slate-950/50 p-5 text-sm text-slate-400">No automated actions were required for this request.</div>';

  el['response-panel'].innerHTML = `
    <div class="space-y-4">
      <div class="rounded-3xl border border-slate-700/80 bg-slate-950/50 p-5 fade-in">
        <div class="flex items-start justify-between gap-4">
          <div>
            <p class="text-[0.65rem] font-semibold uppercase tracking-[0.24em] text-amber-300">Execution summary</p>
            <h3 class="mt-2 text-lg font-semibold text-slate-100">${h(resp?.userQuery || '')}</h3>
            <p class="mt-1 text-sm text-slate-400">User subject: ${h(resp?.userSub || '')}</p>
          </div>
          <div class="rounded-2xl border border-emerald-500/20 bg-emerald-500/10 px-3 py-2 text-right">
            <p class="text-[0.65rem] font-semibold uppercase tracking-[0.2em] text-emerald-300">Actions taken</p>
            <p class="mt-1 text-xl font-bold text-emerald-200">${actions.length}</p>
          </div>
        </div>
        <div class="mt-4 grid gap-3 sm:grid-cols-3">
          ${chip('Calendar link', resp?.calendarLink)}
          ${chip('Slack ts', resp?.slackTs)}
          ${chip('Procore ID', resp?.procoreId)}
        </div>
        <div class="mt-4 rounded-2xl border border-slate-700/80 bg-slate-950/60 p-4">
          <div class="grid gap-2 md:grid-cols-2 xl:grid-cols-3">
            ${chip('Actions returned', String(actions.length))}
            ${chip('Calendar event', resp?.calendarLink ? 'Created or updated' : null)}
            ${chip('Slack message', resp?.slackTs ? 'Posted' : null)}
          </div>
          ${hasIds ? '' : '<p class="mt-3 text-sm text-slate-400">No downstream IDs were returned for this command.</p>'}
        </div>
      </div>
      <div class="space-y-3">${body}</div>
    </div>`;
}

function renderPhotos() {
  const gallery = el['photo-gallery'];
  gallery.innerHTML = state.photos.length
    ? state.photos.map(photo => `
      <article class="photo-card overflow-hidden rounded-2xl">
        <img class="photo-thumb" src="${photo.src}" alt="${h(photo.label)}" />
        <div class="space-y-1 p-3">
          <p class="text-sm font-semibold text-slate-100">${h(photo.label)}</p>
          <p class="text-xs text-slate-400">${h(photo.meta)}</p>
        </div>
      </article>`).join('')
    : '<div class="rounded-2xl border border-dashed border-slate-600/70 bg-slate-950/40 p-6 text-sm text-slate-400">No photos attached yet. Capture a site image or upload one from the device gallery.</div>';

  el['camera-count'].textContent = `${state.photos.length} attached ${state.photos.length === 1 ? 'photo' : 'photos'}`;
}

function addPhoto(src, label, meta) {
  state.photos.unshift({ src, label, meta });
  renderPhotos();
}

async function initAuth() {
  state.auth0 = await createAuth0Client({
    domain: config.auth0Domain,
    clientId: config.auth0ClientId,
    cacheLocation: 'localstorage',
    useRefreshTokens: true,
    authorizationParams: {
      audience: config.auth0Audience,
      redirect_uri: window.location.origin + window.location.pathname,
      scope: 'openid profile email'
    }
  });

  const params = new URLSearchParams(window.location.search);
  if (params.has('code') && params.has('state')) {
    try {
      await state.auth0.handleRedirectCallback();
      window.history.replaceState({}, document.title, window.location.pathname);
    } catch (err) {
      if (!isProduction()) console.error('Redirect callback failed', err);
      clearSession();
      renderAuth();
      return;
    }
  }

  if (await state.auth0.isAuthenticated()) {
    try {
      const user = await state.auth0.getUser();
      const token = await getTokenSilentlyWithRetry();
      if (token) saveSession(token, user);
    } catch (err) {
      if (!isProduction()) console.error('Initial token fetch failed', err);
      clearSession();
    }
  }
  startSilentRefresh();
}

// Production-aware token refresh: retry once with backoff; on login_required/consent_required clear session.
const REFRESH_RETRY_DELAY_MS = 800;
const REFRESH_MAX_RETRIES = 1;

async function getTokenSilentlyWithRetry() {
  if (!state.auth0) return null;
  const opts = { authorizationParams: { audience: config.auth0Audience } };
  let lastErr;
  for (let attempt = 0; attempt <= REFRESH_MAX_RETRIES; attempt++) {
    try {
      const token = await state.auth0.getTokenSilently(opts);
      if (token) return token;
    } catch (e) {
      lastErr = e;
      const code = (e && (e.error || e.message || e.code)) || '';
      const codeStr = typeof code === 'string' ? code : String(code);
      if (/login_required|consent_required|interaction_required/.test(codeStr)) {
        clearSession();
        stopSilentRefresh();
        throw new Error('Session expired. Please sign in again.');
      }
      if (attempt < REFRESH_MAX_RETRIES) await new Promise(r => setTimeout(r, REFRESH_RETRY_DELAY_MS));
    }
  }
  throw lastErr || new Error('Unable to obtain token.');
}

async function getToken() {
  if (!state.auth0) throw new Error('Auth0 client is not ready yet.');
  if (!(await state.auth0.isAuthenticated())) throw new Error('Please sign in with Auth0 first.');
  const fresh = await getTokenSilentlyWithRetry();
  if (!fresh) throw new Error('Please sign in with Auth0 first.');
  saveSession(fresh, await state.auth0.getUser());
  return fresh;
}

async function login() {
  if (!state.auth0) throw new Error('Auth0 client is not ready yet.');
  await state.auth0.loginWithRedirect({ authorizationParams: { audience: config.auth0Audience, redirect_uri: window.location.origin + window.location.pathname } });
}

async function logout() {
  clearSession();
  stopSilentRefresh();
  renderAuth();
  renderIdle();
  if (state.auth0) {
    await state.auth0.logout({ logoutParams: { returnTo: window.location.origin + window.location.pathname } });
  }
}

const REFRESH_INTERVAL_MS = 50 * 60 * 1000;
let silentRefreshTimer = null;

function startSilentRefresh() {
  stopSilentRefresh();
  silentRefreshTimer = setInterval(async () => {
    if (!state.auth0 || !(await state.auth0.isAuthenticated())) return;
    try {
      await getToken();
    } catch (err) {
      clearSession();
      stopSilentRefresh();
      renderAuth();
      if (err?.message?.includes('Session expired')) setPanelMessage('warning', 'Session expired', 'Please sign in again to continue.');
      if (!isProduction()) console.warn('Silent refresh failed', err?.message || err);
    }
  }, REFRESH_INTERVAL_MS);
}

function stopSilentRefresh() {
  if (silentRefreshTimer) {
    clearInterval(silentRefreshTimer);
    silentRefreshTimer = null;
  }
}

// Production-aware: never log tokens. On 401, clear session and surface re-auth; on network error show friendly message.
async function apiFetch(path, options = {}) {
  let token;
  try {
    token = await getToken();
  } catch (err) {
    clearSession();
    stopSilentRefresh();
    renderAuth();
    throw err;
  }
  const headers = new Headers(options.headers);
  headers.set('Authorization', `Bearer ${token}`);
  const url = `${config.apiBaseUrl}${path}`;
  let response;
  try {
    response = await fetch(url, { ...options, headers });
  } catch (networkErr) {
    throw new Error('Network error. Check your connection and try again.');
  }
  if (response.status === 401) {
    clearSession();
    stopSilentRefresh();
    renderAuth();
    const challenge = response.headers.get('WWW-Authenticate') || '';
    let payload = null;
    try { payload = await response.clone().json(); } catch { /* ignore */ }
    const msg = payload?.message || 'Session expired or invalid. Please sign in again.';
    throw Object.assign(new Error(msg), { status: 401, challenge });
  }
  if (response.status === 429) {
    throw new Error('Too many requests. Please wait a moment and try again.');
  }
  return response;
}

function wireVoice() {
  const SpeechRecognition = window.SpeechRecognition || window.webkitSpeechRecognition;
  if (!SpeechRecognition) {
    el['voice-status'].textContent = 'Voice input is not supported in this browser.';
    el['voice-button'].disabled = true;
    return;
  }

  const recognition = new SpeechRecognition();
  recognition.lang = 'en-US';
  recognition.continuous = false;
  recognition.interimResults = true;
  let finalTranscript = '';

  el['voice-button'].addEventListener('click', () => {
    finalTranscript = '';
    try { recognition.start(); } catch { recognition.stop(); recognition.start(); }
  });

  recognition.onstart = () => {
    el['voice-button'].classList.add('ring-2', 'ring-orange-400/60');
    el['voice-status'].textContent = 'Listening for voice commands...';
  };

  recognition.onerror = event => {
    el['voice-button'].classList.remove('ring-2', 'ring-orange-400/60');
    el['voice-status'].textContent = `Voice input error: ${event.error}`;
  };

  recognition.onresult = event => {
    let interim = '';
    for (let i = event.resultIndex; i < event.results.length; i += 1) {
      const text = event.results[i][0].transcript;
      if (event.results[i].isFinal) finalTranscript += `${text} `; else interim += `${text} `;
    }
    const transcript = [finalTranscript, interim].join(' ').trim();
    if (transcript) {
      const current = el['query-input'].value.trim();
      el['query-input'].value = [current, transcript].filter(Boolean).join(' ').trim();
    }
    el['voice-status'].textContent = finalTranscript.trim() ? 'Voice captured.' : 'Listening...';
  };

  recognition.onend = () => {
    el['voice-button'].classList.remove('ring-2', 'ring-orange-400/60');
    if (el['voice-status'].textContent === 'Listening for voice commands...') {
      el['voice-status'].textContent = 'Voice ready.';
    }
  };
}

function wireCamera() {
  el['camera-start'].addEventListener('click', async () => {
    try {
      state.stream = await navigator.mediaDevices.getUserMedia({ video: { facingMode: { ideal: 'environment' } }, audio: false });
      el['camera-video'].srcObject = state.stream;
      await el['camera-video'].play();
      el['camera-status'].textContent = 'Camera ready. Capture a photo when you need one.';
    } catch (error) {
      el['camera-status'].textContent = `Camera access blocked: ${error.message}`;
    }
  });

  el['camera-stop'].addEventListener('click', () => {
    if (state.stream) {
      state.stream.getTracks().forEach(track => track.stop());
      state.stream = null;
    }
    el['camera-video'].srcObject = null;
    el['camera-status'].textContent = 'Camera stopped.';
  });

  el['camera-capture'].addEventListener('click', () => {
    try {
      const video = el['camera-video'];
      const canvas = el['camera-canvas'];
      if (!video.videoWidth || !video.videoHeight) throw new Error('Camera is not ready yet.');
      canvas.width = video.videoWidth;
      canvas.height = video.videoHeight;
      canvas.getContext('2d').drawImage(video, 0, 0, canvas.width, canvas.height);
      addPhoto(canvas.toDataURL('image/jpeg', 0.88), 'Camera capture', new Date().toLocaleString());
      el['camera-status'].textContent = 'Camera frame captured.';
    } catch (error) {
      el['camera-status'].textContent = error.message;
    }
  });

  el['camera-file'].addEventListener('change', event => {
    const files = Array.from(event.target.files || []);
    files.forEach(file => addPhoto(URL.createObjectURL(file), file.name, `${(file.size / 1024 / 1024).toFixed(1)} MB • ${file.type || 'image'}`));
    if (files.length) {
      el['camera-status'].textContent = `${files.length} photo${files.length === 1 ? '' : 's'} added from the device gallery.`;
      event.target.value = '';
    }
  });

  el['clear-photos'].addEventListener('click', () => {
    state.photos.splice(0, state.photos.length);
    renderPhotos();
    setStatus('Cleared attached photos.');
  });
}

async function submitCommand(event) {
  event.preventDefault();
  const userQuery = el['query-input'].value.trim();
  if (!userQuery) {
    renderError('Enter a request, use voice dictation, or capture a site note before submitting.');
    return;
  }

  el['submit-button'].disabled = true;
  el['submit-button'].textContent = 'Processing field command...';
  setStatus('Submitting command to the Foreman API...');

  try {
    const response = await apiFetch('/action', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ userQuery })
    });

    const text = await response.text();
    let payload = null;
    if (text) {
      try { payload = JSON.parse(text); } catch { payload = { raw: text }; }
    }

    if (!response.ok) {
      throw Object.assign(new Error(payload?.message || 'Foreman request failed.'), { status: response.status, challenge: response.headers.get('WWW-Authenticate') || '' });
    }

    renderResult(payload);
    setStatus(`Last updated ${new Date().toLocaleTimeString()}`);
  } catch (error) {
    const is401 = error?.status === 401;
    const isStepUp = is401 && (error?.challenge?.includes('step-up') || error?.challenge?.includes('insufficient_scope'));
    const message = isStepUp
      ? 'This request requires step-up authentication or a new Auth0 session.'
      : (error?.message || 'Unable to process command.');
    renderError(message, error?.challenge || '');
    setStatus(`Request failed at ${new Date().toLocaleTimeString()}`);
    if (is401 && !isStepUp) setPanelMessage('warning', 'Session expired', 'Please sign in again to send commands.');
  } finally {
    el['submit-button'].disabled = false;
    el['submit-button'].textContent = 'Send command';
  }
}

function bindPrompts() {
  document.querySelectorAll('[data-template]').forEach(button => {
    button.addEventListener('click', () => {
      el['query-input'].value = button.getAttribute('data-template');
      el['query-input'].focus();
    });
  });
}

async function bootstrap() {
  [
    'auth-badge', 'auth-note', 'login-button', 'logout-button', 'install-button', 'install-badge', 'last-updated',
    'command-form', 'submit-button', 'query-input', 'voice-button', 'voice-status', 'camera-video', 'camera-canvas',
    'camera-start', 'camera-stop', 'camera-capture', 'camera-file', 'camera-status', 'camera-count', 'photo-gallery',
    'clear-photos', 'response-panel'
  ].forEach($);

  renderIdle();
  renderPhotos();
  bindPrompts();
  wireVoice();
  wireCamera();
  await initAuth();
  renderAuth();

  el['login-button'].addEventListener('click', login);
  el['logout-button'].addEventListener('click', logout);
  el['command-form'].addEventListener('submit', submitCommand);

  if ('serviceWorker' in navigator) {
    navigator.serviceWorker.register('/service-worker.js').catch(() => {});
  }

  window.addEventListener('beforeinstallprompt', event => {
    event.preventDefault();
    state.deferredInstall = event;
    el['install-button'].classList.remove('hidden');
    el['install-badge'].className = 'badge badge-live';
    el['install-badge'].innerHTML = '<span class="status-dot"></span><span>Install ready</span>';
  });

  el['install-button'].addEventListener('click', async () => {
    if (!state.deferredInstall) return;
    state.deferredInstall.prompt();
    await state.deferredInstall.userChoice;
    state.deferredInstall = null;
    el['install-button'].classList.add('hidden');
  });

  setPanelMessage('success', 'Ready for field work', 'Authenticate, dictate a task, attach site photos, and send the command to the Foreman API.');
  setStatus('Ready.');
}

window.addEventListener('DOMContentLoaded', () => {
  bootstrap().catch(error => {
    if (!isProduction()) console.error('Bootstrap error', error);
    const panel = document.getElementById('response-panel');
    if (panel) {
      panel.className = 'rounded-3xl border border-red-500/30 bg-red-950/40 p-5 text-red-100';
      panel.innerHTML = `<p class="text-sm font-semibold uppercase tracking-[0.18em] text-red-300">Initialization failed</p><p class="mt-2 text-base">${h(error.message || 'Failed to initialize the PWA shell.')}</p>`;
    }
  });
});
