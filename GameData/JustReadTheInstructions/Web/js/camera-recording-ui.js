import { setButtonLabel } from './dom.js';
import { formatBytes, formatTimecode } from './ui.js';

const REC_STATES = {
    recording: {
        cardClass: 'recording',
        status: 'Recording',
        recBtn: { label: 'Stop', active: true, disabled: false },
        pauseBtn: { hidden: false, label: 'Pause' },
    },
    paused: {
        cardClass: 'paused',
        status: 'Paused',
        recBtn: { label: 'Stop', active: true, disabled: false },
        pauseBtn: { hidden: false, label: 'Resume' },
    },
    finalizing: {
        cardClass: null,
        status: 'Saving...',
        recBtn: { label: 'Saving', active: false, disabled: true },
        pauseBtn: { hidden: true },
    },
    idle: {
        cardClass: null,
        status: 'Idle',
        recBtn: { label: 'Record', active: false, disabled: false },
        pauseBtn: { hidden: true },
    },
};

export class CameraRecordingUI {
    constructor(cardEl, { getRecorder, onIdle }) {
        this._el = cardEl;
        this._getRecorder = getRecorder;
        this._onIdle = onIdle;

        this._clockTimer = null;
        this._lastBytes = 0;
        this._recBtn = cardEl.querySelector('[data-role="record"]');
        this._pauseBtn = cardEl.querySelector('[data-role="pause"]');
        this._statusEl = cardEl.querySelector('[data-role="rec-status"]');
        this._sizeEl = cardEl.querySelector('[data-role="rec-size"]');
    }

    onStateChange({ state, bytesUploaded, startedAt }) {
        const spec = REC_STATES[state] ?? REC_STATES.idle;

        this._el.classList.toggle('recording', spec.cardClass === 'recording');
        this._el.classList.toggle('paused', spec.cardClass === 'paused');

        setButtonLabel(this._recBtn, spec.recBtn.label);
        this._recBtn.classList.toggle('active', spec.recBtn.active);
        this._recBtn.disabled = spec.recBtn.disabled;

        this._pauseBtn.hidden = spec.pauseBtn.hidden;
        if (!spec.pauseBtn.hidden) setButtonLabel(this._pauseBtn, spec.pauseBtn.label);

        if (state === 'recording') this._startClock(startedAt);
        else this._stopClock(spec.status);

        if (state === 'idle') this._onIdle(this._statusEl);

        this._updateSize(state, bytesUploaded);
    }

    dispose() {
        this._stopClock(null);
    }

    _updateSize(state, bytes) {
        const active = state === 'recording' || state === 'paused' || state === 'finalizing';
        if (active) this._lastBytes = bytes;
        const shown = active ? bytes : this._lastBytes;
        this._sizeEl.textContent = shown > 0 ? (active ? formatBytes(shown) : `Saved ${formatBytes(shown)}`) : '';
    }

    _startClock(startedAt) {
        this._startedAt = startedAt;
        this._tickClock();
        if (!this._clockTimer) this._clockTimer = setInterval(() => this._tickClock(), 500);
    }

    _tickClock() {
        const recorder = this._getRecorder();
        if (recorder && recorder.state !== 'recording') return;
        this._statusEl.textContent = `REC ${formatTimecode(Date.now() - this._startedAt)}`;
    }

    _stopClock(label) {
        clearInterval(this._clockTimer);
        this._clockTimer = null;
        if (label) this._statusEl.textContent = label;
    }
}
