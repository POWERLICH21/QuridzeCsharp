// BauBico storefront: a small hash-routed single page app talking to the /api backend.
(() => {
  const $ = (s, root = document) => root.querySelector(s);
  const view = $('#view');
  const esc = s => String(s ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
  const money = c => '$' + (c / 100).toFixed(2);
  const load = (k, d) => { try { return JSON.parse(localStorage.getItem(k)) ?? d; } catch { return d; } };
  const save = (k, v) => localStorage.setItem(k, JSON.stringify(v));

  const state = {
    user: null,
    config: { freeShippingFromCents: 7500, shippingCents: 695 },
    cats: { categories: [], collections: [] },
    cart: load('baubico_cart', []),
    wishlist: load('baubico_wishlist', []),
  };

  async function api(path, options = {}) {
    const res = await fetch('/api' + path, {
      method: options.method || 'GET',
      headers: options.body ? { 'Content-Type': 'application/json' } : undefined,
      body: options.body ? JSON.stringify(options.body) : undefined,
      credentials: 'same-origin',
    });
    const text = await res.text();
    let data = null;
    try { data = text ? JSON.parse(text) : null; } catch { /* non-JSON error body */ }
    if (!res.ok) {
      const fallback = res.status === 401 ? 'Please sign in first.' : res.status === 429 ? 'Too many attempts. Please wait a minute.' : 'Something went wrong.';
      throw new Error(data?.error || fallback);
    }
    return data;
  }

  function toast(msg) {
    const t = $('#toast');
    t.textContent = msg; t.hidden = false;
    clearTimeout(toast.timer);
    toast.timer = setTimeout(() => { t.hidden = true; }, 2400);
  }

  // ---------- Product artwork (generated SVG, so the shop needs no image files) ----------
  const SHAPES = {
    tee: 'M100 50 L60 70 L20 120 L55 150 L80 125 L80 310 L220 310 L220 125 L245 150 L280 120 L240 70 L200 50 Q150 85 100 50 Z',
    long: 'M100 45 L55 70 L15 250 L55 262 L82 150 L82 315 L218 315 L218 150 L245 262 L285 250 L245 70 L200 45 Q150 80 100 45 Z',
    cloak: 'M105 40 L60 65 L10 240 L50 255 L72 170 L60 340 L240 340 L228 170 L250 255 L290 240 L240 65 L195 40 L150 95 Z',
    shorts: 'M70 90 L230 90 L255 270 L165 270 L150 160 L135 270 L45 270 Z',
    pants: 'M85 30 L215 30 L235 335 L165 335 L150 110 L135 335 L65 335 Z',
  };
  const KIND = { Hoodies: 'long', Jackets: 'long', Sweaters: 'long', 'Cloak Coats': 'cloak', Shorts: 'shorts', Bottoms: 'pants' };
  const MOTIFS = {
    cloud: '<path d="M-34 10 a16 16 0 0 1 8 -30 a22 22 0 0 1 40 -4 a17 17 0 0 1 18 34 Z"/>',
    wave: '<path d="M-38 6 q10 -22 20 0 t20 0 t20 0 t16 0 v14 h-76 Z"/><path d="M-38 -14 q10 -18 20 0 t20 0 t20 0 t16 0" fill="none" stroke-width="5" stroke="currentColor"/>',
    star: '<path d="M0 -36 L10 -11 L37 -11 L15 6 L23 33 L0 17 L-23 33 L-15 6 L-37 -11 L-10 -11 Z"/>',
    bolt: '<path d="M8 -38 L-24 6 L-4 6 L-12 38 L24 -8 L2 -8 Z"/>',
    leaf: '<path d="M0 -38 C34 -18 30 22 0 38 C-30 22 -34 -18 0 -38 Z"/><path d="M0 -30 V34" stroke="rgba(0,0,0,.35)" stroke-width="3"/>',
    ring: '<circle r="30" fill="none" stroke="currentColor" stroke-width="10"/><circle r="9"/>',
    flame: '<path d="M0 -40 C22 -14 30 2 22 20 C16 34 -16 34 -22 20 C-30 2 -12 -6 -8 -22 C-2 -16 0 -12 0 -40 Z"/>',
    snow: '<g stroke="currentColor" stroke-width="7" stroke-linecap="round"><path d="M0 -34 V34 M-30 -17 L30 17 M-30 17 L30 -17"/></g>',
  };
  let artSeq = 0;
  function art(p) {
    const kind = KIND[p.category] || 'tee';
    const id = 'g' + (++artSeq);
    const spot = kind === 'shorts' ? 'translate(98 215) scale(.55)' : kind === 'pants' ? 'translate(108 240) scale(.5)' : 'translate(150 195) scale(1.15)';
    return `<svg viewBox="0 0 300 360" role="img" aria-label="${esc(p.name)}">
      <defs><linearGradient id="${id}" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="${esc(p.colorA)}"/><stop offset=".72" stop-color="${esc(p.colorA)}"/><stop offset="1" stop-color="${esc(p.colorB)}"/></linearGradient></defs>
      <path d="${SHAPES[kind]}" fill="url(#${id})" stroke="rgba(20,20,20,.55)" stroke-width="3" stroke-linejoin="round"/>
      <g transform="${spot}" fill="${esc(p.colorB)}" color="${esc(p.colorB)}">${MOTIFS[p.motif] || MOTIFS.cloud}</g>
    </svg>`;
  }

  function price(p) {
    return p.compareAtCents
      ? `<span class="price sale">${money(p.priceCents)}<s>${money(p.compareAtCents)}</s></span>`
      : `<span class="price">${money(p.priceCents)}</span>`;
  }

  function card(p) {
    const wished = state.wishlist.includes(p.id);
    return `<article class="card">
      <div class="badges">${p.compareAtCents ? '<span class="badge sale">Sale</span>' : ''}${p.isNew ? '<span class="badge new">New</span>' : ''}</div>
      <button class="heart ${wished ? 'on' : ''}" data-act="wish" data-id="${p.id}" aria-label="Toggle wishlist">♥</button>
      <a href="#/product/${esc(p.slug)}"><div class="art">${art(p)}</div></a>
      <div class="card-body">
        <span class="card-cat">${esc(p.category)}</span>
        <h3><a href="#/product/${esc(p.slug)}">${esc(p.name)}</a></h3>
        ${price(p)}
      </div>
    </article>`;
  }
  const grid = items => items.length ? `<div class="grid">${items.map(card).join('')}</div>` : '<p class="muted">No products found.</p>';
  const stars = n => `<span class="stars" aria-label="${n} out of 5">${'★'.repeat(n)}${'☆'.repeat(5 - n)}</span>`;
  const qs = o => { const p = new URLSearchParams(); Object.entries(o).forEach(([k, v]) => { if (v) p.set(k, v); }); const s = p.toString(); return s ? '?' + s : ''; };

  // ---------- Cart ----------
  const cartCount = () => state.cart.reduce((n, i) => n + i.qty, 0);
  const subtotal = () => state.cart.reduce((n, i) => n + i.qty * i.priceCents, 0);
  const shippingFor = sub => (sub === 0 || sub >= state.config.freeShippingFromCents ? 0 : state.config.shippingCents);

  function cartChanged() {
    save('baubico_cart', state.cart);
    $('#cart-count').textContent = cartCount();
    if (!$('#drawer').hidden) renderDrawer();
    if (route().path === '/cart') render();
  }
  function addToCart(p, size, qty) {
    const line = state.cart.find(i => i.productId === p.id && i.size === size);
    if (line) line.qty = Math.min(20, line.qty + qty);
    else state.cart.push({ productId: p.id, slug: p.slug, name: p.name, size, qty, priceCents: p.priceCents, category: p.category, colorA: p.colorA, colorB: p.colorB, motif: p.motif });
    cartChanged();
  }
  function cartLines() {
    return state.cart.map((i, n) => `<div class="line">
      <div class="art">${art(i)}</div>
      <div><a href="#/product/${esc(i.slug)}"><b>${esc(i.name)}</b></a><small>Size ${esc(i.size)} · ${money(i.priceCents)}</small>
        <div class="qty"><button data-act="qty" data-n="${n}" data-d="-1" aria-label="Decrease">−</button><span>${i.qty}</span><button data-act="qty" data-n="${n}" data-d="1" aria-label="Increase">+</button></div>
        <button class="link" data-act="remove" data-n="${n}">Remove</button></div>
      <b>${money(i.qty * i.priceCents)}</b>
    </div>`).join('');
  }
  function totals() {
    const sub = subtotal(), ship = shippingFor(sub);
    return `<div class="totals"><div><span>Subtotal</span><span>${money(sub)}</span></div>
      <div><span>Shipping</span><span>${ship ? money(ship) : 'Free'}</span></div>
      <div class="grand"><span>Total</span><span>${money(sub + ship)}</span></div></div>`;
  }
  function renderDrawer() {
    $('#drawer').innerHTML = `<div class="drawer-head"><h2>Your cart</h2><button class="link" data-act="close-cart">Close ✕</button></div>
      ${state.cart.length ? cartLines() + totals() + '<a class="btn alt" href="#/checkout" data-act="close-cart">Checkout</a> <a class="btn ghost" href="#/cart" data-act="close-cart" style="margin-top:8px">View cart</a>'
        : '<p class="muted">Your cart is empty.</p>'}`;
  }
  function openDrawer(open) {
    if (open) renderDrawer();
    $('#drawer').hidden = !open; $('#overlay').hidden = !open;
  }

  // ---------- Views ----------
  const views = {
    async '/'() {
      const [trending, latest] = await Promise.all([api('/products?trending=1&pageSize=8'), api('/products?isNew=1&pageSize=8')]);
      const heroArt = trending.items.slice(0, 3).map(p => `<a class="art" href="#/product/${esc(p.slug)}">${art(p)}</a>`).join('');
      const tiles = state.cats.categories.map(c => {
        const sample = [...trending.items, ...latest.items].find(p => p.category === c.name) || { category: c.name, name: c.name, colorA: '#141414', colorB: '#f0dcd6', motif: 'cloud' };
        return `<a class="tile" href="#/shop?category=${encodeURIComponent(c.name)}"><div class="art">${art(sample)}</div><span>${esc(c.name)}</span></a>`;
      }).join('') + `<a class="tile" href="#/shop?sale=1"><div class="art">${art({ category: 'Shirts', name: 'Sale', colorA: '#b23a2a', colorB: '#f0dcd6', motif: 'star' })}</div><span>Sale</span></a>`;
      return `<section class="hero">
          <div><h1>Wear the <em>story</em>.<br>Not the logo.</h1>
            <p>Original anime-inspired hoodies, cloak coats, jerseys and gym wear. Designed in-house and printed in small batches.</p>
            <a class="btn alt" href="#/shop?trending=1">Shop trending</a><a class="btn ghost" href="#/shop?isNew=1">New arrivals</a></div>
          <div class="hero-art">${heroArt}</div>
        </section>
        <div class="section-head"><h2>Shop by category</h2><a href="#/shop">View all →</a></div>
        <div class="tiles">${tiles}</div>
        <div class="section-head"><h2>Trending now</h2><a href="#/shop?trending=1">See more →</a></div>${grid(trending.items)}
        <div class="section-head"><h2>Latest drops</h2><a href="#/shop?isNew=1">See more →</a></div>${grid(latest.items)}
        <div class="perks">
          <div class="perk"><b>Free shipping</b>On orders over ${money(state.config.freeShippingFromCents)}.</div>
          <div class="perk"><b>30-day returns</b>Wrong size? Send it back.</div>
          <div class="perk"><b>Small batches</b>Printed to order, less waste.</div>
          <div class="perk"><b>Order tracking</b>Follow your parcel any time.</div>
        </div>`;
    },

    async '/shop'(q) {
      const page = parseInt(q.page || '1', 10) || 1;
      const data = await api('/products' + qs({ ...q, page, pageSize: 12 }));
      const link = (label, patch, on) => `<a class="${on ? 'on' : ''}" href="#/shop${qs({ ...q, page: '', ...patch })}">${esc(label)}</a>`;
      const title = q.q ? `Results for “${q.q}”` : q.category || q.collection || (q.sale ? 'Sale' : q.trending ? 'Trending' : q.isNew ? 'New arrivals' : 'All products');
      const pages = Math.ceil(data.total / data.pageSize);
      const sorts = [['', 'Newest'], ['price-asc', 'Price: low to high'], ['price-desc', 'Price: high to low'], ['name', 'Name A–Z']];
      return `<div class="shop">
        <aside class="filters">
          <h4>Category</h4>${link('All', { category: '' }, !q.category)}
          ${state.cats.categories.map(c => link(`${c.name} (${c.count})`, { category: c.name }, q.category === c.name)).join('')}
          <h4>Collection</h4>${link('All', { collection: '' }, !q.collection)}
          ${state.cats.collections.map(c => link(c.name, { collection: c.name }, q.collection === c.name)).join('')}
          <h4>Show</h4>${link('On sale', { sale: q.sale ? '' : '1' }, !!q.sale)}${link('Trending', { trending: q.trending ? '' : '1' }, !!q.trending)}${link('New arrivals', { isNew: q.isNew ? '' : '1' }, !!q.isNew)}
        </aside>
        <section>
          <h1>${esc(title)}</h1>
          <div class="toolbar"><span class="muted">${data.total} product${data.total === 1 ? '' : 's'}</span>
            <label>Sort by <select data-act="sort">${sorts.map(([v, l]) => `<option value="${v}" ${(q.sort || '') === v ? 'selected' : ''}>${l}</option>`).join('')}</select></label></div>
          ${grid(data.items)}
          ${pages > 1 ? `<div class="pager">${Array.from({ length: pages }, (_, i) => `<a class="btn small ${i + 1 === page ? '' : 'ghost'}" href="#/shop${qs({ ...q, page: i + 1 })}">${i + 1}</a>`).join('')}</div>` : ''}
        </section></div>`;
    },

    async '/product'(q, slug) {
      const { product: p, reviews, related } = await api('/products/' + encodeURIComponent(slug));
      views.current = { p, size: null, qty: 1 };
      const avg = reviews.length ? Math.round(reviews.reduce((n, r) => n + r.rating, 0) / reviews.length) : 0;
      return `<div class="product">
          <div class="art">${art(p)}</div>
          <div>
            <span class="card-cat">${esc(p.category)} · ${esc(p.collection)}</span>
            <h1>${esc(p.name)}</h1>
            ${reviews.length ? `<p>${stars(avg)} <span class="muted">${reviews.length} review${reviews.length === 1 ? '' : 's'}</span></p>` : ''}
            <p style="font-size:1.5rem">${price(p)}</p>
            <p>${esc(p.description)}</p>
            <h4>Size</h4>
            <div class="sizes">${p.sizes.split(',').map(s => `<button data-act="size" data-size="${esc(s)}">${esc(s)}</button>`).join('')}</div>
            ${p.stock > 0 ? `<div class="buy-row">
              <div class="qty"><button data-act="pqty" data-d="-1" aria-label="Decrease">−</button><span id="pqty">1</span><button data-act="pqty" data-d="1" aria-label="Increase">+</button></div>
              <button class="btn alt" data-act="add">Add to cart</button>
              <button class="btn ghost" data-act="wish" data-id="${p.id}">${state.wishlist.includes(p.id) ? '♥ In wishlist' : '♡ Wishlist'}</button>
            </div><p id="add-msg" class="error"></p>` : '<p class="error">Sold out</p>'}
            <p class="muted">Free shipping over ${money(state.config.freeShippingFromCents)} · 30-day returns</p>
          </div>
        </div>
        <div class="section-head"><h2>Reviews</h2></div>
        <div class="two"><div>${reviews.length ? reviews.map(r => `<div class="review">${stars(r.rating)} <b>${esc(r.author)}</b><p>${esc(r.text)}</p></div>`).join('') : '<p class="muted">No reviews yet. Be the first.</p>'}</div>
          <div class="panel">${state.user ? `<form class="form" data-form="review" data-id="${p.id}"><h3>Write a review</h3>
              <label>Rating <select name="rating">${[5, 4, 3, 2, 1].map(n => `<option value="${n}">${n} star${n === 1 ? '' : 's'}</option>`).join('')}</select></label>
              <label>Your review <textarea class="field" name="text" rows="3" required maxlength="1000"></textarea></label>
              <button class="btn">Post review</button><p class="error" data-msg></p></form>`
            : '<p><a class="link" href="#/account">Sign in</a> to write a review.</p>'}</div></div>
        <div class="section-head"><h2>You may also like</h2></div>${grid(related)}`;
    },

    async '/cart'() {
      if (!state.cart.length) return '<h1>Your cart</h1><p class="muted">Your cart is empty.</p><a class="btn" href="#/shop">Start shopping</a>';
      return `<h1>Your cart</h1><div class="two"><div class="panel">${cartLines()}</div>
        <div class="panel"><h3>Summary</h3>${totals()}<a class="btn alt" href="#/checkout">Checkout</a></div></div>`;
    },

    async '/checkout'() {
      if (!state.cart.length) return '<h1>Checkout</h1><p class="muted">Your cart is empty.</p><a class="btn" href="#/shop">Start shopping</a>';
      const u = state.user || {};
      return `<h1>Checkout</h1><div class="two">
        <form class="form panel" data-form="order"><h3>Shipping details</h3>
          <div class="row"><label>Full name <input class="field" name="name" required value="${esc(u.name)}"></label>
            <label>Email <input class="field" type="email" name="email" required value="${esc(u.email)}"></label></div>
          <label>Address <input class="field" name="address" required></label>
          <div class="row"><label>City <input class="field" name="city" required></label><label>ZIP / postcode <input class="field" name="zip" required></label></div>
          <label>Country <input class="field" name="country" required></label>
          <p class="muted">This is a demo shop: no payment is taken and nothing is shipped.</p>
          <button class="btn alt">Place order</button><p class="error" data-msg></p></form>
        <div class="panel"><h3>Your order</h3>${state.cart.map(i => `<div class="totals"><div><span>${i.qty} × ${esc(i.name)} (${esc(i.size)})</span><span>${money(i.qty * i.priceCents)}</span></div></div>`).join('')}${totals()}</div></div>`;
    },

    async '/track'() {
      return `<h1>Track your order</h1><div class="two"><form class="form panel" data-form="track">
        <label>Order number <input class="field" name="number" placeholder="BB-XXXXXXXXXX" required></label>
        <label>Email used for the order <input class="field" type="email" name="email" required></label>
        <button class="btn">Track</button><p class="error" data-msg></p></form><div id="track-result"></div></div>`;
    },

    async '/wishlist'() {
      if (!state.wishlist.length) return '<h1>Wishlist</h1><p class="muted">Tap the heart on any product to save it here.</p>';
      const data = await api('/products?pageSize=100&ids=' + state.wishlist.join(','));
      return `<h1>Wishlist</h1>${grid(data.items)}`;
    },

    async '/account'() {
      if (!state.user) {
        return `<h1>Account</h1><div class="two">
          <form class="form panel" data-form="login"><h3>Sign in</h3>
            <label>Email <input class="field" type="email" name="email" required autocomplete="email"></label>
            <label>Password <input class="field" type="password" name="password" required autocomplete="current-password"></label>
            <button class="btn">Sign in</button><p class="error" data-msg></p></form>
          <form class="form panel" data-form="register"><h3>Create account</h3>
            <label>Name <input class="field" name="name" required autocomplete="name"></label>
            <label>Email <input class="field" type="email" name="email" required autocomplete="email"></label>
            <label>Password (8+ characters) <input class="field" type="password" name="password" required minlength="8" autocomplete="new-password"></label>
            <button class="btn alt">Create account</button><p class="error" data-msg></p></form></div>`;
      }
      const orders = await api('/orders/mine');
      return `<h1>Hello, ${esc(state.user.name)}</h1>
        <p>${esc(state.user.email)} · <button class="link" data-act="logout">Sign out</button>${state.user.isAdmin ? ' · <a class="link" href="#/admin">Admin panel</a>' : ''}</p>
        <div class="section-head"><h2>Your orders</h2></div>
        ${orders.length ? orders.map(orderCard).join('') : '<p class="muted">You have no orders yet.</p>'}`;
    },

    async '/about'() {
      return `<div class="prose"><h1>About us</h1><p class="script">For the story you wear.</p><p>BauBico makes original, anime-inspired clothing. Every pattern is drawn by our own small team, then printed to order so nothing sits in a warehouse.</p>
        <p>We started with one cloud-print hoodie and now make cloak coats, jerseys, gym wear and seasonal knitwear.</p><a class="btn" href="#/shop">Browse the shop</a></div>`;
    },
    async '/faq'() {
      const faq = [['How long does shipping take?', 'Items are printed to order in 3–5 days, then delivered in 5–10 days.'],
        ['What is your return policy?', 'You can return unworn items within 30 days of delivery.'],
        ['How do I pick a size?', 'Our fit is relaxed. If you are between sizes, choose the smaller one for a closer fit.'],
        ['How do I track my order?', 'Use the Track order page with your order number and the email you ordered with.']];
      return `<div class="prose"><h1>FAQ</h1>${faq.map(([a, b]) => `<details><summary>${a}</summary><p>${b}</p></details>`).join('')}</div>`;
    },
    async '/contact'() {
      return `<h1>Contact us</h1><div class="two"><form class="form panel" data-form="contact">
        <label>Name <input class="field" name="name" required></label><label>Email <input class="field" type="email" name="email" required></label>
        <label>Message <textarea class="field" name="message" rows="5" required maxlength="2000"></textarea></label>
        <button class="btn">Send message</button><p data-msg></p></form>
        <div class="panel"><h3>We usually reply within a day</h3><p class="muted">Questions about sizing, an order, or a design idea? Send us a note.</p></div></div>`;
    },

    async '/admin'(q) {
      if (!state.user?.isAdmin) return '<h1>Admin</h1><p class="error">You need an admin account to see this page.</p><a class="btn" href="#/account">Sign in</a>';
      const tab = q.tab || 'orders';
      const stats = await api('/admin/stats');
      const head = `<h1>Admin panel</h1><div class="stats">
        <div class="stat"><b>${money(stats.revenueCents)}</b>Revenue</div><div class="stat"><b>${stats.orders}</b>Orders</div>
        <div class="stat"><b>${stats.products}</b>Products</div><div class="stat"><b>${stats.customers}</b>Customers</div><div class="stat"><b>${stats.subscribers}</b>Subscribers</div></div>
        <div class="tabs">${['orders', 'products', 'messages'].map(t => `<a class="btn small ${t === tab ? '' : 'ghost'}" href="#/admin?tab=${t}">${t}</a>`).join('')}</div>`;
      if (tab === 'products') {
        const { items } = await api('/products?pageSize=100');
        views.adminProducts = items;
        const editing = items.find(p => String(p.id) === q.edit);
        return head + `<div class="two"><div class="panel"><table><tr><th>Product</th><th>Price</th><th>Stock</th><th></th></tr>
          ${items.map(p => `<tr><td><b>${esc(p.name)}</b><br><span class="muted">${esc(p.category)} · ${esc(p.collection)}</span></td><td>${money(p.priceCents)}</td><td>${p.stock}</td>
            <td><a class="link" href="#/admin?tab=products&edit=${p.id}">Edit</a> · <button class="link" data-act="del-product" data-id="${p.id}">Delete</button></td></tr>`).join('')}</table></div>
          ${productForm(editing)}</div>`;
      }
      if (tab === 'messages') {
        const msgs = await api('/admin/messages');
        return head + `<div class="panel">${msgs.length ? msgs.map(m => `<div class="review"><b>${esc(m.name)}</b> <span class="muted">${esc(m.email)} · ${new Date(m.createdAt).toLocaleString()}</span><p>${esc(m.message)}</p></div>`).join('') : '<p class="muted">No messages yet.</p>'}</div>`;
      }
      const orders = await api('/admin/orders');
      const statuses = ['Placed', 'Processing', 'Shipped', 'Delivered', 'Cancelled'];
      return head + `<div class="panel">${orders.length ? `<table><tr><th>Order</th><th>Customer</th><th>Items</th><th>Total</th><th>Status</th></tr>
        ${orders.map(o => `<tr><td><b>${esc(o.number)}</b><br><span class="muted">${new Date(o.createdAt).toLocaleDateString()}</span></td>
          <td>${esc(o.name)}<br><span class="muted">${esc(o.email)}<br>${esc(o.address)}, ${esc(o.city)} ${esc(o.zip)}, ${esc(o.country)}</span></td>
          <td>${o.items.map(i => `${i.qty} × ${esc(i.name)} (${esc(i.size)})`).join('<br>')}</td><td>${money(o.totalCents)}</td>
          <td><select data-act="status" data-number="${esc(o.number)}">${statuses.map(s => `<option ${s === o.status ? 'selected' : ''}>${s}</option>`).join('')}</select></td></tr>`).join('')}</table>`
        : '<p class="muted">No orders yet.</p>'}</div>`;
    },
  };

  function productForm(p) {
    const v = p || { name: '', description: '', category: '', collection: '', priceCents: 0, compareAtCents: null, sizes: 'S,M,L,XL', colorA: '#141414', colorB: '#f0dcd6', motif: 'cloud', isNew: true, isTrending: false, stock: 20 };
    return `<form class="form panel" data-form="product" data-id="${p ? p.id : ''}"><h3>${p ? 'Edit product' : 'Add product'}</h3>
      <label>Name <input class="field" name="name" required value="${esc(v.name)}"></label>
      <div class="row"><label>Category <input class="field" name="category" required value="${esc(v.category)}"></label><label>Collection <input class="field" name="collection" value="${esc(v.collection)}"></label></div>
      <div class="row"><label>Price ($) <input class="field" name="price" type="number" step="0.01" min="0.01" required value="${v.priceCents ? (v.priceCents / 100).toFixed(2) : ''}"></label>
        <label>Old price ($, optional) <input class="field" name="compare" type="number" step="0.01" min="0" value="${v.compareAtCents ? (v.compareAtCents / 100).toFixed(2) : ''}"></label></div>
      <div class="row"><label>Sizes <input class="field" name="sizes" required value="${esc(v.sizes)}"></label><label>Stock <input class="field" name="stock" type="number" min="0" required value="${v.stock}"></label></div>
      <div class="row"><label>Main colour <input name="colorA" type="color" value="${esc(v.colorA)}"></label><label>Accent colour <input name="colorB" type="color" value="${esc(v.colorB)}"></label></div>
      <label>Motif <select name="motif">${Object.keys(MOTIFS).map(m => `<option ${m === v.motif ? 'selected' : ''}>${m}</option>`).join('')}</select></label>
      <label>Description <textarea class="field" name="description" rows="3">${esc(v.description)}</textarea></label>
      <label><span><input type="checkbox" name="isNew" ${v.isNew ? 'checked' : ''}> New arrival</span></label>
      <label><span><input type="checkbox" name="isTrending" ${v.isTrending ? 'checked' : ''}> Trending</span></label>
      <button class="btn alt">${p ? 'Save changes' : 'Add product'}</button>${p ? '<a class="link" href="#/admin?tab=products">Cancel</a>' : ''}<p class="error" data-msg></p></form>`;
  }

  function orderCard(o) {
    return `<div class="panel" style="margin-bottom:14px"><p><b>${esc(o.number)}</b> · ${new Date(o.createdAt).toLocaleDateString()} · <span class="status">${esc(o.status)}</span></p>
      ${o.items.map(i => `<div class="totals"><div><span>${i.qty} × ${esc(i.name)} (${esc(i.size)})</span><span>${money(i.qty * i.unitPriceCents)}</span></div></div>`).join('')}
      <div class="totals"><div><span>Shipping</span><span>${o.shippingCents ? money(o.shippingCents) : 'Free'}</span></div><div class="grand"><span>Total</span><span>${money(o.totalCents)}</span></div></div>
      <p class="muted">Ships to ${esc(o.name)}, ${esc(o.address)}, ${esc(o.city)} ${esc(o.zip)}, ${esc(o.country)}</p></div>`;
  }

  // ---------- Router ----------
  function route() {
    const [rawPath, rawQuery = ''] = (location.hash.slice(1) || '/').split('?');
    const parts = rawPath.split('/').filter(Boolean);
    const q = Object.fromEntries(new URLSearchParams(rawQuery));
    return { path: '/' + (parts[0] || ''), arg: parts[1] ? decodeURIComponent(parts[1]) : null, q };
  }
  let renderSeq = 0;
  async function render() {
    const { path, arg, q } = route();
    const seq = ++renderSeq;
    const handler = views[path];
    let html;
    try {
      html = handler ? await handler(q, arg) : '<h1>Page not found</h1><a class="btn" href="#/">Back to the shop</a>';
    } catch (e) {
      html = `<h1>Oops</h1><p class="error">${esc(e.message)}</p><a class="btn" href="#/">Back to the shop</a>`;
    }
    if (seq !== renderSeq) return; // a newer navigation finished first
    view.innerHTML = html;
    document.querySelectorAll('#catnav a').forEach(a => a.classList.toggle('on', path === '/shop' && a.dataset.cat === (q.category || '')));
  }
  function renderChrome() {
    $('#catnav').innerHTML = state.cats.categories.map(c => `<a data-cat="${esc(c.name)}" href="#/shop?category=${encodeURIComponent(c.name)}">${esc(c.name)}</a>`).join('')
      + '<a data-cat="-" href="#/shop?sale=1">Sale</a>';
    $('#account-link').textContent = state.user ? state.user.name.split(' ')[0] : 'Account';
    $('#cart-count').textContent = cartCount();
    $('#ship-note').textContent = `Free shipping on orders over ${money(state.config.freeShippingFromCents)}`;
  }

  // ---------- Events ----------
  document.addEventListener('click', async e => {
    const el = e.target.closest('[data-act]');
    if (!el || el.tagName === 'SELECT') return;
    const act = el.dataset.act, cur = views.current;
    if (act === 'wish') {
      const id = Number(el.dataset.id);
      state.wishlist = state.wishlist.includes(id) ? state.wishlist.filter(x => x !== id) : [...state.wishlist, id];
      save('baubico_wishlist', state.wishlist);
      toast(state.wishlist.includes(id) ? 'Saved to wishlist' : 'Removed from wishlist');
      render();
    } else if (act === 'size') {
      cur.size = el.dataset.size;
      document.querySelectorAll('.sizes button').forEach(b => b.classList.toggle('on', b === el));
      $('#add-msg').textContent = '';
    } else if (act === 'pqty') {
      cur.qty = Math.max(1, Math.min(20, cur.qty + Number(el.dataset.d)));
      $('#pqty').textContent = cur.qty;
    } else if (act === 'add') {
      if (!cur.size) { $('#add-msg').textContent = 'Please choose a size.'; return; }
      addToCart(cur.p, cur.size, cur.qty);
      openDrawer(true);
    } else if (act === 'qty') {
      const line = state.cart[Number(el.dataset.n)];
      line.qty += Number(el.dataset.d);
      if (line.qty < 1) state.cart.splice(Number(el.dataset.n), 1); else line.qty = Math.min(20, line.qty);
      cartChanged();
    } else if (act === 'remove') {
      state.cart.splice(Number(el.dataset.n), 1); cartChanged();
    } else if (act === 'close-cart') {
      openDrawer(false);
    } else if (act === 'logout') {
      await api('/auth/logout', { method: 'POST' });
      state.user = null; renderChrome(); render();
    } else if (act === 'del-product') {
      if (!confirm('Delete this product?')) return;
      try { await api('/admin/products/' + el.dataset.id, { method: 'DELETE' }); toast('Product deleted'); await refreshCats(); render(); }
      catch (err) { toast(err.message); }
    }
  });

  document.addEventListener('change', async e => {
    const el = e.target;
    if (el.dataset.act === 'sort') location.hash = '#/shop' + qs({ ...route().q, sort: el.value, page: '' });
    if (el.dataset.act === 'status') {
      try { await api(`/admin/orders/${encodeURIComponent(el.dataset.number)}/status`, { method: 'PUT', body: { status: el.value } }); toast('Status updated'); }
      catch (err) { toast(err.message); }
    }
  });

  const forms = {
    async login(d) { state.user = await api('/auth/login', { method: 'POST', body: d }); renderChrome(); render(); },
    async register(d) { state.user = await api('/auth/register', { method: 'POST', body: d }); renderChrome(); render(); },
    async review(d, form) { await api(`/products/${form.dataset.id}/reviews`, { method: 'POST', body: { rating: Number(d.rating), text: d.text } }); toast('Thanks for your review!'); render(); },
    async contact(d, form) { await api('/contact', { method: 'POST', body: d }); form.reset(); return 'Thanks! Your message has been sent.'; },
    async track(d) { const o = await api('/orders/track' + qs(d)); $('#track-result').innerHTML = orderCard(o); },
    async order(d) {
      const o = await api('/orders', { method: 'POST', body: { ...d, items: state.cart.map(i => ({ productId: i.productId, size: i.size, qty: i.qty })) } });
      state.cart = []; save('baubico_cart', state.cart); $('#cart-count').textContent = 0;
      view.innerHTML = `<h1>Thank you!</h1><p class="success">Your order ${esc(o.number)} has been placed.</p>
        <p class="muted">Keep the order number: you can follow the order on the <a class="link" href="#/track">Track order</a> page.</p>${orderCard(o)}<a class="btn" href="#/shop">Continue shopping</a>`;
      window.scrollTo(0, 0);
    },
    async product(d, form) {
      const body = { name: d.name, description: d.description, category: d.category, collection: d.collection,
        priceCents: Math.round(Number(d.price) * 100), compareAtCents: d.compare ? Math.round(Number(d.compare) * 100) : null,
        sizes: d.sizes, colorA: d.colorA, colorB: d.colorB, motif: d.motif, isNew: 'isNew' in d, isTrending: 'isTrending' in d, stock: Number(d.stock) };
      const id = form.dataset.id;
      await api('/admin/products' + (id ? '/' + id : ''), { method: id ? 'PUT' : 'POST', body });
      toast(id ? 'Product saved' : 'Product added');
      await refreshCats();
      if (id) location.hash = '#/admin?tab=products'; else render();
    },
  };

  document.addEventListener('submit', async e => {
    const form = e.target;
    e.preventDefault();
    if (form.id === 'search') { location.hash = '#/shop' + qs({ q: form.q.value.trim() }); return; }
    if (form.id === 'newsletter') {
      const msg = $('#newsletter-msg');
      try { await api('/newsletter', { method: 'POST', body: { email: form.email.value } }); msg.textContent = 'You are on the list!'; form.reset(); }
      catch (err) { msg.textContent = err.message; }
      return;
    }
    const handler = forms[form.dataset.form];
    if (!handler) return;
    const msg = $('[data-msg]', form), button = $('button', form);
    if (msg) { msg.textContent = ''; msg.className = 'error'; }
    button.disabled = true;
    try {
      const ok = await handler(Object.fromEntries(new FormData(form)), form);
      if (ok && msg) { msg.className = 'success'; msg.textContent = ok; }
    } catch (err) {
      if (msg) msg.textContent = err.message; else toast(err.message);
    } finally { button.disabled = false; }
  });

  $('#cart-btn').addEventListener('click', () => openDrawer(true));
  $('#overlay').addEventListener('click', () => openDrawer(false));
  window.addEventListener('hashchange', () => { openDrawer(false); window.scrollTo(0, 0); render(); });

  async function refreshCats() { state.cats = await api('/categories'); renderChrome(); }

  (async () => {
    try {
      [state.config, state.user] = await Promise.all([api('/config'), api('/auth/me')]);
      await refreshCats();
    } catch (e) { toast(e.message); }
    renderChrome();
    render();
  })();
})();
