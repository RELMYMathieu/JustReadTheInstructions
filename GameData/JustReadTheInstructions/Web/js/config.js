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

export const TRACKS = Object.freeze([
    { id: 'off', label: 'Off', hint: 'Stays where you point it.' },
    { id: 'vessel', label: 'Active vessel', hint: 'Follows the vessel you fly and zooms with its distance.' },
    { id: 'target', label: 'Target', hint: 'Follows your target and zooms with its distance.' },
]);

const V1 = '/api/v1';

export const API = Object.freeze({
    discovery: V1,
    session: `${V1}/session`,
    cameras: `${V1}/cameras`,
    snapshot: (id) => `${V1}/camera/${id}/snapshot`,
    obsStream: (id) => `/camera/${id}/stream`,
    audio: (id) => `${V1}/camera/${id}/audio`,
    streams: (ids, preview) => `${V1}/streams?ids=${ids.join(',')}${preview ? '&preview=1' : ''}`,
    status: (id) => `${V1}/camera/${id}/status`,
    settings: (id) => `${V1}/camera/${id}/settings`,
    gameRecording: (id, action, codec) => `${V1}/camera/${id}/recording/${action}${codec ? `?codec=${codec}` : ''}`,
    viewer: (id) => `/viewer.html?id=${id}`,
    debugStats: `${V1}/debug/stats`,
    events: `${V1}/events`,
    program: `${V1}/program`,
    programTake: (name) => `${V1}/program/take/${encodeURIComponent(name)}`,
    programClear: `${V1}/program/clear`,
    layouts: `${V1}/layouts`,
    layout: (name) => `${V1}/layouts/${encodeURIComponent(name)}`,
    recordings: `${V1}/recordings`,
    recording: (file, download) => `${V1}/recordings/${encodeURIComponent(file)}${download ? '?download=1' : ''}`,
});

export const VIDEO_CODECS = Object.freeze({
    H264: 'h264',
    AV1: 'av1',
});
