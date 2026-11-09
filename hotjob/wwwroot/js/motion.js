// Quiet Signal motion: reveal, stagger, count-up, condensing nav.
// No dependency. Everything no-ops under prefers-reduced-motion.

(() => {
    const reduceMotion = window.matchMedia('(prefers-reduced-motion: reduce)');
    const revealSelector = [
        '.home-hero > *',
        '.home-section > *',
        '.home-panel',
        '.home-cta',
        '.shortcut-card',
        '.job-card',
        '.company-card',
        '.jobs-hero > *',
        '.companies-hero > *',
        '.job-detail-header',
        '.job-detail-main > section',
        '.job-detail-side > *',
        '.company-detail-header',
        '.company-detail > *',
        '.workspace-header',
        '.workspace-stat',
        '.workspace-card',
        '.workspace-toolbar',
        '.workspace-tabs',
        '.review-card',
        '.premium-hero',
        '.premium-plan-card',
        '.state-page',
        '.login-form-inner > *',
        '.login-benefits-inner > *',
        '.form-inner > *',
        '.benefits-inner > *'
    ].join(',');

    const staggerContainers = [
        '.shortcut-grid',
        '.jobs-list',
        '.company-grid',
        '.home-split',
        '.workspace-metrics',
        '.review-list',
        '.admin-grid'
    ].join(',');

    const observer = 'IntersectionObserver' in window
        ? new IntersectionObserver(onIntersect, { rootMargin: '0px 0px -8% 0px', threshold: 0.08 })
        : null;

    function onIntersect(entries) {
        for (const entry of entries) {
            if (!entry.isIntersecting) continue;
            entry.target.classList.add('is-revealed');
            observer.unobserve(entry.target);
            countUp(entry.target);
        }
    }

    function countUp(element) {
        if (reduceMotion.matches || element.dataset.counted === 'true') return;
        const value = element.querySelector?.('.workspace-stat-value');
        if (!value || value.dataset.counted === 'true') return;

        const text = value.textContent.trim();
        if (!/^\d{1,7}$/.test(text)) return;

        const target = Number(text);
        if (!Number.isFinite(target) || target < 2) return;

        element.dataset.counted = 'true';
        value.dataset.counted = 'true';
        const duration = 720;
        const start = performance.now();

        const tick = (now) => {
            const progress = Math.min(1, (now - start) / duration);
            const eased = 1 - Math.pow(1 - progress, 3);
            value.textContent = String(Math.round(target * eased));
            if (progress < 1) requestAnimationFrame(tick);
        };

        value.textContent = '0';
        requestAnimationFrame(tick);
    }

    function markReveal(element, index) {
        if (element.dataset.revealReady === 'true') return;
        element.dataset.revealReady = 'true';
        element.classList.add('reveal');

        const parent = element.parentElement?.closest(staggerContainers);
        if (parent) {
            const siblings = Array.from(parent.children).filter((child) => child.classList.contains('reveal'));
            const order = Math.max(0, siblings.indexOf(element));
            element.style.setProperty('--reveal-delay', `${Math.min(order, 5) * 70}ms`);
        }

        if (observer) {
            observer.observe(element);
        } else {
            element.classList.add('is-revealed');
        }
    }

    function scan(root) {
        if (reduceMotion.matches) return;

        const scope = root.nodeType === Node.ELEMENT_NODE ? root : document;
        if (scope.matches?.(revealSelector)) markReveal(scope, 0);

        for (const element of scope.querySelectorAll(revealSelector)) {
            markReveal(element, 0);
        }
    }

    function bind() {
        scan(document);

        if (reduceMotion.matches) return;

        new MutationObserver((records) => {
            for (const record of records) {
                for (const node of record.addedNodes) {
                    if (node.nodeType === Node.ELEMENT_NODE) scan(node);
                }
            }
        }).observe(document.body, { childList: true, subtree: true });
    }

    let navBound = false;

    function bindNav() {
        if (navBound) return;
        const nav = document.querySelector('.top-row');
        if (!nav) return;

        navBound = true;
        const update = () => nav.classList.toggle('is-scrolled', window.scrollY > 8);
        update();
        window.addEventListener('scroll', update, { passive: true });
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', () => {
            bind();
            bindNav();
        }, { once: true });
    } else {
        bind();
        bindNav();
    }

    // Blazor may replace the layout header; re-bind when it appears.
    new MutationObserver(() => bindNav()).observe(document.body, { childList: true, subtree: true });

    window.hotjobMotion = {
        refresh() { scan(document); }
    };
})();
