export const SNAPSHOT_REFRESH_MS = 10000;
export const CAMERA_SYNC_MS = 5000;
export const LOS_DELAY_MS = 3000;
export const VIEWER_LOS_DELAY_MS = 5000;

export const STREAM_RETRY_MIN_MS = 1000;
export const STREAM_RETRY_MAX_MS = 10000;
export const STREAM_STALL_MS = 15000;

export const RECORDINGS_REFRESH_MS = 3000;

export const DEBUG_POLL_MS = 1000;
export const DEBUG_REQUEST_TIMEOUT_MS = 3000;
export const DEBUG_HISTORY_SAMPLES = 120;

export const LAYOUT_SIGNAL_CHECK_MS = 1000;
export const LAYOUT_CHROME_HIDE_MS = 3000;
export const LAYOUT_REMOTE_POLL_MS = 2000;
export const LAYOUT_SAVE_DELAY_MS = 400;
export const LAYOUT_SAVE_RETRY_MS = 3000;

export const LOS_OVERLAY_HTML = '<img src="/images/los.png" alt="Loss of signal">';
export const WAITING_OVERLAY_HTML = '<span class="waiting">Waiting for frames</span>';

export const MICS = Object.freeze([
    { id: 'game', label: 'Game mix', hint: "The game's sound from where this camera is, clean: no air delay, echo or hull muffling." },
    { id: 'external', label: 'External', hint: 'Outside mic: sound crosses the air and arrives late from far away. Silent in vacuum, except its own vessel through the hull.' },
    { id: 'onboard', label: 'Onboard', hint: 'Inside mic: its own vessel through the structure, other vessels muffled by the hull.' },
]);

export const API = Object.freeze({
    session: '/session',
    cameras: '/cameras',
    snapshot: (id) => `/camera/${id}/snapshot?t=${Date.now()}`,
    stream: (id) => `/camera/${id}/stream`,
    audio: (id) => `/camera/${id}/audio`,
    streams: (ids, preview) => `/streams?ids=${ids.join(',')}${preview ? '&preview=1' : ''}`,
    status: (id) => `/camera/${id}/status`,
    settings: (id) => `/camera/${id}/settings`,
    gameRecording: (id, action, codec) => `/camera/${id}/recording/${action}${codec ? `?codec=${codec}` : ''}`,
    viewer: (id) => `/viewer.html?id=${id}`,
    debugStats: '/debug/stats',
    events: '/events',
    program: '/program',
    programTake: (name) => `/program/take/${encodeURIComponent(name)}`,
    programClear: '/program/clear',
    layouts: '/layouts',
    layout: (name) => `/layouts/${encodeURIComponent(name)}`,
    recordings: '/recordings',
    recording: (file, download) => `/recordings/${encodeURIComponent(file)}${download ? '?download=1' : ''}`,
});

export const VIDEO_CODECS = Object.freeze({
    H264: 'h264',
    AV1: 'av1',
});
