import { h } from './dom.js';

const DRAG_THRESHOLD = 6;

let active = null;

function tileAt(x, y, findTile) {
    const el = document.elementFromPoint(x, y)?.closest('.layout-tile');
    return el ? findTile(el) : null;
}

function setDropTarget(tile) {
    if (active.target === tile) return;
    active.target?.el.classList.remove('drop-target');
    active.target = tile && tile !== active.source ? tile : null;
    active.target?.el.classList.add('drop-target');
}

function onMove(e) {
    if (!active || e.pointerId !== active.pointerId) return;
    if (!active.started) {
        if (Math.hypot(e.clientX - active.x, e.clientY - active.y) < DRAG_THRESHOLD) return;
        active.started = true;
        active.ghost = h('div', { class: 'drag-ghost' }, h('span', {}, active.label));
        document.body.append(active.ghost);
        active.source?.el.classList.add('drag-source');
    }
    e.preventDefault();
    active.ghost.style.left = `${e.clientX}px`;
    active.ghost.style.top = `${e.clientY}px`;
    setDropTarget(tileAt(e.clientX, e.clientY, active.findTile));
}

function finish(e, cancelled) {
    if (!active || e.pointerId !== active.pointerId) return;
    const drag = active;
    active = null;
    window.removeEventListener('pointermove', onMove);
    window.removeEventListener('pointerup', onUp);
    window.removeEventListener('pointercancel', onCancel);
    drag.ghost?.remove();
    drag.source?.el.classList.remove('drag-source');
    drag.target?.el.classList.remove('drop-target');
    if (!drag.started) {
        if (!cancelled) drag.onClick?.();
        return;
    }
    if (!cancelled) drag.onDrop(drag.target, { x: e.clientX, y: e.clientY });
}

function onUp(e) {
    finish(e, false);
}

function onCancel(e) {
    finish(e, true);
}

export function isDragging() {
    return active?.started === true;
}

export function beginDrag(e, { source = null, label, findTile, onDrop, onClick }) {
    if (active || (e.button != null && e.button !== 0)) return;
    active = {
        pointerId: e.pointerId,
        x: e.clientX,
        y: e.clientY,
        started: false,
        source,
        label,
        findTile,
        onDrop,
        onClick,
        target: null,
        ghost: null,
    };
    window.addEventListener('pointermove', onMove, { passive: false });
    window.addEventListener('pointerup', onUp);
    window.addEventListener('pointercancel', onCancel);
}
