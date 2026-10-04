// Necropolis Casino: two-player server.
// Runs the real game (deck, deals, spells, scores, payouts) and sends each player only what
// they are allowed to see, so the fog of war can't be bypassed from the browser.
// No packages needed. Start it with:   node server.js        (then open http://localhost:8080)
'use strict';

const http = require('http');
const fs = require('fs');
const path = require('path');
const os = require('os');
const crypto = require('crypto');
const { spawn } = require('child_process');

const PORT = Number(process.env.PORT || 8080);
const CLIENT_FILE = path.join(__dirname, 'client.html');
const SEAT_RECLAIM_MS = 60 * 1000; // A seat whose player has been gone this long can be taken by someone new

// --- Game rules (the same as the single-player game) ---
const UNITS = [
    { name: 'Skeleton', spawn: 20, power: 1, count: 20 },
    { name: 'Zombie', spawn: 12, power: 2, count: 12 },
    { name: 'Wight', spawn: 9, power: 3, count: 9 },
    { name: 'Vampire', spawn: 8, power: 4, count: 8 },
    { name: 'Lich', spawn: 6, power: 7, count: 6 },
    { name: 'Dark Knight', spawn: 3, power: 17, count: 3 },
    { name: 'Bone Dragon', spawn: 1, power: 60, count: 1 },
].map(unit => ({ ...unit, pts: unit.spawn * unit.power }));
const PTS = Object.fromEntries(UNITS.map(unit => [unit.name, unit.pts]));

const SLOT_COUNT = 5;
const CARDS_PER_ROUND = 5;   // Each army draws this many cards every round
const TOTAL_ROUNDS = 3;
const PARTNER_RANGE = 10;    // A dealt pair differs by at most this many points when the crypt allows
const WIN_RATE_PERCENT = 20; // A win returns the bet plus bet x 0.20 x (your points / 100)
const FORTUNE_TIERS = [      // Crypt Fortune: after a win, a luck roll may multiply the winnings
    { key: 'mega', multiplier: 50, chance: 1 / 20000 },
    { key: 'big', multiplier: 10, chance: 1 / 1000 },
    { key: 'lucky', multiplier: 2, chance: 1 / 50 },
];

const random = () => crypto.randomInt(0, 2 ** 47) / 2 ** 47; // Uniform in [0, 1) from a secure source

// --- Table state ---
// phase: lobby -> betting -> spells -> reveal -> spells ... -> finished -> betting (play again)
const table = { seats: [null, null], phase: 'lobby', round: 0, deck: [], gameId: 0, result: null };
let nextPileId = 1;

function newSeat(name) {
    return {
        token: crypto.randomUUID(), name, army: [], bet: 0, ready: false, spell: null,
        outbox: [], seq: 0, waiters: new Set(), online: false, awayTimer: null, goneSince: Date.now(),
    };
}

function buildDeck() {
    const deck = [];
    for (const unit of UNITS) for (let i = 0; i < unit.count; i++) deck.push({ name: unit.name, pts: unit.pts });
    for (let i = deck.length - 1; i > 0; i--) {
        const j = crypto.randomInt(i + 1);
        [deck[i], deck[j]] = [deck[j], deck[i]];
    }
    return deck;
}

const strength = army => army.reduce((sum, pile) => sum + (pile && pile.qty > 0 ? PTS[pile.name] * pile.qty : 0), 0);

function freeSlot(army) {
    for (let i = 0; i < SLOT_COUNT; i++) if (!army[i] || army[i].qty === 0) return i;
    return -1;
}
const fits = (army, card) => army.some(pile => pile && pile.qty > 0 && pile.name === card.name) || freeSlot(army) !== -1;

// Puts a card onto its matching pile or into the first free slot
function place(army, card) {
    const index = army.findIndex(pile => pile && pile.qty > 0 && pile.name === card.name);
    if (index !== -1) {
        army[index].qty += 1;
        return { index, joins: true, card };
    }
    const free = freeSlot(army);
    const pile = {
        id: `p${nextPileId++}`, name: card.name, qty: 1, empowered: false, revealed: false,
        rot: random() * 12 - 6, tx: random() * 10 - 5, ty: random() * 10 - 5, // Tabletop scatter
    };
    army[free] = pile;
    return { index: free, joins: false, card, pile: { id: pile.id, name: pile.name, rot: pile.rot, tx: pile.tx, ty: pile.ty } };
}

