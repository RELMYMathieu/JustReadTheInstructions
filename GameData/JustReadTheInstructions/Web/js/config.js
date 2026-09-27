export const SNAPSHOT_REFRESH_MS = 10000;
export const CAMERA_SYNC_MS = 5000;
export const LOS_DELAY_MS = 3000;
export const VIEWER_LOS_DELAY_MS = 5000;

export const STREAM_RETRY_MIN_MS = 1000;
export const STREAM_RETRY_MAX_MS = 10000;
export const STREAM_STALL_MS = 15000;

export const RECORDER_CHUNK_MS = 2000;
export const RECORDER_CAPTURE_FPS = 24;
export const RECORDER_VIDEO_BPS = 3_500_000;
export const RECORDER_HEARTBEAT_MS = 5000;
export const RECORDER_FINALIZE_TIMEOUT_MS = 15000;
export const RECORDER_LOS_DELAY_MS = 5000;
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
    recordingAppend: (sessionId, filename) =>
        `/recordings/${sessionId}/append?name=${encodeURIComponent(filename)}`,
    recordingFinalize: (sessionId, filename) =>
        `/recordings/${sessionId}/finalize?name=${encodeURIComponent(filename)}`,
    recordingHeartbeat: (sessionId, filename) =>
        `/recordings/${sessionId}/heartbeat?name=${encodeURIComponent(filename)}`,
});

export const LOS_BEHAVIORS = Object.freeze({
    AUTO_SAVE: 'auto_save',
    PAUSE: 'pause',
    DISCARD: 'discard',
});

export const DEFAULT_LOS_BEHAVIOR = LOS_BEHAVIORS.AUTO_SAVE;

export const RECORDERS = Object.freeze({
    GAME: 'game',
    BROWSER: 'browser',
});

export const VIDEO_CODECS = Object.freeze({
    H264: 'h264',
    AV1: 'av1',
});
