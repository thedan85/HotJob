// Native <details> nav dropdowns: close on outside click, Escape, or link click.
// No dependency. The mobile hamburger keeps its own Blazor state.
(() => {
    const closeAll = (except) => {
        for (const dropdown of document.querySelectorAll('[data-nav-dropdown][open]')) {
            if (dropdown !== except) dropdown.removeAttribute('open');
        }
    };

    document.addEventListener('click', (event) => {
        const inside = event.target instanceof Element
            ? event.target.closest('[data-nav-dropdown]')
            : null;
        closeAll(inside);
    });

    document.addEventListener('keydown', (event) => {
        if (event.key !== 'Escape') return;
        const open = document.querySelector('[data-nav-dropdown][open]');
        if (!open) return;
        open.removeAttribute('open');
        open.querySelector('summary')?.focus();
    });

    document.addEventListener('click', (event) => {
        if (!(event.target instanceof Element)) return;
        if (!event.target.closest('[data-nav-dropdown] .nav-link')) return;
        closeAll(null);
    });
})();
