import { API } from './config.js';

const EVENT_TYPES = ['program', 'layout'];
const listeners = new Map(EVENT_TYPES.map((type) => [type, new Set()]));

let source = null;
let connected = false;

function dispatch(type, data) {
    let payload;
    try {
        payload = JSON.parse(data);
    } catch {
        return;
    }
    for (const listener of listeners.get(type)) listener(payload);
}

function connect() {
    if (source || typeof EventSource === 'undefined') return;
    source = new EventSource(API.events);
    source.addEventListener('open', () => { connected = true; });
    source.addEventListener('error', () => { connected = false; });
    for (const type of EVENT_TYPES) source.addEventListener(type, (e) => dispatch(type, e.data));
}

export function onServerEvent(type, listener) {
    connect();
    listeners.get(type).add(listener);
    return () => listeners.get(type).delete(listener);
}

export function serverEventsConnected() {
    return connected;
}
