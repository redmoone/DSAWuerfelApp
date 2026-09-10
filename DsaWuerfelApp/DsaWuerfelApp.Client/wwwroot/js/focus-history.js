export function focusHistoryEntry(entryId) {
    const button = Array.from(document.querySelectorAll("[data-history-entry-id]")
        .values())
        .find(element => element.dataset.historyEntryId === entryId);

    button?.focus();
}
