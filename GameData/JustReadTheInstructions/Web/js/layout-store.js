import { LAYOUT_REMOTE_POLL_MS, LAYOUT_SAVE_DELAY_MS, LAYOUT_SAVE_RETRY_MS } from './config.js';
import { fetchLayout, saveLayout, fetchProgram } from './api.js';
import { onServerEvent, serverEventsConnected } from './events.js';

const LOCAL_KEY = 'jrti-layout';
export const LABEL_MODES = Object.freeze(['auto', 'always', 'never']);
export const COLUMN_CHOICES = Object.freeze([null, 1, 2, 3, 4]);

function parseWholeNumber(text) {
    const trimmed = text?.trim() ?? '';
    return /^\d+$/.test(trimmed) ? Number(trimmed) : null;
}

function normalizeTile(tile) {
    if (typeof tile === 'string') return { id: null, name: tile };
    return {
        id: Number.isInteger(tile?.id) ? tile.id : null,
        name: typeof tile?.name === 'string' && tile.name ? tile.name : null,
    };
}

export function normalizeLayout(raw) {
    const tiles = Array.isArray(raw?.tiles) ? raw.tiles.map(normalizeTile) : [];
    return {
        tiles,
        spotlight: Number.isInteger(raw?.spotlight) && raw.spotlight >= 0 && raw.spotlight < tiles.length ? raw.spotlight : null,
        fill: raw?.fill === true,
        labels: LABEL_MODES.includes(raw?.labels) ? raw.labels : 'auto',
        columns: COLUMN_CHOICES.includes(raw?.columns) ? raw.columns : null,
    };
}

function toJson(layout) {
    return { version: 1, ...layout };
}

function layoutQuery(layout) {
    let query = `cams=${layout.tiles.map((tile) => tile.id ?? '').join(',')}`;
    if (layout.spotlight !== null) query += `&spotlight=${layout.spotlight}`;
    if (layout.fill) query += '&fill=1';
    if (layout.labels !== 'auto') query += `&labels=${layout.labels}`;
    if (layout.columns) query += `&cols=${layout.columns}`;
    return query;
}

class LocalLayoutStore {
    kind = 'local';
    title = 'This browser';

    async load() {
        try {
            return normalizeLayout(JSON.parse(localStorage.getItem(LOCAL_KEY)));
        } catch {
            return normalizeLayout(null);
        }
    }

    save(layout) {
        try { localStorage.setItem(LOCAL_KEY, JSON.stringify(toJson(layout))); } catch { }
    }

    shareQuery(layout) {
        return layoutQuery(layout);
    }

    watch() { }

    stop() { }
}

class UrlLayoutStore {
    kind = 'url';
    title = 'From a link';

    constructor(params) {
        this._params = params;
    }

    async load() {
        const cams = this._params.get('cams');
        const tiles = cams ? cams.split(',').map((token) => ({ id: parseWholeNumber(token), name: null })) : [];
        return normalizeLayout({
            tiles,
            spotlight: parseWholeNumber(this._params.get('spotlight')),
            fill: this._params.get('fill') === '1',
            labels: this._params.get('labels'),
            columns: parseWholeNumber(this._params.get('cols')),
        });
    }

    save(layout) {
        history.replaceState(null, '', `?${layoutQuery(layout)}`);
    }

    shareQuery(layout) {
        return layoutQuery(layout);
    }

    watch() { }

    stop() { }
}

class SavedLayoutStore {
    kind = 'saved';

    constructor(name) {
        this.name = name;
        this.title = name;
        this._saveTimer = null;
        this._lastText = null;
        this._savedAt = 0;
        this._pending = null;
        this._timer = null;
        this._unsubscribe = null;
        this.missing = false;
        this.onSaveState = null;
    }

    async load() {
        const text = await fetchLayout(this.name);
        this.missing = text == null;
        this._lastText = text;
        return text == null ? normalizeLayout(null) : normalizeLayout(JSON.parse(text));
    }

    save(layout) {
        const body = toJson(layout);
        this._lastText = JSON.stringify(body);
        this._savedAt = Date.now();
        this._pending = body;
        this.onSaveState?.('saving');
        clearTimeout(this._saveTimer);
        this._saveTimer = setTimeout(() => this._flush(), LAYOUT_SAVE_DELAY_MS);
    }

    async _flush() {
        const body = this._pending;
        try {
            await saveLayout(this.name, body);
            if (this._pending !== body) return;
            this._pending = null;
            this.missing = false;
            this.onSaveState?.('saved');
        } catch {
            if (this._pending !== body) return;
            this.onSaveState?.('retrying');
            this._saveTimer = setTimeout(() => this._flush(), LAYOUT_SAVE_RETRY_MS);
        }
    }

    shareQuery() {
        return `layout=${encodeURIComponent(this.name)}`;
    }

    watch(onRemoteChange, isBusy) {
        const check = async () => {
            const startedAt = Date.now();
            try {
                const text = await fetchLayout(this.name);
                if (text == null || text === this._lastText || this._pending || isBusy() || startedAt < this._savedAt + LAYOUT_SAVE_DELAY_MS * 2) return;
                this._lastText = text;
                onRemoteChange(normalizeLayout(JSON.parse(text)));
            } catch { }
        };
        this._unsubscribe = onServerEvent('layout', (event) => { if (event.name === this.name) check(); });
        this._timer = setInterval(() => { if (!serverEventsConnected()) check(); }, LAYOUT_REMOTE_POLL_MS);
    }

    stop() {
        clearInterval(this._timer);
        this._unsubscribe?.();
    }
}

class ProgramLayoutStore {
    kind = 'program';
    title = 'Clean feed';

    constructor() {
        this.name = null;
        this._lastText = null;
        this._timer = null;
        this._unsubscribers = [];
    }

    async load() {
        const program = await fetchProgram();
        this.name = program.layout ?? null;
        return this._loadLayout();
    }

    async _loadLayout() {
        const text = this.name ? await fetchLayout(this.name) : null;
        this._lastText = text;
        return normalizeLayout(text ? JSON.parse(text) : null);
    }

    save() { }

    shareQuery() {
        return 'program';
    }

    watch(onRemoteChange) {
        const refresh = async (name = this.name) => {
            try {
                const before = [this.name, this._lastText];
                this.name = name;
                const layout = await this._loadLayout();
                if (before[0] !== this.name || before[1] !== this._lastText) onRemoteChange(layout);
            } catch { }
        };
        this._unsubscribers.push(
            onServerEvent('program', (program) => { if ((program.layout ?? null) !== this.name) refresh(program.layout ?? null); }),
            onServerEvent('layout', (event) => { if (event.name === this.name) refresh(); }));
        this._timer = setInterval(async () => {
            if (serverEventsConnected()) return;
            try {
                refresh((await fetchProgram()).layout ?? null);
            } catch { }
        }, LAYOUT_REMOTE_POLL_MS);
    }

    stop() {
        clearInterval(this._timer);
        for (const unsubscribe of this._unsubscribers.splice(0)) unsubscribe();
    }
}

export function savedLayoutStore(name) {
    return new SavedLayoutStore(name);
}

export function storeForLocation(params) {
    if (params.has('program')) return new ProgramLayoutStore();
    if (params.has('layout')) return new SavedLayoutStore(params.get('layout'));
    if (params.has('cams')) return new UrlLayoutStore(params);
    return new LocalLayoutStore();
}

export async function saveLayoutAs(name, layout) {
    await saveLayout(name, toJson(layout));
}
