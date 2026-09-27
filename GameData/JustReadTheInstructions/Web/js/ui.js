import { h } from './dom.js';
import { copyToClipboard } from './clipboard.js';

const MENU_GAP = 6;
const TOAST_MS = 1800;
const TOAST_ACTION_MS = 6000;

let openMenu = null;
let toastEl = null;
let toastTimer = null;

document.addEventListener('pointerdown', (e) => {
    if (openMenu && !openMenu.el.contains(e.target) && !openMenu.anchor.contains(e.target)) openMenu.close();
}, true);

document.addEventListener('keydown', (e) => {
    if (e.key === 'Escape' && openMenu) {
        const anchor = openMenu.anchor;
        openMenu.close();
        anchor.focus();
    }
});

window.addEventListener('resize', () => openMenu?.close());

export class Menu {
    constructor(anchor, build) {
        this.anchor = anchor;
        this._build = build;
        this.el = h('div', { class: 'menu', role: 'menu' });
        document.body.append(this.el);
        anchor.setAttribute('aria-haspopup', 'menu');
        anchor.setAttribute('aria-expanded', 'false');
        anchor.addEventListener('click', (e) => {
            e.stopPropagation();
            if (openMenu === this) this.close();
            else this.open();
        });
    }

    open() {
        openMenu?.close();
        this.el.replaceChildren(...this._build(this));
        this._position();
        this.el.classList.add('open');
        this.anchor.setAttribute('aria-expanded', 'true');
        openMenu = this;
        this.el.querySelector('input, .menu-item')?.focus({ preventScroll: true });
    }

    close() {
        this.el.classList.remove('open');
        this.anchor.setAttribute('aria-expanded', 'false');
        if (openMenu === this) openMenu = null;
    }

    get isOpen() {
        return openMenu === this;
    }

    rerender() {
        if (!this.isOpen) return;
        this.el.replaceChildren(...this._build(this));
        this._position();
    }

    dispose() {
        this.close();
        this.el.remove();
    }

    _position() {
        const r = this.anchor.getBoundingClientRect();
        const { offsetHeight: height, offsetWidth: width } = this.el;
        const below = r.bottom + MENU_GAP + height <= window.innerHeight || r.top < height + MENU_GAP;
        this.el.style.top = below ? `${r.bottom + MENU_GAP}px` : `${Math.max(MENU_GAP, r.top - MENU_GAP - height)}px`;
        this.el.style.left = `${Math.max(MENU_GAP, Math.min(r.right - width, window.innerWidth - width - MENU_GAP))}px`;
    }
}

export function menuItem({ label, detail, onSelect, className = '', href, target, download }) {
    const tag = href ? 'a' : 'button';
    const item = h(tag, {
        class: `menu-item ${className}`.replace(/\s+/g, ' ').trim(),
        role: 'menuitem',
        type: href ? null : 'button',
        href,
        target,
        download,
        rel: target ? 'noopener' : null,
    }, h('span', { class: 'menu-label' }, label), detail ? h('span', { class: 'menu-detail' }, detail) : null);
    item.addEventListener('click', () => {
        openMenu?.close();
        onSelect?.();
    });
    return item;
}

export function menuSeparator() {
    return h('div', { class: 'menu-sep', role: 'separator' });
}

export function menuHeading(text) {
    return h('div', { class: 'menu-heading' }, text);
}

export function isMenuOpen() {
    return openMenu !== null;
}

export class Sheet {
    constructor(el, { onOpen, onClose, dismissOnOutside = true } = {}) {
        this.el = el;
        this._onOpen = onOpen;
        this._onClose = onClose;
        this._opener = null;
        el.querySelector('[data-close]')?.addEventListener('click', () => this.close());
        document.addEventListener('keydown', (e) => {
            if (e.key === 'Escape' && this.isOpen && !openMenu) this.close();
        });
        document.addEventListener('pointerdown', (e) => {
            if (!dismissOnOutside || !this.isOpen || el.contains(e.target) || this._opener?.contains(e.target)) return;
            if (e.target.closest('.menu, .modal-backdrop')) return;
            this.close();
        }, true);
    }

    get isOpen() {
        return this.el.classList.contains('open');
    }

    toggle(opener) {
        if (this.isOpen) this.close();
        else this.open(opener);
    }

    open(opener) {
        this._opener = opener ?? null;
        this.el.classList.add('open');
        this._opener?.setAttribute('aria-expanded', 'true');
        this._onOpen?.();
        this.el.querySelector('.sheet-body')?.focus({ preventScroll: true });
    }

    close() {
        if (!this.isOpen) return;
        this.el.classList.remove('open');
        this._opener?.setAttribute('aria-expanded', 'false');
        this._onClose?.();
    }
}

function hideToast() {
    clearTimeout(toastTimer);
    toastEl?.classList.remove('visible');
}

export function toast(message, action) {
    if (!toastEl) {
        toastEl = h('div', { class: 'toast', role: 'status', 'aria-live': 'polite' });
        document.body.append(toastEl);
    }
    toastEl.replaceChildren(h('span', {}, message));
    if (action) {
        toastEl.append(h('button', {
            type: 'button',
            class: 'btn btn-quiet toast-action',
            onClick: () => {
                hideToast();
                action.onAction();
            },
        }, action.label));
    }
    toastEl.classList.toggle('has-action', Boolean(action));
    toastEl.classList.add('visible');
    clearTimeout(toastTimer);
    toastTimer = setTimeout(hideToast, action ? TOAST_ACTION_MS : TOAST_MS);
}

export async function copyWithToast(text, what = 'Link') {
    if (await copyToClipboard(text)) toast(`${what} copied`);
}

export function formatBytes(bytes) {
    if (bytes < 1024) return `${bytes} B`;
    if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
    if (bytes < 1024 * 1024 * 1024) return `${(bytes / 1024 / 1024).toFixed(1)} MB`;
    return `${(bytes / 1024 / 1024 / 1024).toFixed(2)} GB`;
}

export function formatTimecode(ms) {
    const total = Math.max(0, Math.floor(ms / 1000));
    const hh = String(Math.floor(total / 3600)).padStart(2, '0');
    const mm = String(Math.floor(total / 60) % 60).padStart(2, '0');
    const ss = String(total % 60).padStart(2, '0');
    return `${hh}:${mm}:${ss}`;
}

export function isTyping(e) {
    return e.target.closest?.('input, select, textarea, [contenteditable="true"]') != null;
}
