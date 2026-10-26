const traps = new Map();
let nextId = 1;

const focusableSelector =
    'a[href], button:not([disabled]), input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])';

export function trap(container, dotNetRef) {
    const id = nextId++;
    const previous = document.activeElement;

    const onKeyDown = (event) => {
        if (event.key === 'Escape') {
            event.preventDefault();
            dotNetRef.invokeMethodAsync('OnEscape');
            return;
        }

        if (event.key !== 'Tab') {
            const menuItems = Array.from(container.querySelectorAll('[role="menuitem"]'));
            if (menuItems.length > 0 && ['ArrowDown', 'ArrowUp', 'Home', 'End'].includes(event.key)) {
                event.preventDefault();
                const index = menuItems.indexOf(document.activeElement);
                let next = index;
                if (event.key === 'ArrowDown') next = (index + 1) % menuItems.length;
                if (event.key === 'ArrowUp') next = (index - 1 + menuItems.length) % menuItems.length;
                if (event.key === 'Home') next = 0;
                if (event.key === 'End') next = menuItems.length - 1;
                menuItems[next].focus();
            }

            return;
        }

        const items = Array.from(container.querySelectorAll(focusableSelector)).filter((el) => el.offsetParent !== null);
        if (items.length === 0) {
            event.preventDefault();
            return;
        }

        const first = items[0];
        const last = items[items.length - 1];
        if (event.shiftKey && document.activeElement === first) {
            event.preventDefault();
            last.focus();
        } else if (!event.shiftKey && document.activeElement === last) {
            event.preventDefault();
            first.focus();
        }
    };

    document.addEventListener('keydown', onKeyDown);
    traps.set(id, { onKeyDown, previous });

    const firstFocusable = container.querySelector(focusableSelector);
    (firstFocusable ?? container).focus();
    return id;
}

export function release(id) {
    const entry = traps.get(id);
    if (!entry) {
        return;
    }

    document.removeEventListener('keydown', entry.onKeyDown);
    traps.delete(id);
    if (entry.previous && document.contains(entry.previous)) {
        entry.previous.focus();
    }
}
