const registrations = new WeakMap();

function getInput(dropZone) {
    return dropZone.querySelector('input[type="file"]');
}

function isFileDrag(event) {
    return Array.from(event.dataTransfer?.types ?? []).includes("Files");
}

export function registerHeroDropZone(dropZone) {
    if (!dropZone || registrations.has(dropZone)) {
        return;
    }

    const input = getInput(dropZone);
    if (!input) {
        return;
    }

    const onDragOver = (event) => {
        if (!isFileDrag(event)) {
            return;
        }

        event.preventDefault();
        if (event.dataTransfer) {
            event.dataTransfer.dropEffect = "copy";
        }
    };

    const onDrop = (event) => {
        if (!isFileDrag(event)) {
            return;
        }

        event.preventDefault();

        const files = event.dataTransfer?.files;
        if (!input || !files || files.length === 0) {
            return;
        }

        input.files = files;
        input.dispatchEvent(new Event("change", {bubbles: true}));
    };

    dropZone.addEventListener("dragover", onDragOver);
    dropZone.addEventListener("drop", onDrop);
    registrations.set(dropZone, {onDragOver, onDrop});
}

export function disposeHeroDropZone(dropZone) {
    const registration = registrations.get(dropZone);
    if (!registration) {
        return;
    }

    dropZone.removeEventListener("dragover", registration.onDragOver);
    dropZone.removeEventListener("drop", registration.onDrop);
    registrations.delete(dropZone);
}

// Backward-compatible fallback for older hot-reloaded C# code paths.
export function openFilePicker(dropZone) {
    const input = getInput(dropZone);
    input?.click();
}
