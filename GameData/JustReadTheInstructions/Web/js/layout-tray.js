import { API } from './config.js';
import { h } from './dom.js';
import { Sheet } from './ui.js';

export class CameraTray {
    constructor({ sheetEl, listEl, openerEl, isInLayout, onPointerDown }) {
        this._listEl = listEl;
        this._openerEl = openerEl;
        this._isInLayout = isInLayout;
        this._onPointerDown = onPointerDown;
        this._cameras = [];
        this._key = null;
        this._items = new Map();
        this.sheet = new Sheet(sheetEl, {
            dismissOnOutside: false,
            onOpen: () => this._loadThumbnails(),
        });
    }

    get isOpen() {
        return this.sheet.isOpen;
    }

    toggle() {
        this.sheet.toggle(this._openerEl);
    }

    open() {
        this.sheet.open(this._openerEl);
    }

    close() {
        this.sheet.close();
    }

    setCameras(cameras) {
        this._cameras = cameras;
        const key = JSON.stringify(cameras.map((c) => [c.id, c.name]));
        if (key !== this._key) {
            this._key = key;
            this._render();
        }
        this.refreshMarks();
    }

    refreshMarks() {
        for (const [id, item] of this._items) {
            const inLayout = this._isInLayout(id);
            item.el.classList.toggle('in-layout', inLayout);
            item.sub.replaceChildren(`#${id}`, inLayout ? h('span', { class: 'in' }, '  ·  in the layout') : '');
        }
    }

    _render() {
        this._items.clear();
        if (this._cameras.length === 0) {
            this._listEl.replaceChildren(h('li', { class: 'list-empty' }, 'No cameras open in KSP yet.'));
            return;
        }
        this._listEl.replaceChildren(...this._cameras.map((camera) => {
            const img = h('img', { alt: '', draggable: 'false' });
            const sub = h('div', { class: 'tray-sub' });
            const el = h('li', { class: 'tray-camera', title: 'Click to add, or drag onto a tile' },
                h('div', { class: 'tray-thumb' }, img),
                h('div', { class: 'tray-text' }, h('div', { class: 'tray-name' }, camera.name), sub));
            el.addEventListener('pointerdown', (e) => this._onPointerDown(camera, e));
            this._items.set(camera.id, { el, img, sub });
            return el;
        }));
        if (this.isOpen) this._loadThumbnails();
    }

    _loadThumbnails() {
        for (const [id, item] of this._items) item.img.src = API.snapshot(id);
    }
}
