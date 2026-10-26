let handler = null;

export function register(input) {
    unregister();
    handler = (event) => {
        if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === 'k') {
            event.preventDefault();
            input.focus();
            input.select();
        }
    };
    document.addEventListener('keydown', handler);
}

export function unregister() {
    if (handler) {
        document.removeEventListener('keydown', handler);
        handler = null;
    }
}
