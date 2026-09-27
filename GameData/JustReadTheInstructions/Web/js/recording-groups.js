import { h } from './dom.js';

const STORAGE_KEY = 'jrti-recording-groups-by-name';
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
        barEl.setAttribute('aria-label', 'Record groups');
        GROUP_LABELS.forEach((label, i) => {
            const btn = h('button', {
                type: 'button',
                class: 'btn group-btn',
                hidden: true,
                onClick: () => this.#toggleGroup(i),
            }, label);
            this.#buttons.push(btn);
            barEl.append(btn);
        });
    }

    syncCard(card) {
        this.#paintCard(card, this.#groupId(card));
        card.onCycleGroup = () => this.#cycleGroup(card);
        card.onRecordingChange = () => this.refresh();
    }

    refresh() {
        const cards = [...this.#getCards().values()];
        let anyVisible = false;

        GROUP_LABELS.forEach((label, i) => {
            const members = cards.filter((c) => this.#groupId(c) === i);
            const btn = this.#buttons[i];
            btn.hidden = members.length === 0;
            if (btn.hidden) return;

            anyVisible = true;
            const anyActive = members.some((c) => c.recorder?.isActive);
            btn.classList.toggle('group-btn--active', anyActive);
            btn.textContent = `${anyActive ? 'Stop' : 'Record'} ${label} (${members.length})`;
            btn.title = anyActive
                ? `Stop recording the ${members.length} camera(s) in ${label}`
                : `Record the ${members.length} camera(s) in ${label} together`;
        });

        if (this.#barEl) this.#barEl.hidden = !anyVisible;
    }

    #groupId(card) {
        const v = this.#assignments[card.key];
        return Number.isInteger(v) && v >= 0 && v < GROUP_LABELS.length ? v : null;
    }

    #paintCard(card, groupId) {
        card.setGroup(groupId === null ? null : GROUP_LABELS[groupId]);
    }

    #cycleGroup(card) {
        const cur = this.#groupId(card);
        const next = cur === null ? 0 : cur + 1 >= GROUP_LABELS.length ? null : cur + 1;
        if (next === null) delete this.#assignments[card.key];
        else this.#assignments[card.key] = next;
        try { localStorage.setItem(STORAGE_KEY, JSON.stringify(this.#assignments)); } catch { }
        this.#paintCard(card, next);
        this.refresh();
    }

    #toggleGroup(i) {
        const members = [...this.#getCards().values()].filter((c) => this.#groupId(c) === i);
        const anyActive = members.some((c) => c.recorder?.isActive);
        const pending = anyActive
            ? members.filter((c) => c.recorder?.isActive).map((c) => c.recorder.stop())
            : members.filter((c) => !c.destroyed).map((c) => c.startRecording());
        Promise.allSettled(pending).then(() => this.refresh());
    }
}