// The leading side draws random cards until one fits (a card with nowhere to go is discarded)
function drawLead(army) {
    const discards = [];
    while (table.deck.length > 0) {
        const card = table.deck.pop();
        if (fits(army, card)) return { discards, landing: place(army, card) };
        discards.push(card);
    }
    return { discards, landing: null };
}

// The other side gets a random card that fits, worth within PARTNER_RANGE of the leader's card
function drawPartner(army, leadCard) {
    const fitting = [];
    table.deck.forEach((card, i) => { if (fits(army, card)) fitting.push(i); });
    if (fitting.length === 0) return null;
    const gap = i => Math.abs(table.deck[i].pts - leadCard.pts);
    let pool = fitting.filter(i => gap(i) <= PARTNER_RANGE);
    if (pool.length === 0) {
        const closest = Math.min(...fitting.map(gap));
        pool = fitting.filter(i => gap(i) === closest);
    }
    const [card] = table.deck.splice(pool[crypto.randomInt(pool.length)], 1);
    return place(army, card);
}

// Deals one round in matched pairs and returns what happened, for the animations
function dealRound() {
    const pairs = [];
    for (let i = 0; i < CARDS_PER_ROUND; i++) {
        const leader = crypto.randomInt(2);
        const lead = drawLead(table.seats[leader].army);
        if (!lead.landing) {
            if (lead.discards.length) pairs.push({ steps: [{ seat: leader, discards: lead.discards, landing: null }] });
            break; // The crypt is empty
        }
        const steps = [{ seat: leader, discards: lead.discards, landing: lead.landing }];
        const partner = drawPartner(table.seats[1 - leader].army, lead.landing.card);
        if (partner) steps.push({ seat: 1 - leader, discards: [], landing: partner });
        pairs.push({ steps });
    }
    return pairs;
}

function rollFortune() {
    let roll = random();
    for (const tier of FORTUNE_TIERS) {
        if (roll < tier.chance) return tier;
        roll -= tier.chance;
    }
    return null;
}

// --- What each player may see ---
function pileView(pile, isOwner) {
    if (!pile || pile.qty <= 0) return null;
    const view = { id: pile.id, qty: pile.qty, rot: pile.rot, tx: pile.tx, ty: pile.ty };
    // The owner always knows the card; the opponent only once it has been revealed at a round's end
    if (isOwner || pile.revealed) return { ...view, name: pile.name, empowered: pile.empowered, known: true };
    return { ...view, known: false };
}

function stateFor(i) {
    const me = table.seats[i];
    const opp = table.seats[1 - i];
    return {
        type: 'state',
        gameId: table.gameId,
        phase: table.phase,
        round: table.round,
        deckCount: table.deck.length,
        fog: !(table.phase === 'reveal' || table.phase === 'finished'),
        you: { seat: i, name: me.name, bet: me.bet, ready: me.ready, spell: me.spell, army: armyView(me.army, true) },
        opp: opp ? {
            name: opp.name, connected: opp.online, betPlaced: opp.bet > 0,
            ready: opp.ready, spellChosen: Boolean(opp.spell), army: armyView(opp.army, false),
        } : null,
        result: table.phase === 'finished' && table.result ? table.result[i] : null,
    };
}
const armyView = (army, isOwner) => Array.from({ length: SLOT_COUNT }, (_, i) => pileView(army[i], isOwner));

function dealEventFor(i, pairs) {
    return {
        type: 'deal',
        round: table.round,
        pairs: pairs.map(pair => ({
            steps: pair.steps.map(step => {
                const mine = step.seat === i;
                return {
                    side: mine ? 'you' : 'opp',
                    discards: step.discards.map(card => (mine ? { name: card.name } : {})),
                    landing: step.landing && {
                        index: step.landing.index,
                        joins: step.landing.joins,
                        card: mine ? { name: step.landing.card.name } : null,
                        pile: step.landing.pile && (mine
                            ? { ...step.landing.pile, qty: 1, known: true }
                            : { id: step.landing.pile.id, rot: step.landing.pile.rot, tx: step.landing.pile.tx, ty: step.landing.pile.ty, qty: 1, known: false }),
                    },
                };
            }),
        })),
    };
}

