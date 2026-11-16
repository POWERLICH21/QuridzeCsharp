/* Subfuse front end.
 * Data access goes through a "backend" object: `remote` talks to the server API,
 * `demo` keeps the sample-data mode entirely in this browser. */
(() => {
  'use strict';

  // ---------- Reference data ----------
  // Fixed sample rates (units per 1 USD) until live rates are connected.
  // The currency and cycle lists mirror server/Rules.cs.
  const RATES = { USD: 1, EUR: 0.92, GBP: 0.79, GEL: 2.7, JPY: 150, INR: 84, BRL: 5.6, CAD: 1.38, AUD: 1.52, TRY: 34 };
  const CURRENCIES = Object.keys(RATES);
  const CATEGORIES = ['AI', 'Streaming', 'Music', 'Software', 'Cloud', 'Gaming', 'News', 'Fitness', 'Other'];
  const CYCLES = {
    weekly: { label: 'Weekly', perMonth: 52 / 12 },
    monthly: { label: 'Monthly', perMonth: 1 },
    quarterly: { label: 'Every 3 months', perMonth: 1 / 3 },
    yearly: { label: 'Yearly', perMonth: 1 / 12 },
  };
  // Known services with typical prices, from catalog.json: plan name (lower case) -> plan details.
  const catalog = new Map();

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
      throw Object.assign(new Error(data?.error ?? fallback), { status: res.status, twoFactorRequired: !!data?.twoFactorRequired });
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
    deleteAccount: (password) => api('POST', '/me/delete', { password }),
    changePassword: (currentPassword, newPassword) => api('POST', '/me/password', { currentPassword, newPassword }),
    twoFactorSetup: () => api('POST', '/me/2fa/setup'),
    twoFactorEnable: (code) => api('POST', '/me/2fa/enable', { code }),
    twoFactorDisable: (password, code) => api('POST', '/me/2fa/disable', { password, code }),
    listSuggestions: () => api('GET', '/suggestions'),
    dismissSuggestion: (id) => api('DELETE', '/suggestions/' + id),
    importText: (text) => api('POST', '/import/text', { text }),
  };

  // Sample-data mode: same interface, stored in localStorage, never sent to the server.
  const DEMO_KEY = 'subfuse.demo', DEMO_ON = 'subfuse.demo.active';
  const demo = {
    state: null,
    isActive: () => localStorage.getItem(DEMO_ON) === '1',
    open() {
      try { this.state = JSON.parse(localStorage.getItem(DEMO_KEY)); } catch { this.state = null; }
      this.state ??= { user: { email: 'demo@subfuse.example', name: 'Demo user', currency: 'USD', theme: 'dark' }, subs: sampleSubs() };
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
    async listSuggestions() { return []; },
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
      else if (k === 'style') node.style.cssText = v; // via the DOM, so the content security policy allows it
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
  let suggestions = [];
  let reviewingId = null; // the suggestion currently open in the add form
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
    const pref = user?.theme ?? 'dark'; // Midnight is the default look
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
    $('#code-field').hidden = true;
    $('#auth-form [name=code]').value = '';
  };

  const authError = (msg) => { const e = $('#auth-error'); e.textContent = msg; e.hidden = false; };

  const enter = async (nextBackend, account) => {
    const [list, found] = await Promise.all([nextBackend.listSubs(), nextBackend.listSuggestions()]);
    backend = nextBackend;
    subs = list;
    suggestions = found;
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
        : await api('POST', '/auth/login', { email, password, code: f.code.value.trim() || undefined });
      await enter(remote, account);
      f.reset();
      setAuthMode('signin');
    } catch (err) {
      authError(err.message);
      if (err.twoFactorRequired) { $('#code-field').hidden = false; f.code.select(); }
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

    const kpi = (label, value, sub, hero) => el(hero ? 'div.card.is-hero' : 'div.card', {}, el('div.kpi-label', {}, label), el('div.kpi-value', {}, value), el('div.kpi-sub', {}, sub));
    $('#kpis').replaceChildren(
      kpi('Monthly spend', money(monthly, cur), 'averaged across billing cycles', true),
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

  const renderSuggestions = () => {
    $('#suggestions-card').hidden = suggestions.length === 0;
    $('#import-btn').hidden = backend === demo;
    $('#suggestions').replaceChildren(...suggestions.map((s) => {
      const dismiss = el('button.icon-btn', { onclick: () => run(async () => {
        await backend.dismissSuggestion(s.id);
        suggestions = suggestions.filter((x) => x !== s);
        renderSuggestions();
      }) }, 'Dismiss');
      if (s.source === 'notice') {
        return el('li', {},
          el('div.grow', {}, el('div.name', {}, s.name),
            el('div.muted.small', {}, 'Enter this code in Gmail, under Settings → Forwarding: ', el('code', {}, s.note ?? ''))),
          el('div.row-actions', {}, dismiss));
      }
      return el('li', {},
        avatar(s.name),
        el('div.grow', {}, el('div.name.truncate', {}, s.name),
          el('div.muted.small', {}, `${CYCLES[s.cycle]?.label ?? s.cycle} · from ${s.source === 'email' ? 'a forwarded email' : 'pasted text'}`)),
        el('div.num', {}, s.amount == null ? 'price not found' : money(s.amount, s.currency)),
        el('div.row-actions', {}, el('button.icon-btn', { onclick: () => openDialog(null, s) }, 'Review'), dismiss));
    }));
  };

  const renderSubscriptions = () => {
    renderSuggestions();
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
    // Phones show dots in the grid and this list underneath, since names do not fit in the day cells.
    $('#cal-agenda').replaceChildren(...Object.keys(events).map(Number).sort((x, y) => x - y).flatMap((day) =>
      events[day].map((sub) => el('li', {},
        el('span.agenda-day', {}, fmtDate(new Date(y, m, day), { weekday: 'short', day: 'numeric' })),
        el('div.grow.name.truncate', {}, sub.name),
        el('div.num', {}, money(sub.amount, sub.currency))))));
  };

  const renderSettings = () => {
    $('#set-name').value = user.name;
    $('#set-currency').value = user.currency;
    $('#set-theme').value = user.theme;
    // Password, two-factor and export belong to server accounts; the sample-data mode has none.
    const isDemo = backend === demo;
    $('#security-card').hidden = isDemo;
    $('#export-row').hidden = isDemo;
    $('#forward-card').hidden = !user.inboundAddress;
    $('#forward-address').textContent = user.inboundAddress ?? '';
    const on = !!user.twoFactorEnabled;
    const status = $('#tfa-status');
    status.textContent = on ? 'On' : 'Off';
    status.classList.toggle('on', on);
    $('#tfa-btn').textContent = on ? 'Turn off' : 'Turn on';
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

  /** Opens the form empty, for editing `sub`, or pre-filled from a `suggestion` to review. */
  const openDialog = (sub = null, suggestion = null) => {
    editingId = sub?.id ?? null;
    reviewingId = suggestion?.id ?? null;
    $('#sub-dialog-title').textContent = sub ? 'Edit subscription' : suggestion ? 'Review subscription' : 'Add subscription';
    $('#sub-error').hidden = true;
    $('#sub-hint').hidden = true;
    form.reset();
    form.currency.value = sub?.currency ?? user.currency;
    form.cycle.value = sub?.cycle ?? 'monthly';
    form.category.value = sub?.category ?? CATEGORIES[0];
    form.next.value = sub ? iso(nextRenewal(sub) ?? parse(sub.next)) : iso(today());
    if (sub) { form.name.value = sub.name; form.amount.value = sub.amount; }
    if (suggestion) {
      form.name.value = suggestion.name;
      form.amount.value = suggestion.amount ?? '';
      form.currency.value = suggestion.currency;
      form.cycle.value = suggestion.cycle;
      form.category.value = CATEGORIES.includes(suggestion.category) ? suggestion.category : 'Other';
      form.next.value = suggestion.next;
    }
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
      else {
        subs.push(await backend.createSub(data));
        if (reviewingId) {
          // Saved as a real subscription, so the suggestion has done its job.
          const done = reviewingId;
          suggestions = suggestions.filter((x) => x.id !== done);
          backend.dismissSuggestion(done).catch(() => {});
        }
        render();
      }
      dialog.close();
    } catch (err) {
      if (err.status === 401) { dialog.close(); run(() => { throw err; }); } else fail(err.message);
    }
  });

  // Picking a known service fills in its typical price, cycle and category (new subscriptions only).
  form.name.addEventListener('input', () => {
    const plan = catalog.get(form.name.value.trim().toLowerCase());
    if (!plan || editingId) return;
    form.cycle.value = plan.cycle;
    form.category.value = CATEGORIES.includes(plan.category) ? plan.category : 'Other';
    if (plan.amount != null && form.amount.value === '') {
      form.amount.value = plan.amount;
      form.currency.value = plan.currency;
      $('#sub-hint').hidden = false;
    }
  });

  // ---------- Import from pasted text ----------
  const importDialog = $('#import-dialog');
  const importForm = $('#import-form');
  $('#import-btn').addEventListener('click', () => { importForm.reset(); $('#import-error').hidden = true; importDialog.showModal(); });
  $('#import-cancel').addEventListener('click', () => importDialog.close());
  importForm.addEventListener('submit', async (e) => {
    e.preventDefault();
    const box = $('#import-error');
    try {
      const { found, added } = await backend.importText(importForm.text.value);
      if (found === 0) { box.textContent = 'No known services found in that text. You can still add it by hand.'; box.hidden = false; return; }
      suggestions.push(...added);
      importDialog.close();
      renderSuggestions();
      toast(added.length ? `Found ${added.length} to review.` : 'Those are already in your list.');
    } catch (err) {
      if (err.status === 401) { importDialog.close(); run(() => { throw err; }); }
      else { box.textContent = err.message; box.hidden = false; }
    }
  });

  $('#forward-copy').addEventListener('click', async () => {
    try { await navigator.clipboard.writeText(user.inboundAddress); toast('Address copied.'); }
    catch { toast('Could not copy. Select the address and copy it by hand.'); }
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

  // ---------- "Confirm with your password" dialog ----------
  const askDialog = $('#ask-dialog');
  let askSubmit = null;
  /** Shows a small form; `onSubmit(values)` may throw, and its message is shown in the dialog. */
  const ask = ({ title, text, fields, submitLabel, danger, onSubmit }) => {
    $('#ask-title').textContent = title;
    $('#ask-text').textContent = text;
    $('#ask-fields').replaceChildren(...fields.map((f) =>
      el('label.field', {}, el('span', {}, f.label),
        el('input', { name: f.name, type: f.type ?? 'text', autocomplete: f.autocomplete ?? 'off', placeholder: f.placeholder ?? '', maxlength: '200' }))));
    const button = $('#ask-submit');
    button.textContent = submitLabel;
    button.className = danger ? 'btn btn-danger' : 'btn btn-primary';
    $('#ask-error').hidden = true;
    askSubmit = onSubmit;
    askDialog.showModal();
  };
  $('#ask-cancel').addEventListener('click', () => askDialog.close());
  $('#ask-form').addEventListener('submit', async (e) => {
    e.preventDefault();
    const values = Object.fromEntries(new FormData(e.target));
    const button = $('#ask-submit');
    button.disabled = true;
    try {
      await askSubmit(values);
      askDialog.close();
    } catch (err) {
      if (err.status === 401) { askDialog.close(); run(() => { throw err; }); }
      else { const box = $('#ask-error'); box.textContent = err.message; box.hidden = false; }
    } finally {
      button.disabled = false;
    }
  });

  const currentPassword = { name: 'password', label: 'Current password', type: 'password', autocomplete: 'current-password' };

  $('#change-password-btn').addEventListener('click', () => ask({
    title: 'Change password',
    text: 'Your other devices will be signed out.',
    fields: [currentPassword, { name: 'next', label: 'New password', type: 'password', autocomplete: 'new-password', placeholder: 'At least 8 characters' }],
    submitLabel: 'Change password',
    onSubmit: async ({ password, next }) => {
      if (next.length < 8) throw new Error('Password must be at least 8 characters.');
      await backend.changePassword(password, next);
      toast('Password changed.');
    },
  }));

  // ---------- Two-factor sign-in ----------
  const tfaDialog = $('#tfa-dialog');
  const tfaForm = $('#tfa-form');

  $('#tfa-btn').addEventListener('click', () => {
    if (user.twoFactorEnabled) {
      return ask({
        title: 'Turn off two-factor sign-in',
        text: 'Your account will be protected by your password only.',
        fields: [currentPassword, { name: 'code', label: 'Authenticator code', placeholder: '6-digit code or a recovery code', autocomplete: 'one-time-code' }],
        submitLabel: 'Turn off',
        danger: true,
        onSubmit: async ({ password, code }) => {
          await backend.twoFactorDisable(password, code.trim());
          user.twoFactorEnabled = false;
          render();
          toast('Two-factor sign-in is off.');
        },
      });
    }
    run(async () => {
      const { secret, qrSvg } = await backend.twoFactorSetup();
      $('#tfa-qr').src = 'data:image/svg+xml;charset=utf-8,' + encodeURIComponent(qrSvg);
      $('#tfa-secret').textContent = secret.match(/.{1,4}/g).join(' ');
      $('#tfa-step-scan').hidden = false;
      $('#tfa-step-codes').hidden = true;
      $('#tfa-error').hidden = true;
      tfaForm.reset();
      tfaDialog.showModal();
    });
  });

  tfaForm.addEventListener('submit', async (e) => {
    e.preventDefault();
    const box = $('#tfa-error');
    try {
      const { recoveryCodes } = await backend.twoFactorEnable(tfaForm.code.value.replace(/\s/g, ''));
      user.twoFactorEnabled = true;
      render();
      $('#tfa-codes').replaceChildren(...recoveryCodes.map((c) => el('li', {}, c)));
      $('#tfa-step-scan').hidden = true;
      $('#tfa-step-codes').hidden = false;
    } catch (err) {
      if (err.status === 401) { tfaDialog.close(); run(() => { throw err; }); }
      else { box.textContent = err.message; box.hidden = false; }
    }
  });
  $('#tfa-cancel').addEventListener('click', () => tfaDialog.close());
  $('#tfa-done').addEventListener('click', () => tfaDialog.close());
  $('#tfa-copy').addEventListener('click', async () => {
    const text = $$('#tfa-codes li').map((li) => li.textContent).join('\n');
    try { await navigator.clipboard.writeText(text); toast('Recovery codes copied.'); }
    catch { toast('Could not copy. Select the codes and copy them by hand.'); }
  });
  // Leaving the recovery codes on screen after closing would be a leak.
  tfaDialog.addEventListener('close', () => { $('#tfa-codes').replaceChildren(); $('#tfa-qr').removeAttribute('src'); $('#tfa-secret').textContent = ''; });

  // ---------- Leaving ----------
  const leave = () => { user = null; subs = []; suggestions = []; boot(); };
  $('#signout-btn').addEventListener('click', () => run(async () => { await backend.signOut(); leave(); }));
  $('#delete-account-btn').addEventListener('click', () => {
    if (backend === demo) {
      if (confirm('Delete the sample data from this browser?')) run(async () => { await demo.deleteAccount(); leave(); });
      return;
    }
    ask({
      title: 'Delete account',
      text: 'This removes your account and every subscription for good. It cannot be undone.',
      fields: [currentPassword],
      submitLabel: 'Delete my account',
      danger: true,
      onSubmit: async ({ password }) => { await backend.deleteAccount(password); leave(); },
    });
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
  // The catalogue is a convenience: if it fails to load, adding by hand still works.
  fetch('catalog.json').then((r) => r.json()).then((services) => {
    for (const service of services) for (const plan of service.plans) catalog.set(plan.name.toLowerCase(), { ...plan, category: service.category });
    $('#service-suggestions').replaceChildren(...services.flatMap((s) => s.plans.map((p) => el('option', { value: p.name }))));
  }).catch(() => {});
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
