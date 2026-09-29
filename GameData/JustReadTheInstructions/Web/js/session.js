import { fetchSession } from './api.js';

const EMPTY_SESSION = Object.freeze({ launchId: null, version: null, inGameRecording: false, codecs: [], lanUrls: [] });

let pending = null;

export function getSession() {
    pending ??= fetchSession()
        .then((session) => ({ ...EMPTY_SESSION, ...session }))
        .catch(() => {
            pending = null;
            return EMPTY_SESSION;
        });
    return pending;
}

export function lanUrl(session, path) {
    const base = session.lanUrls[0];
    return base ? new URL(path, base).href : null;
}
