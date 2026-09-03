const ON_STATE_CHANGED = 'OnPwaStateChanged';

class PwaInstallMonitor {
    #dotNetReference;
    #deferredPrompt;
    #displayMode = matchMedia('(display-mode: standalone)');

    constructor(dotNetReference) {
        this.#dotNetReference = dotNetReference;
        globalThis.addEventListener('beforeinstallprompt', this.#onBeforeInstallPrompt);
        globalThis.addEventListener('appinstalled', this.#onAppInstalled);
        this.#displayMode.addEventListener('change', this.#onDisplayModeChanged);

        if ('serviceWorker' in navigator) {
            navigator.serviceWorker.register('/service-worker.js', {
                scope: '/',
                updateViaCache: 'none'
            }).catch(error => {
                console.warn('Needly service worker registration failed.', error);
            });
        }
    }

    state() {
        const standalone = this.#displayMode.matches || navigator.standalone === true;
        const isiOS = /iPad|iPhone|iPod/i.test(navigator.userAgent) ||
            (navigator.platform === 'MacIntel' && navigator.maxTouchPoints > 1);

        return {
            canInstall: Boolean(this.#deferredPrompt) && !standalone,
            showIosInstructions: isiOS && !standalone
        };
    }

    promptInstall = async () => {
        if (!this.#deferredPrompt) {
            return false;
        }

        await this.#deferredPrompt.prompt();
        const choice = await this.#deferredPrompt.userChoice;
        if (choice.outcome === 'accepted') {
            this.#deferredPrompt = undefined;
            await this.#notify();
            return true;
        }

        return false;
    };

    #notify = async () => {
        const state = this.state();
        try {
            await this.#dotNetReference.invokeMethodAsync(
                ON_STATE_CHANGED,
                state.canInstall,
                state.showIosInstructions);
        } catch {
            this.dispose();
        }
    };

    #onBeforeInstallPrompt = async event => {
        event.preventDefault();
        this.#deferredPrompt = event;
        await this.#notify();
    };

    #onAppInstalled = async () => {
        this.#deferredPrompt = undefined;
        await this.#notify();
    };

    #onDisplayModeChanged = async () => await this.#notify();

    dispose() {
        globalThis.removeEventListener('beforeinstallprompt', this.#onBeforeInstallPrompt);
        globalThis.removeEventListener('appinstalled', this.#onAppInstalled);
        this.#displayMode.removeEventListener('change', this.#onDisplayModeChanged);
    }
}

let monitor;

export function initialize(dotNetReference) {
    monitor?.dispose();
    monitor = new PwaInstallMonitor(dotNetReference);
    return monitor.state();
}

export function promptInstall() {
    return monitor?.promptInstall() ?? false;
}

export function dispose() {
    monitor?.dispose();
    monitor = undefined;
}