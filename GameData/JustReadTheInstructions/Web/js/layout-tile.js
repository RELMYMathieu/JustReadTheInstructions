import { VIEWER_LOS_DELAY_MS, LOS_OVERLAY_HTML, WAITING_OVERLAY_HTML } from './config.js';
import { FeedCanvas } from './feed-canvas.js';

const OFFLINE_OPTION_VALUE = 'offline';

const SIGNAL_OVERLAYS = Object.freeze({
    empty: '<span>No camera selected</span>',
    waiting: WAITING_OVERLAY_HTML,
    lost: LOS_OVERLAY_HTML,
});

function makeTileButton(icon, title, onClick) {
    const btn = document.createElement('button');
    btn.type = 'button';
    btn.className = 'btn layout-tile-btn';
    btn.title = title;
    btn.setAttribute('aria-label', title);
    btn.innerHTML = `<i class="fa-solid ${icon}"></i>`;
    btn.addEventListener('click', onClick);
    return btn;
}

export class LayoutTile {
    constructor({ id = null, name = null }, { subscribe, onFrameSize, onPick, onSpotlight, onRemove }) {
        this.id = id;
        this.name = name;
        this.camera = null;

        this._subscribe = subscribe;
        this._unsubscribe = null;
        this._subscribedId = null;
        this._cameras = [];
        this._frameSeen = false;
        this._lastSignalAt = Date.now();
        this._signal = null;
        this._pickerKey = null;

        this._feed = new FeedCanvas('layout-tile-feed', {
            onDraw: () => this._onFrameDrawn(),
            onResize: onFrameSize,
        });
        this.el = this._buildDom(onPick, onSpotlight, onRemove);
        this._nameEl.textContent = this.label;
        this._setSignal('waiting');
    }

    get label() {
        return this.camera?.name ?? this.name ?? (this.id === null ? '' : `Camera ${this.id}`);
    }

    get hasBinding() {
        return this.id !== null || this.name !== null;
    }

    assign(camera) {
        this.id = camera?.id ?? null;
        this.name = camera?.name ?? null;
        this._resetFeed();
        this.setCamera(camera);
    }

    setCamera(camera) {
        if (camera && camera.id !== this.id) {
            this.id = camera.id;
            this._resetFeed();
        }
        this.camera = camera;
        this._followCamera(camera?.id ?? null);
        this._nameEl.textContent = this.label;
        this._renderPicker();
        this.updateSignal(Date.now());
    }

    setCameraList(cameras) {
        this._cameras = cameras;
        this._renderPicker();
    }

    setSpotlit(spotlit) {
        this._spotlightBtn.classList.toggle('active', spotlit);
        const title = spotlit ? 'Back to grid' : 'Spotlight';
        this._spotlightBtn.title = title;
        this._spotlightBtn.setAttribute('aria-label', title);
    }

    place({ x, y, width, height }) {
        Object.assign(this.el.style, {
            left: `${x}px`,
            top: `${y}px`,
            width: `${width}px`,
            height: `${height}px`,
        });
    }

    updateSignal(now) {
        if (!this.camera) {
            this._frameSeen = false;
            this._lastSignalAt = now;
            this._setSignal(this.hasBinding ? 'lost' : 'empty');
        } else if (now - this._lastSignalAt >= VIEWER_LOS_DELAY_MS) {
            this._setSignal('lost');
        } else if (!this._frameSeen) {
            this._setSignal('waiting');
        }
    }

    dispose() {
        this._followCamera(null);
        this._feed.clear();
        this.el.remove();
    }

    _followCamera(cameraId) {
        if (cameraId === this._subscribedId) return;
        this._unsubscribe?.();
        this._subscribedId = cameraId;
        this._unsubscribe = cameraId === null
            ? null
            : this._subscribe(cameraId, (frame) => this._feed.push(frame));
    }

    _onFrameDrawn() {
        this._frameSeen = true;
        this._lastSignalAt = Date.now();
        this._setSignal('live');
    }

    _resetFeed() {
        this._feed.clear();
        this._frameSeen = false;
        this._lastSignalAt = Date.now();
    }

    _setSignal(signal) {
        if (signal === this._signal) return;
        this._signal = signal;
        this.el.classList.toggle('offline', signal !== 'live');
        this._overlay.innerHTML = SIGNAL_OVERLAYS[signal] ?? '';
    }

    _renderPicker() {
        const cameras = this._cameras;
        const key = JSON.stringify([this.id, this.name, Boolean(this.camera), cameras.map((c) => [c.id, c.name])]);
        if (key === this._pickerKey) return;
        this._pickerKey = key;

        const options = [new Option('No camera', '')];
        const offline = this.hasBinding && !this.camera;
        if (offline) {
            const option = new Option(`${this.label} (offline)`, OFFLINE_OPTION_VALUE);
            option.disabled = true;
            options.push(option);
        }
        options.push(...cameras.map((c) => new Option(c.name, String(c.id))));

        this._picker.replaceChildren(...options);
        this._picker.value = this.camera ? String(this.id) : offline ? OFFLINE_OPTION_VALUE : '';
    }

    _buildDom(onPick, onSpotlight, onRemove) {
        const tile = document.createElement('div');
        tile.className = 'layout-tile';
        tile.addEventListener('dblclick', (e) => {
            if (!e.target.closest('.layout-chrome')) onSpotlight(this);
        });

        this._overlay = document.createElement('div');
        this._overlay.className = 'offline-overlay';

        this._nameEl = document.createElement('span');
        this._nameEl.className = 'layout-tile-name';

        this._picker = document.createElement('select');
        this._picker.className = 'layout-picker';
        this._picker.setAttribute('aria-label', 'Camera');
        this._picker.addEventListener('change', () => {
            const value = this._picker.value;
            this._picker.blur();
            onPick(this, value === '' ? null : Number(value));
        });

        this._spotlightBtn = makeTileButton('fa-maximize', 'Spotlight', () => onSpotlight(this));
        const removeBtn = makeTileButton('fa-xmark', 'Remove tile', () => onRemove(this));

        const bar = document.createElement('div');
        bar.className = 'layout-tile-bar layout-chrome';
        bar.append(this._picker, this._spotlightBtn, removeBtn);

        tile.append(this._feed.el, this._overlay, this._nameEl, bar);
        return tile;
    }
}
