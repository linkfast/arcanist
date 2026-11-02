// Arcanist's own Jukebox Jam client (/players/jam/#/jam/<code>): follow a Jam, vote, browse its music and add songs;
// with an account, also hear it from here, in step with its host.
// With an account (the panel's session cookie) or as a guest. No dependencies; the server's REST API, and the
// jukeboxJamChanged subscription (GraphQL over WebSocket, open without an account) to know when to read it again.
'use strict';

(() => {
  const API = '/rest/v1';
  const ES = (navigator.language || 'en').toLowerCase().startsWith('es');
  const T = ES
    ? {
        title: 'Jukebox Jam', noCode: 'Abre el enlace o el QR de un Jam para entrar.', ended: 'Este Jam terminó.',
        gone: 'Este Jam no existe o ya terminó.', hostedBy: 'de', join: 'Unirme', yourName: 'Tu nombre',
        joinText: 'Ponte un nombre para añadir canciones y votar.', joinAccount: 'Entra con tu cuenta para unirte a este Jam.',
        goneOrAccount: 'Este Jam pide entrar con una cuenta, o ya terminó.',
        hostGone: 'El anfitrión se desconectó: la música no avanza hasta que vuelva.', endJam: 'Terminar el Jam',
        endConfirm: '¿Terminar el Jam para todos?', suggested: 'sugerida', likes: (names) => `le gusta a ${names.join(', ')}`, up: 'Subir', down: 'Bajar',
        signIn: 'Iniciar sesión', nothing: 'No suena nada ahora.', hostSong: 'Del anfitrión', paused: 'En pausa',
        queue: 'Cola', add: 'Añadir', invite: 'Invitar', upNext: 'A continuación', empty: 'La cola está vacía: ¡añade algo!',
        unvote: 'Quitar mi voto',
        played: 'Ya sonaron', pinned: 'suena a continuación', search: 'Buscar canciones, artistas o álbumes', artists: 'Artistas', albums: 'Álbumes',
        recent: 'Recientes', songs: 'Canciones', addBtn: 'Añadir', added: 'En la cola', playNow: 'Que suene ya',
        addAll: 'Añadir todas', back: 'Atrás', more: 'Ver más', vote: 'Votar', votes: (n) => `${n} ${n === 1 ? 'voto' : 'votos'}`,
        addedBy: 'añadida por', dj: 'AI DJ de Ultimate', link: 'Enlace para entrar', copy: 'Copiar', copied: 'Copiado',
        share: 'Compartir', skip: 'Saltar', pause: 'Pausar', resume: 'Seguir', remove: 'Quitar', pin: 'Que sea la siguiente',
        addsLeft: (n) => `Puedes añadir ${n} más ahora`, you: 'Tú',
        listen: 'Escuchar aquí', stopListening: 'Dejar de escuchar', volume: 'Volumen', listeners: (names) => `Lo escuchan: ${names.join(', ')}`,
        listenAccount: 'Con una cuenta también puedes oírlo desde donde estés.', notYours: 'Esta canción no está en tus bibliotecas: sigue la música con la próxima.',
        tapToPlay: 'Pulsa para seguir escuchando', cannotPlay: 'Este navegador no puede reproducir esta canción.', listenWaiting: 'Ahora no suena nada.',
        errors: {
          jam_ended: 'Este Jam terminó.', jam_full: 'Este Jam ya no admite más invitados.',
          jam_guest_limit: 'Ya añadiste varias canciones: espera un poco para añadir más.',
          jam_guest_removed: 'El anfitrión te sacó de este Jam.', jam_guests_disabled: 'Este Jam no admite invitados sin cuenta.',
          jam_track_not_allowed: 'Esa canción no se puede añadir a este Jam.',
          jam_listen_account: 'Para escucharlo desde aquí hace falta una cuenta.',
          jam_voting_off: 'Este Jam no tiene votaciones.', jam_replace_off: 'En este Jam no se puede cambiar la que suena.',
          not_found: 'No se encontró.',
          forbidden: 'No puedes hacer eso.', rate_limited: 'Demasiadas peticiones: espera un momento.',
          validation_failed: 'Revisa lo que escribiste.', default: 'Algo salió mal. Inténtalo de nuevo.',
        },
      }
    : {
        title: 'Jukebox Jam', noCode: 'Open a Jam\'s link or QR code to join it.', ended: 'This Jam has ended.',
        gone: 'This Jam does not exist or has ended.', hostedBy: 'by', join: 'Join', yourName: 'Your name',
        joinText: 'Give yourself a name to add songs and vote.', joinAccount: 'Sign in with your account to join this Jam.',
        goneOrAccount: 'This Jam asks you to sign in with an account, or it has ended.',
        hostGone: 'The host went offline: the music will not move on until they are back.', endJam: 'End the Jam',
        endConfirm: 'End the Jam for everyone?', suggested: 'suggested', likes: (names) => `${names.join(', ')} liked it`, up: 'Move up', down: 'Move down',
        signIn: 'Sign in', nothing: 'Nothing is playing now.', hostSong: 'From the host', paused: 'Paused',
        queue: 'Queue', add: 'Add', invite: 'Invite', upNext: 'Up next', empty: 'The queue is empty: add something!',
        unvote: 'Take my vote back',
        played: 'Played', pinned: 'plays next', search: 'Search songs, artists or albums', artists: 'Artists', albums: 'Albums',
        recent: 'Recent', songs: 'Songs', addBtn: 'Add', added: 'Queued', playNow: 'Play now',
        addAll: 'Add all', back: 'Back', more: 'Show more', vote: 'Vote', votes: (n) => `${n} ${n === 1 ? 'vote' : 'votes'}`,
        addedBy: 'added by', dj: 'Ultimate AI DJ', link: 'Link to join', copy: 'Copy', copied: 'Copied',
        share: 'Share', skip: 'Skip', pause: 'Pause', resume: 'Resume', remove: 'Remove', pin: 'Play next',
        addsLeft: (n) => `You can add ${n} more now`, you: 'You',
        listen: 'Listen here', stopListening: 'Stop listening', volume: 'Volume', listeners: (names) => `Listening: ${names.join(', ')}`,
        listenAccount: 'With an account you can also hear it from where you are.', notYours: 'This song is not in your libraries: the music comes back with the next one.',
        tapToPlay: 'Tap to keep listening', cannotPlay: 'This browser cannot play this song.', listenWaiting: 'Nothing is playing right now.',
        errors: {
          jam_ended: 'This Jam has ended.', jam_full: 'This Jam takes no more guests.',
          jam_guest_limit: 'You added several songs: wait a little to add more.',
          jam_guest_removed: 'The host took you out of this Jam.', jam_guests_disabled: 'This Jam takes no guests without an account.',
          jam_track_not_allowed: 'That song cannot be added to this Jam.',
          jam_listen_account: 'Hearing it from here takes an account.',
          jam_voting_off: 'This Jam has no voting.', jam_replace_off: 'The song playing cannot be changed in this Jam.',
          not_found: 'Not found.',
          forbidden: 'You cannot do that.', rate_limited: 'Too many requests: wait a moment.',
          validation_failed: 'Check what you wrote.', default: 'Something went wrong. Try again.',
        },
      };
  document.documentElement.lang = ES ? 'es' : 'en';

  const code = (/^#\/jam\/([A-Za-z0-9_-]+)/.exec(location.hash) || [])[1];
  const guestKey = `arcanist.jam.${code}.guest`;
  const state = { token: null, guest: read(guestKey), jam: null, tab: 'queue', browse: { view: 'home' }, error: null };

  function read(key) {
    try { return localStorage.getItem(key); } catch { return null; }
  }
  function write(key, value) {
    try { value == null ? localStorage.removeItem(key) : localStorage.setItem(key, value); } catch { /* private mode */ }
  }

  // —— DOM ——
  function h(tag, attrs, ...children) {
    const element = document.createElement(tag);
    for (const [name, value] of Object.entries(attrs || {})) {
      if (value == null || value === false) continue;
      if (name.startsWith('on')) element.addEventListener(name.slice(2), value);
      else if (name === 'class') element.className = value;
      else element.setAttribute(name, value === true ? '' : value);
    }
    for (const child of children.flat()) if (child != null && child !== false) element.append(child.nodeType ? child : String(child));
    return element;
  }
  const art = (id, size = 128) => (id ? h('img', { class: 'art', src: `${API}/artwork/${id}?size=${size}`, alt: '', loading: 'lazy' }) : h('div', { class: 'art' }));
  let toastTimer;
  function toast(text) {
    const box = document.getElementById('toast');
    box.textContent = text;
    box.hidden = false;
    clearTimeout(toastTimer);
    toastTimer = setTimeout(() => (box.hidden = true), 3500);
  }

  // —— API ——
  class ApiError extends Error {
    constructor(code, status, body) { super(code); this.code = code; this.status = status; this.body = body; }
  }
  async function api(path, options = {}) {
    const headers = { Accept: 'application/json', ...(options.body ? { 'Content-Type': 'application/json' } : {}) };
    if (state.token) headers.Authorization = `Bearer ${state.token}`;
    else if (state.guest) headers['X-Arcanist-Guest'] = state.guest;
    // The Jam's own routes are relative to it (`/tracks`); the rest of the API is given whole.
    const response = await fetch(path.startsWith(`${API}/`) ? path : `${API}/public/jams/${code}${path}`, {
      method: options.method || 'GET', headers, credentials: 'same-origin', body: options.body ? JSON.stringify(options.body) : undefined, keepalive: options.keepalive,
    });
    const body = response.status === 204 ? null : await response.json().catch(() => null);
    if (!response.ok) throw new ApiError(body?.code || (response.status === 429 ? 'rate_limited' : response.status === 404 ? 'not_found' : 'default'), response.status, body);
    return body;
  }
  function explain(error) {
    return T.errors[error.code] || T.errors.default;
  }
  async function act(work) {
    try {
      await work();
      await load();
    } catch (error) {
      toast(explain(error));
      if (error.code === 'jam_guest_removed') { write(guestKey, null); state.guest = null; await load(); }
    }
  }

  /** The panel's session (a refresh cookie), when this browser has one. */
  async function signIn() {
    try {
      const response = await fetch(`${API}/auth/refresh`, {
        method: 'POST', credentials: 'same-origin', headers: { 'Content-Type': 'application/json' }, body: '{}',
      });
      if (!response.ok) return;
      const tokens = await response.json();
      state.token = tokens.accessToken;
      // Refreshed a minute before it expires.
      const wait = new Date(tokens.accessTokenExpiresAt).getTime() - Date.now() - 60_000;
      setTimeout(signIn, Math.max(30_000, wait));
    } catch { /* offline: a guest, or nothing */ }
  }

  async function load() {
    try {
      state.jam = await api('');
      state.error = null;
    } catch (error) {
      state.error = error;
    }
    render();
  }

  // —— Live updates: jukeboxJamChanged over graphql-transport-ws; a slow reread when it is not available. ——
  let socket, reread;
  function live() {
    const url = `${location.protocol === 'https:' ? 'wss' : 'ws'}://${location.host}/graphql`;
    try {
      socket = new WebSocket(url, 'graphql-transport-ws');
    } catch { return; }
    socket.onopen = () => socket.send(JSON.stringify({ type: 'connection_init', payload: state.token ? { Authorization: `Bearer ${state.token}` } : {} }));
    socket.onmessage = (message) => {
      const data = JSON.parse(message.data);
      if (data.type === 'connection_ack')
        socket.send(JSON.stringify({ id: '1', type: 'subscribe', payload: { query: 'subscription($c: String!) { jukeboxJamChanged(code: $c) { kind } }', variables: { c: code } } }));
      else if (data.type === 'ping') socket.send(JSON.stringify({ type: 'pong' }));
      else if (data.type === 'next') {
        clearTimeout(reread);
        reread = setTimeout(() => { load(); if (listening.on) askWhatPlays(); }, 250);
      }
    };
    socket.onclose = () => setTimeout(live, 5000);
  }
  setInterval(() => { if (document.visibilityState === 'visible') load(); }, 30_000);
  document.addEventListener('visibilitychange', () => { if (document.visibilityState === 'visible') load(); });
  // The song's position moves on between updates.
  setInterval(() => { const bar = document.querySelector('.progress > div'); if (bar) bar.style.width = `${progress()}%`; }, 1000);

  // —— Hearing it from here: the server says what plays and where; this keeps a player there. ——
  const SEEK_AT = 2; // seconds off: jump there
  const NUDGE_AT = 0.15; // seconds off: speed up or slow down a little, which nobody hears
  const listening = {
    on: false, audio: null, what: null, clockOffset: 0, key: null, url: null, base: 0, fallback: false, failed: false, blocked: false,
    reloadedAt: 0, heardMs: 0, heardTrack: null, volume: Number(read('arcanist.jam.volume') ?? 1), warmed: null, warmer: null,
  };

  function startListening() {
    // Created and started inside the tap: browsers then let this same player go on by itself.
    const audio = new Audio();
    audio.preload = 'auto';
    audio.volume = listening.volume;
    audio.addEventListener('error', onPlayerError);
    audio.play().catch(() => {});
    Object.assign(listening, { on: true, audio, key: null, blocked: false, failed: false });
    askWhatPlays();
    render();
  }

  function stopListening() {
    reportHeard();
    listening.audio?.pause();
    listening.audio?.removeAttribute('src');
    Object.assign(listening, { on: false, audio: null, what: null, key: null, warmer: null, warmed: null });
    act(() => api('/listen', { method: 'DELETE' }));
  }

  let asking = false;
  async function askWhatPlays() {
    if (asking) return;
    asking = true;
    try {
      const what = await api('/listen', { method: 'POST' });
      if (listening.on) {
        listening.what = what;
        listening.clockOffset = new Date(what.serverTime).getTime() - Date.now();
        follow();
      }
    } catch (error) {
      if (error.code === 'jam_ended' || error.status === 404) stopPlayerOnly();
    } finally {
      asking = false;
    }
  }

  function stopPlayerOnly() {
    reportHeard();
    listening.audio?.pause();
    Object.assign(listening, { on: false, audio: null, what: null, key: null });
    render();
  }

  /** Where the song should be now, in seconds. */
  function shouldBeAt(what) {
    const moved = what.state === 'PLAYING' && what.at ? Date.now() + listening.clockOffset - new Date(what.at).getTime() : 0;
    const position = what.positionMs + Math.max(0, moved);
    return (what.durationMs ? Math.min(position, what.durationMs) : position) / 1000;
  }

  /**
   * Opens the song with the quality the server gives web players. An original opens at its start and jumps with
   * Range; a conversion has nothing to jump to, so it is asked to start further on (`converted`). Only after an error
   * does it ask for a format every browser plays.
   */
  function openSong(at, converted) {
    const audio = listening.audio;
    const params = ['client=WEB'];
    if (listening.fallback) params.push('format=mp3');
    if (converted && at > 0.5) params.push(`timeOffset=${at.toFixed(1)}`);
    listening.base = converted ? at : 0;
    listening.reloadedAt = Date.now();
    audio.src = `${listening.url}&${params.join('&')}`;
    audio.playbackRate = 1;
    audio.load();
  }

  function onPlayerError() {
    if (!listening.on || !listening.what?.streamUrl) return;
    if (!listening.fallback) {
      listening.fallback = true;
      openSong(shouldBeAt(listening.what), true);
    } else {
      listening.failed = true;
      render();
    }
  }

  function canJumpTo(at) {
    const audio = listening.audio;
    const { seekable } = audio;
    return Number.isFinite(audio.duration) && seekable.length > 0 && at - listening.base <= seekable.end(seekable.length - 1) && at >= listening.base;
  }

  /** Called every second and with every answer: changes song, starts, pauses, and corrects the distance to the host. */
  function follow() {
    const { what, audio } = listening;
    if (!listening.on || !what) return;
    const hearing = what.state === 'PLAYING' || what.state === 'PAUSED';
    if (!hearing) {
      reportHeard();
      if (!audio.paused) audio.pause();
      if (listening.key) { listening.key = null; audio.removeAttribute('src'); }
      return;
    }

    const key = what.entryId || what.trackId;
    const target = shouldBeAt(what);
    if (key !== listening.key) {
      reportHeard();
      Object.assign(listening, { key, url: what.streamUrl, fallback: false, failed: false, heardTrack: what.trackId });
      openSong(target, false);
      render();
    }
    warmNext(what);
    if (listening.failed || audio.readyState < 1) return;

    const off = listening.base + audio.currentTime - target;
    if (what.state === 'PAUSED') {
      if (!audio.paused) audio.pause();
      if (Math.abs(off) > 1 && canJumpTo(target)) audio.currentTime = target - listening.base;
      return;
    }
    if (audio.ended) return; // Its end came before the host's: the next song arrives with the next answer.
    if (audio.paused) {
      audio.play().then(() => { if (listening.blocked) { listening.blocked = false; render(); } }).catch(() => { if (!listening.blocked) { listening.blocked = true; render(); } });
    }
    if (Math.abs(off) > SEEK_AT) {
      audio.playbackRate = 1;
      if (canJumpTo(target)) audio.currentTime = target - listening.base;
      // Starts again where it should be; once that was tried, not more than every so often.
      else if (listening.base === 0 || Date.now() - listening.reloadedAt > 20_000) openSong(target, true);
    } else if (Math.abs(off) > NUDGE_AT) {
      audio.playbackRate = off > 0 ? 0.97 : 1.03;
    } else {
      audio.playbackRate = 1;
    }
  }

  /** The next song starts sooner when the browser already has its beginning. */
  function warmNext(what) {
    if (!what.nextTrackId || listening.warmed === what.nextTrackId) return;
    listening.warmed = what.nextTrackId;
    listening.warmer = new Audio(`${what.nextStreamUrl}&client=WEB`);
    listening.warmer.preload = 'auto';
    listening.warmer.muted = true;
  }

  /** What was heard counts for this user, as with any player; the server decides whether it is a play. */
  function reportHeard() {
    const { heardMs, heardTrack } = listening;
    listening.heardMs = 0;
    if (heardTrack && heardMs >= 1000)
      api(`${API}/plays`, { method: 'POST', keepalive: true, body: { trackId: heardTrack, playedMs: Math.round(heardMs), client: 'Jukebox Jam' } }).catch(() => {});
  }

  setInterval(() => {
    if (!listening.on) return;
    if (listening.audio && !listening.audio.paused && !listening.audio.ended) listening.heardMs += 1000;
    follow();
  }, 1000);
  setInterval(() => { if (listening.on) askWhatPlays(); }, 15_000);
  addEventListener('pagehide', () => { if (listening.on) reportHeard(); });

  // —— Views ——
  /** Signing in comes back to this Jam. */
  const signInLink = () => h('a', { class: 'btn primary', href: `/admin/login?returnUrl=${encodeURIComponent(`${location.pathname}#/jam/${code}`)}` }, T.signIn);

  function render() {
    const app = document.getElementById('app');
    app.replaceChildren(...view().filter(Boolean));
  }

  function view() {
    if (!code) return [h('p', { class: 'center muted' }, T.noCode)];
    // Without a session, a Jam closed to guests answers as if it did not exist: it may just need an account.
    if (state.error && !state.jam && state.error.status === 404 && !state.token)
      return [h('div', { class: 'center' }, h('p', { class: 'muted' }, T.goneOrAccount), h('p', { style: 'margin-top:12px' }, signInLink()))];
    if (state.error && !state.jam) return [h('p', { class: 'center muted' }, state.error.status === 404 ? T.gone : explain(state.error))];
    const jam = state.jam;
    if (!jam) return [h('p', { class: 'center muted' }, '…')];
    document.title = `${jam.name} · ${T.title}`;
    const head = h('header', {},
      h('div', { class: 'grow' }, h('h1', { class: 'ellipsis' }, jam.name), h('p', { class: 'muted' }, `${T.title} ${T.hostedBy} ${jam.host}`)),
      jam.dj ? h('span', { class: 'badge' }, T.dj) : null);
    if (jam.endedAt) return [head, h('p', { class: 'center muted' }, T.ended)];
    if (jam.you.participation === 'VISITOR') return [head, nowPlaying(jam), joinForm(jam)];
    const tabs = h('nav', { class: 'tabs', role: 'tablist' },
      ...[['queue', T.queue], ['add', T.add], ['invite', T.invite]].map(([id, label]) =>
        h('button', { role: 'tab', 'aria-selected': String(state.tab === id), onclick: () => { state.tab = id; state.browser = null; render(); } }, label)));
    // The browser keeps what it shows (and what is being typed) while the Jam changes.
    const body = state.tab === 'add' ? (state.browser ??= browser(jam)) : state.tab === 'invite' ? invite(jam) : queue(jam);
    return [head, hostGone(jam), nowPlaying(jam), controls(jam), listenBar(jam), tabs, body];
  }

  function hostGone(jam) {
    if (!jam.hostGone) return null;
    return h('div', { class: 'card notice' }, h('p', {}, T.hostGone),
      jam.you.canManage
        ? h('p', { style: 'margin-top:8px' }, h('button', { class: 'btn small', onclick: () => { if (confirm(T.endConfirm)) act(() => api('/end', { method: 'POST' })); } }, T.endJam))
        : null);
  }

  function progress() {
    const now = state.jam?.nowPlaying;
    const song = now?.entry?.song || now?.external;
    if (!song?.durationMs) return 0;
    const moved = now.paused || !now.at ? 0 : Date.now() - new Date(now.at).getTime();
    return Math.min(100, ((now.positionMs + moved) / song.durationMs) * 100);
  }

  function nowPlaying(jam) {
    const now = jam.nowPlaying;
    const song = now.entry?.song || now.external;
    if (!song) return h('div', { class: 'card muted' }, T.nothing);
    return h('div', { class: 'card' },
      h('div', { class: 'now' }, art(song.artworkId, 256),
        h('div', { class: 'grow' },
          h('p', { class: 'ellipsis' }, h('strong', {}, song.title)),
          h('p', { class: 'muted ellipsis' }, song.artists.join(', ')),
          h('p', { class: 'muted ellipsis' }, now.entry ? (now.entry.addedBy ? `${T.addedBy} ${now.entry.addedBy}` : now.entry.addedByDj ? T.dj : '') : T.hostSong),
          now.paused ? h('span', { class: 'badge' }, T.paused) : null)),
      h('div', { class: 'progress' }, h('div', { style: `width:${progress()}%` })));
  }

  function controls(jam) {
    if (!jam.you.canControl) return null;
    const paused = jam.nowPlaying.paused;
    return h('div', { class: 'row', style: 'margin-top:8px' },
      h('button', { class: 'btn small', onclick: () => act(() => api('/control', { method: 'POST', body: { action: paused ? 'RESUME' : 'PAUSE' } })) }, paused ? T.resume : T.pause),
      h('button', { class: 'btn small', onclick: () => act(() => api('/control', { method: 'POST', body: { action: 'SKIP' } })) }, T.skip));
  }

  function listenBar(jam) {
    if (!jam.you.canListen) return jam.you.participation === 'GUEST' ? h('p', { class: 'muted listen' }, T.listenAccount) : null;
    if (!listening.on) return h('div', { class: 'row listen' }, h('button', { class: 'btn small primary', onclick: startListening }, `🎧 ${T.listen}`), listenersLine(jam));
    const what = listening.what;
    const note = listening.failed ? T.cannotPlay
      : listening.blocked ? null
      : what?.state === 'UNAVAILABLE' ? T.notYours
      : what?.state === 'NOTHING' ? T.listenWaiting
      : null;
    const volume = h('input', {
      type: 'range', min: '0', max: '1', step: '0.05', value: String(listening.volume), 'aria-label': T.volume, title: T.volume,
      oninput: (event) => {
        listening.volume = Number(event.target.value);
        if (listening.audio) listening.audio.volume = listening.volume;
        write('arcanist.jam.volume', String(listening.volume));
      },
    });
    return h('div', { class: 'listen' },
      h('div', { class: 'row' },
        h('button', { class: 'btn small', onclick: stopListening }, T.stopListening),
        volume,
        listening.blocked ? h('button', { class: 'btn small primary', onclick: () => { listening.audio.play().catch(() => {}); listening.blocked = false; render(); } }, T.tapToPlay) : null),
      note ? h('p', { class: 'muted' }, note) : null,
      listenersLine(jam));
  }

  function listenersLine(jam) {
    return jam.listeners?.length ? h('p', { class: 'muted' }, T.listeners(jam.listeners)) : null;
  }

  function joinForm(jam) {
    // Signed in: a member with one click. Without an account: a guest by name, when the Jam takes guests.
    if (state.token)
      return h('div', { style: 'margin-top:16px' }, h('button', { class: 'btn primary', onclick: () => act(() => api('/join', { method: 'POST', body: {} })) }, T.join));
    if (!jam.allowGuests)
      return h('div', { class: 'card', style: 'margin-top:16px' }, h('p', {}, T.joinAccount),
        h('p', { style: 'margin-top:12px' }, signInLink()));
    const name = h('input', { type: 'text', maxlength: '50', placeholder: T.yourName, autocomplete: 'nickname', required: true, value: read('arcanist.jam.name') || '' });
    return h('form', {
      class: 'join card', onsubmit: (event) => {
        event.preventDefault();
        act(async () => {
          const session = await api('/join', { method: 'POST', body: { name: name.value.trim() } });
          write('arcanist.jam.name', name.value.trim());
          if (session?.token) { state.guest = session.token; write(guestKey, session.token); }
        });
      },
    }, h('p', {}, T.joinText), name, h('button', { class: 'btn primary', type: 'submit' }, T.join));
  }

  function entryRow(jam, entry, waiting) {
    const you = jam.you;
    const actions = [];
    if (waiting && jam.voting)
      actions.push(h('button', { class: `btn small${entry.votedByYou ? ' primary' : ''}`, title: entry.votedByYou ? T.unvote : T.vote, 'aria-pressed': String(entry.votedByYou), onclick: () => act(() => api('/votes', { method: 'POST', body: { entryId: entry.id } })) }, `▲ ${entry.votes}`));
    if (waiting && you.canControl) {
      if (!jam.voting && !jam.dj) {
        const index = jam.queue.indexOf(entry);
        const move = (position) => act(() => api('/control', { method: 'POST', body: { action: 'MOVE', entryId: entry.id, position } }));
        actions.push(h('button', { class: 'btn small', title: T.up, 'aria-label': T.up, disabled: index === 0, onclick: () => move(index - 1) }, '↑'));
        actions.push(h('button', { class: 'btn small', title: T.down, 'aria-label': T.down, disabled: index === jam.queue.length - 1, onclick: () => move(index + 1) }, '↓'));
      }
      actions.push(h('button', { class: 'btn small', title: T.pin, onclick: () => act(() => api('/control', { method: 'POST', body: { action: 'PIN', entryId: entry.id } })) }, '⤒'));
      actions.push(h('button', { class: 'btn small', title: T.remove, onclick: () => act(() => api('/control', { method: 'POST', body: { action: 'REMOVE', entryId: entry.id } })) }, '✕'));
    }
    return h('li', { class: 'row song' }, art(entry.song.artworkId),
      h('div', { class: 'grow' },
        h('p', { class: 'ellipsis' }, entry.song.title),
        h('p', { class: 'muted ellipsis' }, [entry.song.artists.join(', '),
          entry.addedByYou ? ` · ${T.you}` : entry.addedBy ? ` · ${entry.addedBy}` : entry.addedByDj ? ` · ${T.dj}` : entry.suggested ? ` · ${T.suggested}` : '',
          entry.suggested && entry.likedBy?.length ? ` · ${T.likes(entry.likedBy)}` : '',
          waiting && entry.pinned ? ` · ${T.pinned}` : '',
          waiting && jam.voting && !actions.length ? ` · ${T.votes(entry.votes)}` : ''].join(''))),
      ...actions);
  }

  function queue(jam) {
    const parts = [h('h2', {}, T.upNext)];
    parts.push(jam.queue.length ? h('ul', { class: 'list' }, jam.queue.map((e) => entryRow(jam, e, true))) : h('p', { class: 'muted' }, T.empty));
    if (jam.you.addsLeft != null) parts.push(h('p', { class: 'muted', style: 'margin-top:8px' }, T.addsLeft(jam.you.addsLeft)));
    if (jam.played.length) parts.push(h('h2', {}, T.played), h('ul', { class: 'list' }, jam.played.slice(0, 20).map((e) => entryRow(jam, e, false))));
    return h('section', {}, ...parts);
  }

  function invite(jam) {
    const url = new URL(jam.url, location.origin).href;
    return h('section', {},
      h('h2', {}, T.link),
      h('input', { type: 'text', readonly: true, value: url, onfocus: (e) => e.target.select() }),
      h('div', { class: 'row', style: 'margin-top:8px' },
        h('button', { class: 'btn', onclick: async () => { try { await navigator.clipboard.writeText(url); toast(T.copied); } catch { /* not allowed */ } } }, T.copy),
        navigator.share ? h('button', { class: 'btn', onclick: () => navigator.share({ title: jam.name, url }).catch(() => {}) }, T.share) : null));
  }

  // —— Adding songs: search, artists, albums ——
  function songRow(jam, result) {
    const song = result.song;
    const add = h('button', {
      class: 'btn small', disabled: result.inQueue,
      onclick: () => act(async () => { await api('/tracks', { method: 'POST', body: { trackIds: [song.trackId] } }); result.inQueue = true; toast(T.added); }),
    }, result.inQueue ? T.added : T.addBtn);
    const now = state.jam.you.canReplace
      ? h('button', { class: 'btn small primary', title: T.playNow, onclick: () => act(() => api('/now-playing', { method: 'POST', body: { trackId: song.trackId } })) }, '⚡')
      : null;
    return h('li', { class: 'row song' }, art(song.artworkId),
      h('div', { class: 'grow' }, h('p', { class: 'ellipsis' }, song.title), h('p', { class: 'muted ellipsis' }, [song.artists.join(', '), song.album].filter(Boolean).join(' · '))),
      add, now);
  }

  function browser(jam) {
    const b = state.browse;
    const section = h('section', {});
    const search = h('input', { type: 'search', placeholder: T.search, value: b.query || '' });
    let typing;
    search.addEventListener('input', () => {
      clearTimeout(typing);
      typing = setTimeout(() => { b.query = search.value.trim(); b.view = b.query ? 'search' : 'home'; fill(); }, 300);
    });
    const content = h('div', {});
    section.append(search, content);

    async function fill() {
      content.replaceChildren(h('p', { class: 'muted', style: 'margin-top:12px' }, '…'));
      try {
        content.replaceChildren(...(await (b.view === 'search' ? searchView() : b.view === 'artist' ? artistView() : b.view === 'album' ? albumView() : homeView())));
      } catch (error) {
        content.replaceChildren(h('p', { class: 'error', style: 'margin-top:12px' }, explain(error)));
      }
    }

    const albumTile = (album) => h('button', { class: 'tile', onclick: () => { Object.assign(b, { view: 'album', album, from: b.view }); fill(); } },
      art(album.artworkId, 256), h('p', { class: 'ellipsis', style: 'margin-top:6px' }, album.title),
      h('p', { class: 'muted ellipsis' }, [album.artist, album.year].filter(Boolean).join(' · ')));
    const artistTile = (artist) => h('button', { class: 'tile', onclick: () => { Object.assign(b, { view: 'artist', artist }); fill(); } },
      art(artist.artworkId, 256), h('p', { class: 'ellipsis', style: 'margin-top:6px' }, artist.name));

    async function homeView() {
      const mode = b.mode || 'recent';
      const chips = h('div', { class: 'chips' }, ...[['recent', T.recent], ['albums', T.albums], ['artists', T.artists]].map(([id, label]) =>
        h('button', { class: `btn small${mode === id ? ' primary' : ''}`, onclick: () => { b.mode = id; fill(); } }, label)));
      if (mode === 'artists') {
        const page = await api('/artists?first=60');
        return [chips, h('div', { class: 'grid' }, page.items.map(artistTile))];
      }
      const page = await api(`/albums?first=60&sort=${mode === 'recent' ? 'RECENTLY_ADDED' : 'TITLE'}`);
      return [chips, h('div', { class: 'grid' }, page.items.map(albumTile))];
    }

    async function searchView() {
      const q = encodeURIComponent(b.query);
      const [songs, artists, albums] = await Promise.all([api(`/search?query=${q}&first=20`), api(`/artists?search=${q}&first=12`), api(`/albums?search=${q}&first=12`)]);
      const parts = [];
      if (songs.length) parts.push(h('h2', {}, T.songs), h('ul', { class: 'list' }, songs.map((r) => songRow(jam, r))));
      if (artists.items.length) parts.push(h('h2', {}, T.artists), h('div', { class: 'grid' }, artists.items.map(artistTile)));
      if (albums.items.length) parts.push(h('h2', {}, T.albums), h('div', { class: 'grid' }, albums.items.map(albumTile)));
      return parts.length ? parts : [h('p', { class: 'muted', style: 'margin-top:12px' }, '—')];
    }

    async function artistView() {
      const page = await api(`/albums?artistId=${b.artist.id}&first=100`);
      return [h('p', { style: 'margin:12px 0' }, h('button', { class: 'link', onclick: () => { b.view = b.query ? 'search' : 'home'; fill(); } }, `← ${T.back}`)),
        h('h2', {}, b.artist.name), h('div', { class: 'grid' }, page.items.map(albumTile))];
    }

    async function albumView() {
      const songs = await api(`/albums/${b.album.id}/songs`);
      const waiting = songs.filter((r) => !r.inQueue);
      const limit = state.jam.you.addsLeft ?? Infinity;
      return [h('p', { style: 'margin:12px 0' }, h('button', { class: 'link', onclick: () => { b.view = b.artist && b.from !== 'search' ? 'artist' : b.query ? 'search' : 'home'; fill(); } }, `← ${T.back}`)),
        h('div', { class: 'row' }, art(b.album.artworkId, 256), h('div', { class: 'grow' }, h('h2', { style: 'margin:0' }, b.album.title), h('p', { class: 'muted' }, b.album.artist)),
          h('button', {
            class: 'btn small', disabled: !waiting.length || limit < waiting.length,
            onclick: () => act(async () => { await api('/tracks', { method: 'POST', body: { trackIds: waiting.slice(0, 50).map((r) => r.song.trackId) } }); toast(T.added); }),
          }, T.addAll)),
        h('ul', { class: 'list', style: 'margin-top:8px' }, songs.map((r) => songRow(jam, r)))];
    }

    fill();
    return section;
  }

  // —— Start ——
  (async () => {
    if (!code) return render();
    await signIn();
    await load();
    live();
  })();
})();