// --- Sending to players (long polling) ---
// Each browser keeps asking "anything new?" and the server answers as soon as something happens.
// Plain requests and answers get through every tunnel and proxy, unlike a stream held open.
const POLL_WAIT_MS = 25 * 1000; // How long a question waits for news before the answer is "nothing new"
const AWAY_MS = 8 * 1000;       // A player whose browser stops asking for this long is shown as away
const OUTBOX_SIZE = 200;        // Recent messages kept per player, to resend after a short drop

function send(i, message) {
    const seat = table.seats[i];
    if (!seat) return;
    seat.outbox.push({ seq: ++seat.seq, message });
    if (seat.outbox.length > OUTBOX_SIZE) seat.outbox.shift();
    for (const waiter of [...seat.waiters]) answerPoll(seat, waiter);
}
function answerPoll(seat, waiter) {
    seat.waiters.delete(waiter);
    clearTimeout(waiter.timer);
    const messages = seat.outbox.filter(entry => entry.seq > waiter.after).map(entry => entry.message);
    reply(waiter.res, 200, { seq: seat.seq, messages });
}
function setOnline(i, seat, online) {
    if (table.seats[i] !== seat || seat.online === online || (!online && seat.waiters.size > 0)) return;
    seat.online = online;
    seat.goneSince = online ? null : Date.now();
    if (table.seats[1 - i]) send(1 - i, stateFor(1 - i)); // The opponent sees you come and go
}
function broadcastState() {
    for (const i of [0, 1]) if (table.seats[i]) send(i, stateFor(i));
}
function notify(i, text) {
    send(i, { type: 'info', text });
}

// --- Game flow ---
function newGame() {
    table.gameId += 1;
    table.phase = 'betting';
    table.round = 0;
    table.deck = buildDeck();
    table.result = null;
    for (const seat of table.seats) Object.assign(seat, { army: [], bet: 0, ready: false, spell: null });
    broadcastState();
}

function startRound(round) {
    table.round = round;
    for (const seat of table.seats) Object.assign(seat, { ready: false, spell: null });
    const pairs = dealRound();
    for (const i of [0, 1]) send(i, dealEventFor(i, pairs));
    table.phase = 'spells';
    broadcastState();
}

// Both spells take effect together: doubles first, then implosions,
// so a card that is doubled and destroyed in the same round is destroyed
function resolveRound() {
    const casts = [0, 1].map(i => table.seats[i].spell && { caster: i, ...table.seats[i].spell }).filter(Boolean);
    const ordered = [...casts.filter(c => c.type === 'double'), ...casts.filter(c => c.type === 'implode')];

    // The end of a round reveals both armies
    for (const seat of table.seats) for (const pile of seat.army) if (pile && pile.qty > 0) pile.revealed = true;
    const before = [0, 1].map(i => armyView(table.seats[i].army, true));

    for (const cast of ordered) {
        if (cast.type === 'double') {
            const pile = table.seats[cast.caster].army[cast.index];
            if (pile && pile.qty > 0) { pile.qty *= 2; pile.empowered = true; }
        } else {
            const pile = table.seats[1 - cast.caster].army[cast.index];
            if (pile && pile.qty > 0) pile.qty = 0;
        }
    }

    for (const i of [0, 1]) {
        send(i, {
            type: 'resolve',
            round: table.round,
            before: { you: before[i], opp: before[1 - i] },
            spells: ordered.map(cast => ({
                caster: cast.caster === i ? 'you' : 'opp',
                type: cast.type,
                side: (cast.type === 'double') === (cast.caster === i) ? 'you' : 'opp',
                index: cast.index,
            })),
        });
    }

    for (const seat of table.seats) Object.assign(seat, { ready: false, spell: null });
    if (table.round < TOTAL_ROUNDS) {
        table.phase = 'reveal';
        broadcastState();
    } else {
        finishGame();
    }
}

