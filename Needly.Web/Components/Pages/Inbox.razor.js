const ON_SHORTCUT = 'OnInboxShortcutAsync';

class InboxKeyboardNavigator {
    #root;
    #dotNetRef;
    #abortController = new AbortController();

    constructor(root, dotNetRef) {
        this.#root = root;
        this.#dotNetRef = dotNetRef;
        document.addEventListener('keydown', this.#onKeyDown, { signal: this.#abortController.signal });
    }

    #isEditable(target) {
        return target instanceof Element && Boolean(target.closest(
            'input, textarea, select, [contenteditable]:not([contenteditable="false"])'));
    }

    #rows() {
        return [...this.#root.querySelectorAll('[data-inbox-action]')];
    }

    #currentRow(rows) {
        const focused = document.activeElement?.closest?.('[data-inbox-action]');
        return focused ?? rows.find(row => row.getAttribute('aria-current') === 'true') ?? rows[0];
    }

    #notify = async (command, row) => {
        const actionId = row?.dataset.actionId;
        if (!actionId) {
            return;
        }

        try {
            await this.#dotNetRef.invokeMethodAsync(ON_SHORTCUT, command, actionId);
        } catch {
            this.dispose();
        }
    };

    #onKeyDown = async event => {
        if (event.defaultPrevented || event.altKey || event.ctrlKey || event.metaKey || this.#isEditable(event.target)) {
            return;
        }

        const key = event.key.toLowerCase();
        if (!['j', 'k', 'enter', 'e', 's', 'm'].includes(key)) {
            return;
        }

        const rows = this.#rows();
        if (rows.length === 0) {
            return;
        }

        const current = this.#currentRow(rows);
        if (key === 'j' || key === 'k') {
            event.preventDefault();
            const offset = key === 'j' ? 1 : -1;
            const index = Math.max(0, Math.min(rows.length - 1, rows.indexOf(current) + offset));
            const next = rows[index];
            next.focus({ preventScroll: true });
            next.scrollIntoView({ block: 'nearest', behavior: 'smooth' });
            await this.#notify('select', next);
            return;
        }

        event.preventDefault();
        if (key === 'enter') {
            current.querySelector('[data-primary-action]')?.click();
            return;
        }

        await this.#notify(key, current);
    };

    dispose() {
        this.#abortController.abort();
    }
}

export function createInboxKeyboardNavigator(root, dotNetRef) {
    return new InboxKeyboardNavigator(root, dotNetRef);
}