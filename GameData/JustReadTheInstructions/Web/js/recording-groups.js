const STORAGE_KEY = 'jrti-recording-groups';
export const GROUP_COLORS = ['#e0533a', '#58a6ff', '#3fb950', '#d29922'];
const GROUP_LABELS = ['G1', 'G2', 'G3', 'G4'];

export class RecordingGroups {
    #assignments = {};
    #getCards;
    #barEl = null;
    #buttons = [];

    constructor(getCards) {
        this.#getCards = getCards;
        try {
            this.#assignments = JSON.parse(localStorage.getItem(STORAGE_KEY)) ?? {};
        } catch {
            this.#assignments = {};
        }
    }

    mount(barEl) {
        this.#barEl = barEl;
        for (let i = 0; i < GROUP_LABELS.length; i++) {
            const btn = document.createElement('button');
            btn.type = 'button';
            btn.className = 'btn group-btn';
            btn.style.opacity = '0';
            btn.style.pointerEvents = 'none';
            btn.textContent = `● ${GROUP_LABELS[i]}  0×  Rec`;
            btn.addEventListener('click', () => this.#toggleGroup(i));
            this.#buttons.push(btn);
            barEl.appendChild(btn);
        }
    }

    syncCard(card) {
        const groupId = this.#groupId(card.id);
        card.setGroupStrip(groupId, groupId !== null ? GROUP_COLORS[groupId] : null);
        const cycle = () => this.#cycleGroup(card);
        card.groupStripEl.onclick = cycle;
        card.groupBtnEl.onclick = cycle;
    }

    refresh() {
        const cards = [...this.#getCards().values()];
        let anyVisible = false;

        for (let i = 0; i < GROUP_LABELS.length; i++) {
            const members = cards.filter(c => this.#groupId(c.id) === i);
            const btn = this.#buttons[i];
            if (!btn) continue;

            const empty = members.length === 0;
            btn.style.opacity = empty ? '0' : '1';
            btn.style.pointerEvents = empty ? 'none' : '';
            if (empty) continue;

            anyVisible = true;

            const color = GROUP_COLORS[i];
            const anyActive = members.some(c => c.recorder?.isActive);
            const n = members.length;

            btn.style.setProperty('--gc', color);
            btn.classList.toggle('group-btn--active', anyActive);
            btn.textContent = anyActive
                ? `■ ${GROUP_LABELS[i]}  ${n}x  Stop`
                : `● ${GROUP_LABELS[i]}  ${n}x  Rec`;
        }

    }

    #groupId(cameraId) {
        const v = this.#assignments[cameraId];
        return (Number.isInteger(v) && v >= 0 && v < GROUP_LABELS.length) ? v : null;
    }

    #cycleGroup(card) {
        const cur = this.#groupId(card.id);
        const next = cur === null ? 0 : (cur + 1 >= GROUP_LABELS.length ? null : cur + 1);
        if (next === null) delete this.#assignments[card.id];
        else this.#assignments[card.id] = next;
        try { localStorage.setItem(STORAGE_KEY, JSON.stringify(this.#assignments)); } catch {}
        card.setGroupStrip(next, next !== null ? GROUP_COLORS[next] : null);
        this.refresh();
    }

    #toggleGroup(i) {
        const members = [...this.#getCards().values()].filter(c => this.#groupId(c.id) === i);
        const anyActive = members.some(c => c.recorder?.isActive);
        const pending = anyActive
            ? members.filter(c => c.recorder?.isActive).map(c => c.recorder.stop())
            : members.filter(c => !c.destroyed).map(c => c.startRecording());
        Promise.allSettled(pending).then(() => this.refresh());
    }
}