function finishGame() {
    const points = table.seats.map(seat => strength(seat.army));
    table.result = [0, 1].map(i => {
        const mine = points[i], theirs = points[1 - i], bet = table.seats[i].bet;
        if (mine > theirs) {
            const winnings = Math.floor(bet * mine * WIN_RATE_PERCENT / 10000);
            const fortune = rollFortune();
            return { outcome: 'win', you: mine, opp: theirs, bet, winnings, fortune: fortune ? fortune.key : null,
                     collected: bet + winnings * (fortune ? fortune.multiplier : 1) };
        }
        if (mine < theirs) return { outcome: 'loss', you: mine, opp: theirs, bet, winnings: 0, fortune: null, collected: 0 };
        return { outcome: 'draw', you: mine, opp: theirs, bet, winnings: 0, fortune: null, collected: bet };
    });
    table.phase = 'finished';
    for (const i of [0, 1]) send(i, { type: 'gameover', gameId: table.gameId, ...table.result[i] });
    broadcastState();
}

// --- Player actions ---
function act(i, msg) {
    const me = table.seats[i];
    const opp = table.seats[1 - i];
    if (!opp) return 'Waiting for a second player to join.';
    const bothReady = () => table.seats.every(seat => seat.ready);

    switch (msg.type) {
        case 'bet': {
            if (table.phase !== 'betting' || me.ready) return 'Bets are closed right now.';
            const amount = Number(msg.amount);
            if (!Number.isInteger(amount) || amount < 1 || amount > 1e12) return 'Bet a whole number of gold, at least 1.';
            me.bet = amount;
            me.ready = true;
            notify(1 - i, `${me.name} placed a bet.`);
            if (bothReady()) startRound(1); else broadcastState();
            return null;
        }
        case 'spell': {
            if (table.phase !== 'spells' || me.ready) return 'You cannot cast a spell right now.';
            const spell = msg.spell;
            if (spell === null) { me.spell = null; broadcastState(); return null; }
            const army = spell && spell.type === 'double' ? me.army : spell && spell.type === 'implode' ? opp.army : null;
            if (!army || !Number.isInteger(spell.index) || spell.index < 0 || spell.index >= SLOT_COUNT) return 'Unknown spell.';
            const target = army[spell.index];
            if (!target || target.qty <= 0) return 'That card is not on the table.';
            me.spell = { type: spell.type, index: spell.index };
            broadcastState();
            return null;
        }
        case 'ready': {
            if (table.phase !== 'spells' || me.ready) return 'You are already ready.';
            me.ready = true;
            if (bothReady()) resolveRound(); else broadcastState();
            return null;
        }
        case 'continue': {
            if (table.phase !== 'reveal' || me.ready) return 'Not yet.';
            me.ready = true;
            if (bothReady()) startRound(table.round + 1); else broadcastState();
            return null;
        }
        case 'again': {
            if (table.phase !== 'finished' || me.ready) return 'Not yet.';
            me.ready = true;
            if (bothReady()) newGame(); else broadcastState();
            return null;
        }
        default:
            return 'Unknown action.';
    }
}

// Seats a player: same token rejoins; otherwise a free seat, or a seat abandoned for a while
function join(name, token) {
    const existing = table.seats.findIndex(seat => seat && seat.token === token);
    if (existing !== -1) return { seat: existing, token };

    let index = table.seats.findIndex(seat => !seat);
    if (index === -1) {
        index = table.seats.findIndex(seat => !seat.online && Date.now() - seat.goneSince > SEAT_RECLAIM_MS);
        if (index === -1) return { error: 'The table is full: two players are already playing.' };
        const other = 1 - index;
        notify(other, `${table.seats[index].name} left the table. ${name} takes the empty seat; a new game begins.`);
    }
    table.seats[index] = newSeat(name);
    if (table.seats[0] && table.seats[1]) newGame();
    else { table.phase = 'lobby'; broadcastState(); }
    return { seat: index, token: table.seats[index].token };
}

// --- HTTP ---
function readJson(req) {
    return new Promise((resolve, reject) => {
        let body = '';
        req.on('data', chunk => {
            body += chunk;
            if (body.length > 10000) { reject(new Error('Request too large')); req.destroy(); }
        });
        req.on('end', () => { try { resolve(JSON.parse(body || '{}')); } catch (e) { reject(e); } });
    });
}
function reply(res, status, data) {
    res.writeHead(status, { 'Content-Type': 'application/json', 'Cache-Control': 'no-store' });
    res.end(JSON.stringify(data));
}
const seatOf = token => table.seats.findIndex(seat => seat && token && seat.token === token);

