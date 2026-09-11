let lastFocusedElement = null;

export function rememberActiveElement() {
    const element = document.activeElement;
    lastFocusedElement = element instanceof HTMLElement && element !== document.body
        ? element
        : null;
}

export function restoreActiveElement() {
    const element = lastFocusedElement;
    lastFocusedElement = null;
    if (element instanceof HTMLElement && element.isConnected && !element.disabled) {
        element.focus({ preventScroll: true });
    }
}

export function focusHistoryEntry(entryId) {
    const button = Array.from(document.querySelectorAll("[data-history-entry-id]")
        .values())
        .find(element => element.dataset.historyEntryId === entryId);

    button?.focus();
}
