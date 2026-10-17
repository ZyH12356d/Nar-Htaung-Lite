const listeners = new WeakMap();
export function show(dialog, receiver) {
    const cancel = event => {
        event.preventDefault();
        receiver.invokeMethodAsync('RequestClose');
    };
    dialog.addEventListener('cancel', cancel);
    listeners.set(dialog, cancel);
    dialog.showModal();
    dialog.querySelector('[autofocus]')?.focus();
}
export function close(dialog) {
    const cancel = listeners.get(dialog);
    if (cancel) dialog.removeEventListener('cancel', cancel);
    listeners.delete(dialog);
    dialog.close();
}
