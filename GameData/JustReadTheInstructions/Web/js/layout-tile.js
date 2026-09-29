import { VIEWER_LOS_DELAY_MS, LOS_OVERLAY_HTML, WAITING_OVERLAY_HTML } from './config.js';
import { FeedCanvas } from './feed-canvas.js';
import { h, icon, button } from './dom.js';

const OFFLINE_OPTION_VALUE = 'offline';
const NARROW_TILE_PX = 300;
const LEAVE_MS = 320;

const SIGNAL_OVERLAYS = Object.freeze({
    empty: '<span class="waiting">No camera. Pick one below, or drag one here from Cameras.</span>',
    waiting: WAITING_OVERLAY_HTML,
    lost: LOS_OVERLAY_HTML,
});

export class LayoutTile {
    constructor({ id = null, name = null }, { subscribe, onFrameSize, onPick, onSpotlight, onRemove, onGrab }) {
        this.id = id;
        this.name = name;
        this.bound = false;
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
        this.el = this._buildDom({ onPick, onSpotlight, onRemove, onGrab });
        this.el.classList.add('entering');
        this._renderLabel();
        this._setSignal('waiting');
    }

    get label() {
        return this.camera?.name ?? this.name ?? (this.id === null ? '' : `Camera ${this.id}`);
    }

    get hasBinding() {
        return this.id !== null || this.name !== null;
    }

    get stored() {
        return { id: this.id, name: this.name };
    }

    assign(camera) {
        this.id = camera?.id ?? null;
        this.name = camera?.name ?? null;
        this.bound = camera != null;
        this._resetFeed();
        this.setCamera(camera);
    }

    setCamera(camera) {
        if (camera && camera.id !== this.id) {
            this.id = camera.id;
            this._resetFeed();
        }
        if (camera) {
            this.bound = true;
            this.name ??= camera.name;
        }
        this.camera = camera;
        this._followCamera(camera?.id ?? null);
        this._renderLabel();
        this._renderPicker();
        this.updateSignal(Date.now());
    }

    setCameraList(cameras) {
        this._cameras = cameras;
        this._renderPicker();
    }

    setSpotlit(spotlit) {
        this._spotlightBtn.setAttribute('aria-pressed', String(spotlit));
        const title = spotlit ? 'Back to the grid' : 'Spotlight: make this tile large';
        this._spotlightBtn.title = title;
        this._spotlightBtn.setAttribute('aria-label', title);
    }

    place({ x, y, width, height }) {
        this.el.classList.toggle('narrow', width < NARROW_TILE_PX);
        Object.assign(this.el.style, {
            left: `${x}px`,
            top: `${y}px`,
            width: `${width}px`,
            height: `${height}px`,
        });
        if (!this.el.classList.contains('entering')) return;
        void this.el.offsetWidth;
        this.el.classList.remove('entering');
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

    retire() {
        this._followCamera(null);
        this.el.classList.add('leaving');
        setTimeout(() => this.el.remove(), LEAVE_MS);
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

    _renderLabel() {
        this._nameText.textContent = this.label;
        this._nameEl.hidden = !this.label;
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
        options.push(...cameras.map((c) => new Option(`${c.name}  #${c.id}`, String(c.id))));

        this._picker.replaceChildren(...options);
        this._picker.value = this.camera ? String(this.id) : offline ? OFFLINE_OPTION_VALUE : '';
    }

    _buildDom({ onPick, onSpotlight, onRemove, onGrab }) {
        this._overlay = h('div', { class: 'offline-overlay' });
        this._nameText = h('span');
        this._nameEl = h('div', { class: 'layout-tile-name' }, h('span', { class: 'lamp' }), this._nameText);

        this._picker = h('select', { class: 'input layout-picker', 'aria-label': 'Camera for this tile' });
        this._picker.addEventListener('change', () => {
            const value = this._picker.value;
            this._picker.blur();
            onPick(this, value === '' ? null : Number(value));
        });

        const grip = h('span', { class: 'btn btn-quiet btn-icon tile-grip', title: 'Drag onto another tile to swap them' }, icon('grip'));
        grip.addEventListener('pointerdown', (e) => onGrab(this, e));

        this._spotlightBtn = button({ icon: 'spotlight', className: 'btn overlay-btn', title: 'Spotlight: make this tile large', pressed: false, onClick: () => onSpotlight(this) });
        const removeBtn = button({ icon: 'close', className: 'btn overlay-btn', title: 'Remove this tile', onClick: () => onRemove(this) });

        const bar = h('div', { class: 'layout-tile-bar layout-chrome' },
            grip, this._picker, h('div', { class: 'tile-bar-end' }, this._spotlightBtn, removeBtn));

        const tile = h('div', { class: 'layout-tile' }, this._feed.el, this._overlay, this._nameEl, bar);
        tile.addEventListener('dblclick', (e) => {
            if (!e.target.closest('.layout-tile-bar')) onSpotlight(this);
        });
        tile.addEventListener('pointerdown', (e) => {
            if (e.pointerType === 'mouse' && !e.target.closest('.layout-tile-bar')) onGrab(this, e);
        });
        return tile;
    }
}