const server = http.createServer(async (req, res) => {
    const url = new URL(req.url, 'http://localhost');
    try {
        if (req.method === 'GET' && (url.pathname === '/' || url.pathname === '/index.html')) {
            res.writeHead(200, { 'Content-Type': 'text/html; charset=utf-8', 'Cache-Control': 'no-store' });
            fs.createReadStream(CLIENT_FILE).pipe(res);
            return;
        }
        if (req.method === 'GET' && url.pathname === '/poll') {
            const i = seatOf(url.searchParams.get('token'));
            if (i === -1) return reply(res, 401, { error: 'Unknown player. Join the table again.' });
            const seat = table.seats[i];
            const waiter = { res, after: Number(url.searchParams.get('after') ?? NaN), timer: null };
            clearTimeout(seat.awayTimer);
            seat.waiters.add(waiter);
            res.on('close', () => {
                clearTimeout(waiter.timer);
                seat.waiters.delete(waiter);
                if (table.seats[i] === seat && seat.waiters.size === 0) {
                    clearTimeout(seat.awayTimer);
                    seat.awayTimer = setTimeout(() => setOnline(i, seat, false), AWAY_MS);
                }
            });
            setOnline(i, seat, true);
            const oldest = seat.outbox.length ? seat.outbox[0].seq : seat.seq + 1;
            if (!Number.isInteger(waiter.after) || waiter.after < oldest - 1 || waiter.after > seat.seq) {
                // A freshly opened page (or one that missed too much) starts from the table as it is now
                seat.waiters.delete(waiter);
                return reply(res, 200, { seq: seat.seq, messages: [stateFor(i)] });
            }
            if (waiter.after < seat.seq) return answerPoll(seat, waiter);
            waiter.timer = setTimeout(() => answerPoll(seat, waiter), POLL_WAIT_MS);
            return;
        }
        if (req.method === 'POST' && url.pathname === '/join') {
            const body = await readJson(req);
            const name = String(body.name || '').trim().slice(0, 20) || 'Necromancer';
            const result = join(name, body.token);
            return reply(res, result.error ? 409 : 200, result);
        }
        if (req.method === 'POST' && url.pathname === '/action') {
            const body = await readJson(req);
            const i = seatOf(body.token);
            if (i === -1) return reply(res, 401, { error: 'Unknown player. Join the table again.' });
            const error = act(i, body);
            return reply(res, error ? 400 : 200, error ? { error } : { ok: true });
        }
        if (url.pathname === '/favicon.ico') { res.writeHead(204); res.end(); return; }
        reply(res, 404, { error: 'Not found' });
    } catch (e) {
        console.error('Request failed:', e);
        reply(res, 400, { error: 'Bad request' });
    }
});

server.listen(PORT, () => {
    console.log(`Necropolis Casino is running.`);
    console.log(`  On this PC:            http://localhost:${PORT}`);
    for (const nets of Object.values(os.networkInterfaces())) {
        for (const net of nets || []) {
            if (net.family === 'IPv4' && !net.internal) console.log(`  On your Wi-Fi/network: http://${net.address}:${PORT}`);
        }
    }
    console.log('Keep this window open while you play. Press Ctrl+C to stop the server.');
    if (!process.env.NO_BROWSER) openBrowser(`http://localhost:${PORT}`);
});
server.on('error', (e) => {
    if (e.code === 'EADDRINUSE') {
        console.error(`Port ${PORT} is already in use, so the server is probably already running in another window.`);
        console.error(`Use that window and open http://localhost:${PORT}, or close it and start this again.`);
    } else {
        console.error('The server could not start:', e.message);
    }
    process.exitCode = 1;
});

// Opens the game for the host on Windows and macOS (set NO_BROWSER=1 to skip)
function openBrowser(url) {
    const command = process.platform === 'win32' ? ['cmd', ['/c', 'start', '""', url]]
        : process.platform === 'darwin' ? ['open', [url]] : null;
    if (!command) return;
    try {
        const child = spawn(command[0], command[1], { stdio: 'ignore', detached: true, windowsVerbatimArguments: true });
        child.on('error', () => {});
        child.unref();
    } catch (e) { /* The address is printed above, so it can be opened by hand */ }
}
