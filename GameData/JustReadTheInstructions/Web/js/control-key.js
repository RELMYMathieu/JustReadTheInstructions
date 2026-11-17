const STORAGE_KEY = 'jrti.controlKey';
const URL_PARAM = 'key';
const PROMPT_TEXT = "This needs the control key from the game's JRTI settings (Web streaming server). Enter it once and this browser keeps it.";

let asking = null;
let declined = false;

function read() {
    try { return localStorage.getItem(STORAGE_KEY); } catch { return null; }
}

function store(key) {
    try { localStorage.setItem(STORAGE_KEY, key); } catch { }
}

function adoptKeyFromUrl() {
    const url = new URL(location.href);
    const key = url.searchParams.get(URL_PARAM);
    if (!key) return;
    store(key.trim().toUpperCase());
    url.searchParams.delete(URL_PARAM);
    history.replaceState(history.state, '', url);
}

export function controlKeyHeaders() {
    const key = read();
    return key ? { Authorization: `Bearer ${key}` } : {};
}

export function askForControlKey() {
    if (declined) return Promise.resolve(false);
    asking ??= Promise.resolve().then(() => {
        const key = prompt(PROMPT_TEXT)?.trim();
        if (key) store(key.toUpperCase());
        else declined = true;
        asking = null;
        return Boolean(key);
    });
    return asking;
}

adoptKeyFromUrl();
