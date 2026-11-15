/* Allsubs front end.
 * Data access goes through a "backend" object: `remote` talks to the server API,
 * `demo` keeps the sample-data mode entirely in this browser. */
(() => {
  'use strict';

  // ---------- Reference data ----------
  // Fixed sample rates (units per 1 USD) until live rates are connected.
  // The currency and cycle lists mirror server/Rules.cs.
  const RATES = { USD: 1, EUR: 0.92, GBP: 0.79, GEL: 2.7, JPY: 150, INR: 84, BRL: 5.6, CAD: 1.38, AUD: 1.52, TRY: 34 };
  const CURRENCIES = Object.keys(RATES);
  const CATEGORIES = ['Streaming', 'Music', 'Software', 'Cloud', 'Gaming', 'News', 'Fitness', 'Other'];
  const CYCLES = {
    weekly: { label: 'Weekly', perMonth: 52 / 12 },
    monthly: { label: 'Monthly', perMonth: 1 },
    quarterly: { label: 'Every 3 months', perMonth: 1 / 3 },
    yearly: { label: 'Yearly', perMonth: 1 / 12 },
  };
  const SUGGESTIONS = ['Video streaming', 'Music streaming', 'Cloud storage', 'Design tools', 'Game pass', 'News site', 'Gym membership', 'VPN', 'Password manager'];

  // ---------- Backends ----------
  const api = async (method, path, body) => {
    let res;
    try {
      res = await fetch('/api' + path, {
        method,
        headers: body ? { 'Content-Type': 'application/json' } : {},
        body: body ? JSON.stringify(body) : undefined,
      });
    } catch {
      throw new Error('Cannot reach the server. Check your connection and try again.');
    }
    if (res.status === 204) return null;
    const data = await res.json().catch(() => null);
    if (!res.ok) {
      const fallback = res.status === 429 ? 'Too many attempts. Wait a minute and try again.' : 'Something went wrong. Try again.';
      throw Object.assign(new Error(data?.error ?? fallback), { status: res.status });
    }
    return data;
  };

  const remote = {
    listSubs: () => api('GET', '/subscriptions'),
    createSub: (data) => api('POST', '/subscriptions', data),
    updateSub: (id, data) => api('PUT', '/subscriptions/' + id, data),
    deleteSub: (id) => api('DELETE', '/subscriptions/' + id),
    updateUser: (patch) => api('PATCH', '/me', patch),
    signOut: () => api('POST', '/auth/logout'),
    deleteAccount: () => api('DELETE', '/me'),
  };

  // Sample-data mode: same interface, stored in localStorage, never sent to the server.
  const DEMO_KEY = 'allsubs.demo', DEMO_ON = 'allsubs.demo.active';
  const demo = {
    state: null,
    isActive: () => localStorage.getItem(DEMO_ON) === '1',
    open() {
      try { this.state = JSON.parse(localStorage.getItem(DEMO_KEY)); } catch { this.state = null; }
      this.state ??= { user: { email: 'demo@allsubs.example', name: 'Demo user', currency: 'USD', theme: 'system' }, subs: sampleSubs() };
      localStorage.setItem(DEMO_ON, '1');
      this.save();
      return this.state.user;
    },
    save() { localStorage.setItem(DEMO_KEY, JSON.stringify(this.state)); },
    async listSubs() { return this.state.subs.map((s) => ({ ...s })); },
    async createSub(data) { const s = { id: crypto.randomUUID(), ...data }; this.state.subs.push(s); this.save(); return { ...s }; },
    async updateSub(id, data) { const s = Object.assign(this.state.subs.find((x) => x.id === id), data); this.save(); return { ...s }; },
    async deleteSub(id) { this.state.subs = this.state.subs.filter((x) => x.id !== id); this.save(); },
    async updateUser(patch) { Object.assign(this.state.user, patch); this.save(); return { ...this.state.user }; },
    async signOut() { localStorage.removeItem(DEMO_ON); },
    async deleteAccount() { localStorage.removeItem(DEMO_ON); localStorage.removeItem(DEMO_KEY); },
  };

  // ---------- Date helpers (local dates as YYYY-MM-DD) ----------
  const iso = (d) => `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
  const parse = (s) => { const [y, m, d] = s.split('-').map(Number); return new Date(y, m - 1, d); };
  const today = () => { const d = new Date(); return new Date(d.getFullYear(), d.getMonth(), d.getDate()); };
  const addDays = (d, n) => new Date(d.getFullYear(), d.getMonth(), d.getDate() + n);
  const addMonths = (d, n) => {
    const last = new Date(d.getFullYear(), d.getMonth() + n + 1, 0).getDate();
    return new Date(d.getFullYear(), d.getMonth() + n, Math.min(d.getDate(), last));
  };
  /** k-th renewal counted from the anchor date (k may be negative). */
  const occurrence = (sub, k) => {
    const a = parse(sub.next);
    if (sub.cycle === 'weekly') return addDays(a, 7 * k);
    return addMonths(a, k * { monthly: 1, quarterly: 3, yearly: 12 }[sub.cycle]);
  };
  /** All renewal dates of `sub` within [from, to]. */
  const occurrencesBetween = (sub, from, to) => {
    const stepDays = { weekly: 7, monthly: 31, quarterly: 92, yearly: 366 }[sub.cycle];
    let k = Math.floor((from - parse(sub.next)) / 864e5 / stepDays) - 1;
    const out = [];
    for (let d = occurrence(sub, k); d <= to; d = occurrence(sub, ++k)) if (d >= from) out.push(d);
    return out;
  };
  const nextRenewal = (sub) => occurrencesBetween(sub, today(), addDays(today(), 370))[0];
  const daysUntil = (d) => Math.round((d - today()) / 864e5);

  // ---------- Money ----------
  const convert = (amount, from, to) => amount / RATES[from] * RATES[to];
  const money = (amount, currency) =>
    new Intl.NumberFormat(undefined, { style: 'currency', currency, maximumFractionDigits: amount >= 1000 ? 0 : 2 }).format(amount);
  const monthlyIn = (sub, currency) => convert(sub.amount, sub.currency, currency) * CYCLES[sub.cycle].perMonth;

  // ---------- DOM helpers ----------
  const $ = (sel, root = document) => root.querySelector(sel);
  const $$ = (sel, root = document) => [...root.querySelectorAll(sel)];
  /** el('div.cls', {attrs}, ...children) — children are text or nodes, never HTML. */
  const el = (spec, attrs = {}, ...children) => {
    const [tag, ...classes] = spec.split('.');
    const node = document.createElement(tag || 'div');
    if (classes.length) node.className = classes.join(' ');
    for (const [k, v] of Object.entries(attrs)) {
      if (k.startsWith('on')) node.addEventListener(k.slice(2), v);
      else if (v !== false && v != null) node.setAttribute(k, v);
    }
    node.append(...children.filter((c) => c != null));
    return node;
  };
  const hue = (s) => [...s].reduce((h, c) => (h * 31 + c.charCodeAt(0)) % 360, 7);
  const avatar = (name) => el('span.avatar', { style: `--h:${hue(name)}`, 'aria-hidden': 'true' }, (name.trim()[0] || '?').toUpperCase());
  const fmtDate = (d, opts = { month: 'short', day: 'numeric' }) => d.toLocaleDateString(undefined, opts);
  const options = (select, values, labels = values) =>
    select.replaceChildren(...values.map((v, i) => el('option', { value: v }, labels[i])));

  // ---------- State ----------
  let backend = remote;
  let user = null;
  let subs = [];
  let view = 'overview';
  let authMode = 'signin';
  let editingId = null;
  let calMonth = new Date(today().getFullYear(), today().getMonth(), 1);

  // ---------- Toast (errors outside of forms) ----------
  let toastTimer;
  const toast = (msg) => {
    const t = $('#toast');
    t.textContent = msg; t.hidden = false;
    clearTimeout(toastTimer);
    toastTimer = setTimeout(() => { t.hidden = true; }, 5000);
  };
  /** Runs a backend action; an expired session returns to sign-in, other failures show a toast. */
  const run = async (action) => {
    try { await action(); }
    catch (e) {
      if (e.status === 401) { user = null; subs = []; boot(); toast('Your session has ended. Sign in again.'); }
      else toast(e.message);
    }
  };

  // ---------- Theme ----------
  const applyTheme = () => {
    const pref = user?.theme ?? 'system';
    const dark = pref === 'dark' || (pref === 'system' && matchMedia('(prefers-color-scheme: dark)').matches);
    document.documentElement.dataset.theme = dark ? 'dark' : 'light';
  };
  matchMedia('(prefers-color-scheme: dark)').addEventListener('change', applyTheme);

  // ---------- Auth ----------
  const setAuthMode = (mode) => {
    authMode = mode;
    $$('[data-auth-tab]').forEach((t) => {
      const on = t.dataset.authTab === mode;
      t.classList.toggle('is-active', on);
      t.setAttribute('aria-selected', on);
    });
    $('#name-field').hidden = mode !== 'signup';
    $('#auth-submit').textContent = mode === 'signup' ? 'Create account' : 'Sign in';
    $('#auth-form [name=password]').autocomplete = mode === 'signup' ? 'new-password' : 'current-password';
    $('#auth-error').hidden = true;
  };

  const authError = (msg) => { const e = $('#auth-error'); e.textContent = msg; e.hidden = false; };

  const enter = async (nextBackend, account) => {
    const list = await nextBackend.listSubs();
    backend = nextBackend;
    subs = list;
    user = account;
    view = 'overview';
    boot();
  };

  $$('[data-auth-tab]').forEach((t) => t.addEventListener('click', () => setAuthMode(t.dataset.authTab)));

  $('#auth-form').addEventListener('submit', async (e) => {
    e.preventDefault();
    const f = e.target;
    const email = f.email.value.trim();
    const password = f.password.value;
    if (!/^\S+@\S+\.\S+$/.test(email)) return authError('Enter a valid email address.');
    if (authMode === 'signup' && password.length < 8) return authError('Password must be at least 8 characters.');

    const button = $('#auth-submit');
    button.disabled = true;
    try {
      const account = authMode === 'signup'
        ? await api('POST', '/auth/register', { name: f.name.value, email, password })
        : await api('POST', '/auth/login', { email, password });
      await enter(remote, account);
      f.reset();
    } catch (err) {
      authError(err.message);
    } finally {
      button.disabled = false;
    }
  });

  $('#demo-btn').addEventListener('click', () => enter(demo, demo.open()));

  function sampleSubs() {
    const t = today();
    const mk = (name, amount, currency, cycle, category, inDays) =>
      ({ id: crypto.randomUUID(), name, amount, currency, cycle, category, next: iso(addDays(t, inDays)), paused: false });
    return [
      mk('Video streaming', 15.49, 'USD', 'monthly', 'Streaming', 2),
      mk('Music streaming', 10.99, 'EUR', 'monthly', 'Music', 6),
      mk('Cloud storage 2 TB', 99.99, 'USD', 'yearly', 'Cloud', 19),
      mk('Design tools', 22.99, 'USD', 'monthly', 'Software', 11),
      mk('Game pass', 44.99, 'USD', 'quarterly', 'Gaming', 40),
      mk('Daily news', 4, 'GBP', 'weekly', 'News', 3),
      mk('Gym membership', 90, 'GEL', 'monthly', 'Fitness', 14),
      mk('Password manager', 35.88, 'USD', 'yearly', 'Software', 120),
    ];
  }

  // ---------- Navigation ----------
  const TITLES = { overview: 'Overview', subscriptions: 'Subscriptions', calendar: 'Calendar', settings: 'Settings' };
  $$('.nav-item').forEach((b) => b.addEventListener('click', () => { view = b.dataset.view; render(); }));

  // ---------- Render ----------
  const active = () => subs.filter((s) => !s.paused);

  const renderOverview = () => {
    const cur = user.currency;
    const list = active();
    const monthly = list.reduce((sum, s) => sum + monthlyIn(s, cur), 0);
    const upcoming = list.map((s) => ({ s, d: nextRenewal(s) })).filter((x) => x.d).sort((a, b) => a.d - b.d);
    const first = upcoming[0];

    const kpi = (label, value, sub) => el('div.card', {}, el('div.kpi-label', {}, label), el('div.kpi-value', {}, value), el('div.kpi-sub', {}, sub));
    $('#kpis').replaceChildren(
      kpi('Monthly spend', money(monthly, cur), 'averaged across billing cycles'),
      kpi('Yearly spend', money(monthly * 12, cur), 'at current plans'),
      kpi('Active subscriptions', String(list.length), subs.length - list.length ? `${subs.length - list.length} paused` : 'none paused'),
      kpi('Next renewal', first ? fmtDate(first.d) : '—', first ? `${first.s.name} · ${money(first.s.amount, first.s.currency)}` : 'nothing scheduled'),
    );

    const soon = upcoming.filter((x) => daysUntil(x.d) <= 30);
    $('#upcoming').replaceChildren(...(soon.length ? soon.map(({ s, d }) => {
      const n = daysUntil(d);
      return el('li', {},
        avatar(s.name),
        el('div.grow', {}, el('div.name.truncate', {}, s.name), el('div.muted.small', {}, fmtDate(d, { weekday: 'short', month: 'short', day: 'numeric' }))),
        el(`span.pill${n <= 3 ? '.soon' : ''}`, {}, n === 0 ? 'Today' : n === 1 ? 'Tomorrow' : `in ${n} days`),
        el('div.num', {}, money(s.amount, s.currency)),
      );
    }) : [el('li.empty', {}, list.length ? 'Nothing renews in the next 30 days.' : 'Add your first subscription to see renewals here.')]));

    const byCat = {};
    list.forEach((s) => { byCat[s.category] = (byCat[s.category] || 0) + monthlyIn(s, cur); });
    const rows = Object.entries(byCat).sort((a, b) => b[1] - a[1]);
    const max = rows[0]?.[1] || 1;
    $('#by-category').replaceChildren(...(rows.length ? rows.map(([cat, v]) =>
      el('div.bar-row', { title: `${cat}: ${money(v, cur)} per month (${Math.round(v / monthly * 100)}% of total)` },
        el('span', {}, cat), el('span.val', {}, money(v, cur)),
        el('div.track', {}, el('div.fill', { style: `width:${v / max * 100}%` })),
      )) : [el('p.empty', {}, 'No spend to show yet.')]));
  };

  const replaceSub = (updated) => { subs = subs.map((x) => (x.id === updated.id ? updated : x)); render(); };

  const renderSubscriptions = () => {
    const q = $('#search').value.trim().toLowerCase();
    const cat = $('#filter-cat').value;
    const sort = $('#sort').value;
    const cur = user.currency;
    const farFuture = addDays(today(), 99999);

    const rows = subs
      .filter((s) => (!q || s.name.toLowerCase().includes(q)) && (!cat || s.category === cat))
      .map((s) => ({ s, d: s.paused ? null : nextRenewal(s) }))
      .sort((a, b) =>
        sort === 'name' ? a.s.name.localeCompare(b.s.name)
        : sort === 'cost' ? monthlyIn(b.s, cur) - monthlyIn(a.s, cur)
        : (a.d ?? farFuture) - (b.d ?? farFuture));

    $('#sub-rows').replaceChildren(...rows.map(({ s, d }) =>
      el(`tr${s.paused ? '.is-paused' : ''}`, {},
        el('td', {}, el('div.svc', {}, avatar(s.name), s.name)),
        el('td', {}, el('span.pill', {}, s.category)),
        el('td', {}, CYCLES[s.cycle].label),
        el('td', {}, s.paused ? 'Paused' : d ? fmtDate(d, { year: 'numeric', month: 'short', day: 'numeric' }) : '—'),
        el('td.num', {}, money(s.amount, s.currency),
          s.currency !== cur || s.cycle !== 'monthly' ? el('span.price-sub', {}, `≈ ${money(monthlyIn(s, cur), cur)}/mo`) : null),
        el('td', {}, el('div.row-actions', {},
          el('button.icon-btn', { onclick: () => run(async () => { replaceSub(await backend.updateSub(s.id, { ...s, paused: !s.paused })); }) }, s.paused ? 'Resume' : 'Pause'),
          el('button.icon-btn', { onclick: () => openDialog(s) }, 'Edit'),
          el('button.icon-btn.danger', { onclick: () => confirm(`Remove "${s.name}"?`) && run(async () => { await backend.deleteSub(s.id); subs = subs.filter((x) => x !== s); render(); }) }, 'Remove'),
        )),
      )));
    const empty = $('#sub-empty');
    empty.hidden = rows.length > 0;
    empty.textContent = subs.length ? 'No subscriptions match your filters.' : 'No subscriptions yet. Use “Add subscription” to track your first one.';
  };

  const renderCalendar = () => {
    const cur = user.currency;
    const y = calMonth.getFullYear(), m = calMonth.getMonth();
    const first = new Date(y, m, 1), last = new Date(y, m + 1, 0);
    $('#cal-title').textContent = fmtDate(first, { month: 'long', year: 'numeric' });

    const events = {};
    let total = 0;
    active().forEach((s) => occurrencesBetween(s, first, last).forEach((d) => {
      (events[d.getDate()] ||= []).push(s);
      total += convert(s.amount, s.currency, cur);
    }));

    const lead = (first.getDay() + 6) % 7; // Monday-first grid
    const cells = [];
    for (let i = 0; i < lead; i++) cells.push(el('div.cal-cell.is-out'));
    for (let day = 1; day <= last.getDate(); day++) {
      const isToday = iso(new Date(y, m, day)) === iso(today());
      cells.push(el(`div.cal-cell${isToday ? '.is-today' : ''}`, {},
        el('div.cal-day', {}, String(day)),
        ...(events[day] || []).map((s) => el('div.cal-event', { title: `${s.name} — ${money(s.amount, s.currency)}` },
          el('span', {}, s.name), el('span', {}, money(s.amount, s.currency)))),
      ));
    }
    $('#cal-grid').replaceChildren(...cells);
    $('#cal-total').textContent = total ? `Charges this month: ${money(total, cur)}` : 'No charges this month.';
  };

  const renderSettings = () => {
    $('#set-name').value = user.name;
    $('#set-currency').value = user.currency;
    $('#set-theme').value = user.theme;
  };

  const render = () => {
    $('#user-name').textContent = user.name;
    $('#user-email').textContent = user.email;
    $('#user-avatar').replaceWith(Object.assign(avatar(user.name), { id: 'user-avatar' }));
    $('#view-title').textContent = TITLES[view];
    $$('.nav-item').forEach((b) => b.classList.toggle('is-active', b.dataset.view === view));
    $$('.view').forEach((v) => { v.hidden = v.id !== 'view-' + view; });
    ({ overview: renderOverview, subscriptions: renderSubscriptions, calendar: renderCalendar, settings: renderSettings })[view]();
  };

  // ---------- Add / edit dialog ----------
  const dialog = $('#sub-dialog');
  const form = $('#sub-form');

  const openDialog = (sub = null) => {
    editingId = sub?.id ?? null;
    $('#sub-dialog-title').textContent = sub ? 'Edit subscription' : 'Add subscription';
    $('#sub-error').hidden = true;
    form.reset();
    form.currency.value = sub?.currency ?? user.currency;
    form.cycle.value = sub?.cycle ?? 'monthly';
    form.category.value = sub?.category ?? CATEGORIES[0];
    form.next.value = sub ? iso(nextRenewal(sub) ?? parse(sub.next)) : iso(today());
    if (sub) { form.name.value = sub.name; form.amount.value = sub.amount; }
    dialog.showModal();
  };

  $('#add-btn').addEventListener('click', () => openDialog());
  $('#sub-cancel').addEventListener('click', () => dialog.close());

  form.addEventListener('submit', async (e) => {
    e.preventDefault();
    const box = $('#sub-error');
    const fail = (msg) => { box.textContent = msg; box.hidden = false; };
    const name = form.name.value.trim();
    const amount = Number(form.amount.value);
    if (!name) return fail('Enter the service name.');
    if (form.amount.value === '' || !(amount >= 0)) return fail('Enter a price of 0 or more.');
    if (!form.next.value) return fail('Choose the next renewal date.');

    const existing = subs.find((s) => s.id === editingId);
    const data = { name, amount, currency: form.currency.value, cycle: form.cycle.value, category: form.category.value, next: form.next.value, paused: existing?.paused ?? false };
    try {
      if (existing) replaceSub(await backend.updateSub(existing.id, data));
      else { subs.push(await backend.createSub(data)); render(); }
      dialog.close();
    } catch (err) {
      if (err.status === 401) { dialog.close(); run(() => { throw err; }); } else fail(err.message);
    }
  });

  // ---------- Toolbar, calendar and settings events ----------
  ['#search', '#filter-cat', '#sort'].forEach((s) => $(s).addEventListener('input', renderSubscriptions));
  $('#cal-prev').addEventListener('click', () => { calMonth = addMonths(calMonth, -1); renderCalendar(); });
  $('#cal-next').addEventListener('click', () => { calMonth = addMonths(calMonth, 1); renderCalendar(); });
  $('#cal-today').addEventListener('click', () => { calMonth = new Date(today().getFullYear(), today().getMonth(), 1); renderCalendar(); });

  const updateUser = (patch) => run(async () => { user = await backend.updateUser(patch); applyTheme(); render(); });
  $('#set-name').addEventListener('change', (e) => { const name = e.target.value.trim(); if (name) updateUser({ name }); else render(); });
  $('#set-currency').addEventListener('change', (e) => updateUser({ currency: e.target.value }));
  $('#set-theme').addEventListener('change', (e) => updateUser({ theme: e.target.value }));

  const leave = (action) => run(async () => { await action(); user = null; subs = []; boot(); });
  $('#signout-btn').addEventListener('click', () => leave(() => backend.signOut()));
  $('#delete-account-btn').addEventListener('click', () => {
    const what = backend === demo ? 'Delete the sample data from this browser?' : 'Delete your account and all of its subscriptions? This cannot be undone.';
    if (confirm(what)) leave(() => backend.deleteAccount());
  });

  // ---------- Boot ----------
  const boot = () => {
    $('#auth').hidden = !!user;
    $('#app').hidden = !user;
    applyTheme();
    if (user) render(); else setAuthMode('signin');
  };

  options($('#set-currency'), CURRENCIES);
  options(form.currency, CURRENCIES);
  options(form.category, CATEGORIES);
  options($('#filter-cat'), ['', ...CATEGORIES], ['All categories', ...CATEGORIES]);
  $('#service-suggestions').replaceChildren(...SUGGESTIONS.map((s) => el('option', { value: s })));
  $('#cal-dow').replaceChildren(...['Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat', 'Sun'].map((d) => el('div', {}, d)));

  // Resume the sample-data mode or an existing server session, otherwise show sign-in.
  (async () => {
    try {
      if (demo.isActive()) await enter(demo, demo.open());
      else await enter(remote, await api('GET', '/me'));
    } catch {
      boot();
    }
  })();
})();
