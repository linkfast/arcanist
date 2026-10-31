// Arcanist's own Jukebox Jam client (/players/jam/#/jam/<code>): follow a Jam, vote, browse its music and add songs.
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
        signIn: 'Iniciar sesión', nothing: 'No suena nada ahora.', hostSong: 'Del anfitrión', paused: 'En pausa',
        queue: 'Cola', add: 'Añadir', invite: 'Invitar', upNext: 'A continuación', empty: 'La cola está vacía: ¡añade algo!',
        played: 'Ya sonaron', pinned: 'suena a continuación', search: 'Buscar canciones, artistas o álbumes', artists: 'Artistas', albums: 'Álbumes',
        recent: 'Recientes', songs: 'Canciones', addBtn: 'Añadir', added: 'En la cola', playNow: 'Que suene ya',
        addAll: 'Añadir todas', back: 'Atrás', more: 'Ver más', vote: 'Votar', votes: (n) => `${n} ${n === 1 ? 'voto' : 'votos'}`,
        addedBy: 'añadida por', dj: 'AI DJ de Ultimate', link: 'Enlace para entrar', copy: 'Copiar', copied: 'Copiado',
        share: 'Compartir', skip: 'Saltar', pause: 'Pausar', resume: 'Seguir', remove: 'Quitar', pin: 'Que sea la siguiente',
        addsLeft: (n) => `Puedes añadir ${n} más ahora`, waitVote: (s) => `Podrás votar de nuevo en ${s} s`,
        waitReplace: (s) => `Podrás cambiar la que suena en ${s} s`, you: 'Tú',
        errors: {
          jam_ended: 'Este Jam terminó.', jam_full: 'Este Jam ya no admite más invitados.',
          jam_guest_limit: 'Ya añadiste varias canciones: espera un poco para añadir más.',
          jam_guest_removed: 'El anfitrión te sacó de este Jam.', jam_guests_disabled: 'Este Jam no admite invitados sin cuenta.',
          jam_track_not_allowed: 'Esa canción no se puede añadir a este Jam.', jam_vote_too_soon: 'Espera un poco para volver a votar.',
          jam_voting_off: 'Este Jam no tiene votaciones.', jam_replace_off: 'En este Jam no se puede cambiar la que suena.',
          jam_replace_too_soon: 'Alguien acaba de cambiar la que suena: espera un poco.', not_found: 'No se encontró.',
          forbidden: 'No puedes hacer eso.', rate_limited: 'Demasiadas peticiones: espera un momento.',
          validation_failed: 'Revisa lo que escribiste.', default: 'Algo salió mal. Inténtalo de nuevo.',
        },
      }
    : {
        title: 'Jukebox Jam', noCode: 'Open a Jam\'s link or QR code to join it.', ended: 'This Jam has ended.',
        gone: 'This Jam does not exist or has ended.', hostedBy: 'by', join: 'Join', yourName: 'Your name',
        joinText: 'Give yourself a name to add songs and vote.', joinAccount: 'Sign in with your account to join this Jam.',
        signIn: 'Sign in', nothing: 'Nothing is playing now.', hostSong: 'From the host', paused: 'Paused',
        queue: 'Queue', add: 'Add', invite: 'Invite', upNext: 'Up next', empty: 'The queue is empty: add something!',
        played: 'Played', pinned: 'plays next', search: 'Search songs, artists or albums', artists: 'Artists', albums: 'Albums',
        recent: 'Recent', songs: 'Songs', addBtn: 'Add', added: 'Queued', playNow: 'Play now',
        addAll: 'Add all', back: 'Back', more: 'Show more', vote: 'Vote', votes: (n) => `${n} ${n === 1 ? 'vote' : 'votes'}`,
        addedBy: 'added by', dj: 'Ultimate AI DJ', link: 'Link to join', copy: 'Copy', copied: 'Copied',
        share: 'Share', skip: 'Skip', pause: 'Pause', resume: 'Resume', remove: 'Remove', pin: 'Play next',
        addsLeft: (n) => `You can add ${n} more now`, waitVote: (s) => `You can vote again in ${s} s`,
        waitReplace: (s) => `You can change the song playing in ${s} s`, you: 'You',
        errors: {
          jam_ended: 'This Jam has ended.', jam_full: 'This Jam takes no more guests.',
          jam_guest_limit: 'You added several songs: wait a little to add more.',
          jam_guest_removed: 'The host took you out of this Jam.', jam_guests_disabled: 'This Jam takes no guests without an account.',
          jam_track_not_allowed: 'That song cannot be added to this Jam.', jam_vote_too_soon: 'Wait a little to vote again.',
          jam_voting_off: 'This Jam has no voting.', jam_replace_off: 'The song playing cannot be changed in this Jam.',
          jam_replace_too_soon: 'Someone just changed the song playing: wait a little.', not_found: 'Not found.',
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
      method: options.method || 'GET', headers, credentials: 'same-origin', body: options.body ? JSON.stringify(options.body) : undefined,
    });
    const body = response.status === 204 ? null : await response.json().catch(() => null);
    if (!response.ok) throw new ApiError(body?.code || (response.status === 429 ? 'rate_limited' : response.status === 404 ? 'not_found' : 'default'), response.status, body);
    return body;
  }
  function explain(error) {
    if (error.code === 'jam_vote_too_soon' && error.body?.retryAfterSeconds) return T.waitVote(error.body.retryAfterSeconds);
    if (error.code === 'jam_replace_too_soon' && error.body?.retryAfterSeconds) return T.waitReplace(error.body.retryAfterSeconds);
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
        reread = setTimeout(load, 250);
      }
    };
    socket.onclose = () => setTimeout(live, 5000);
  }
  setInterval(() => { if (document.visibilityState === 'visible') load(); }, 30_000);
  document.addEventListener('visibilitychange', () => { if (document.visibilityState === 'visible') load(); });
  // The song's position moves on between updates.
  setInterval(() => { const bar = document.querySelector('.progress > div'); if (bar) bar.style.width = `${progress()}%`; }, 1000);

  // —— Views ——
  function render() {
    const app = document.getElementById('app');
    app.replaceChildren(...view().filter(Boolean));
  }

  function view() {
    if (!code) return [h('p', { class: 'center muted' }, T.noCode)];
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
    return [head, nowPlaying(jam), controls(jam), tabs, body];
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

  function joinForm(jam) {
    // Signed in: a member with one click. Without an account: a guest by name, when the Jam takes guests.
    if (state.token)
      return h('div', { style: 'margin-top:16px' }, h('button', { class: 'btn primary', onclick: () => act(() => api('/join', { method: 'POST', body: {} })) }, T.join));
    if (!jam.allowGuests)
      return h('div', { class: 'card', style: 'margin-top:16px' }, h('p', {}, T.joinAccount),
        h('p', { style: 'margin-top:12px' }, h('a', { class: 'btn primary', href: '/admin/' }, T.signIn)));
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
      actions.push(h('button', { class: 'btn small', title: T.vote, onclick: () => act(() => api('/votes', { method: 'POST', body: { entryId: entry.id } })) }, `▲ ${entry.votes}`));
    if (waiting && you.canControl) {
      actions.push(h('button', { class: 'btn small', title: T.pin, onclick: () => act(() => api('/control', { method: 'POST', body: { action: 'PIN', entryId: entry.id } })) }, '⤒'));
      actions.push(h('button', { class: 'btn small', title: T.remove, onclick: () => act(() => api('/control', { method: 'POST', body: { action: 'REMOVE', entryId: entry.id } })) }, '✕'));
    }
    return h('li', { class: 'row song' }, art(entry.song.artworkId),
      h('div', { class: 'grow' },
        h('p', { class: 'ellipsis' }, entry.song.title),
        h('p', { class: 'muted ellipsis' }, [entry.song.artists.join(', '),
          entry.addedByYou ? ` · ${T.you}` : entry.addedBy ? ` · ${entry.addedBy}` : entry.addedByDj ? ` · ${T.dj}` : '',
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
