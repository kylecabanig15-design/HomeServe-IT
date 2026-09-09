(() => {
    const visible = element => element.getClientRects().length > 0 && getComputedStyle(element).visibility !== 'hidden';
    const focusable = root => [...root.querySelectorAll('a[href],button,input,select,textarea,[tabindex]')]
        .filter(el => !el.disabled && el.tabIndex >= 0 && visible(el));
    function trap(event, root) {
        const items = focusable(root);
        if (!items.length) { event.preventDefault(); root.focus(); return; }
        const first = items[0], last = items.at(-1);
        if (!root.contains(document.activeElement) || (event.shiftKey && document.activeElement === first)) {
            event.preventDefault(); (event.shiftKey ? last : first).focus();
        } else if (!event.shiftKey && document.activeElement === last) { event.preventDefault(); first.focus(); }
    }
    document.addEventListener('DOMContentLoaded', () => {
        let lastPointerTrigger = null;
        document.addEventListener('pointerdown', event => {
            lastPointerTrigger = event.target.closest('button,a[href],[role="button"]');
        }, true);
        const toggle = document.querySelector('.app-nav-toggle');
        const nav = document.querySelector('#app-navigation');
        const backdrop = document.querySelector('.app-nav-backdrop');
        const main = document.querySelector('.app-main');
        const desktop = matchMedia('(min-width: 1024px)');
        const setNavigation = open => {
            document.body.toggleAttribute('data-nav-open', open);
            toggle?.setAttribute('aria-expanded', String(open));
            toggle?.setAttribute('aria-label', open ? 'Close navigation' : 'Open navigation');
            if (backdrop) backdrop.hidden = !open;
            if (main) main.inert = open;
            if (open) focusable(nav)[0]?.focus(); else toggle?.focus();
        };
        toggle?.addEventListener('click', () => setNavigation(!document.body.hasAttribute('data-nav-open')));
        if (toggle) toggle.disabled = false;
        backdrop?.addEventListener('click', () => setNavigation(false));
        desktop.addEventListener('change', () => { if (document.body.hasAttribute('data-nav-open')) setNavigation(false); });
        nav?.addEventListener('click', event => { if (event.target.closest('a') && !desktop.matches) setNavigation(false); });

        // Keep wide record grids scrollable locally; never hide page-level overflow to mask a layout bug.
        document.querySelectorAll('main table').forEach(table => {
            if (table.parentElement.classList.contains('table-scroll')) return;
            const wrapper = document.createElement('div');
            wrapper.className = 'table-scroll'; wrapper.tabIndex = 0;
            wrapper.setAttribute('role', 'region'); wrapper.setAttribute('aria-label', 'Scrollable records');
            table.before(wrapper); wrapper.append(table);
        });
        let nextId = 0;
        // Associate existing visible labels with their control, including nested styling wrappers.
        document.querySelectorAll('label:not([for])').forEach(label => {
            if (label.querySelector('input,select,textarea')) return;
            const sibling = label.nextElementSibling;
            const control = sibling?.matches('input,select,textarea') ? sibling : sibling?.querySelector('input:not([type="hidden"]),select,textarea');
            if (!control) return;
            control.id ||= 'labelled-control-' + ++nextId;
            label.htmlFor = control.id;
        });
        document.querySelectorAll('input[placeholder],textarea[placeholder]').forEach(input => {
            if (!input.labels?.length && !input.hasAttribute('aria-label')) input.setAttribute('aria-label', input.placeholder);
        });
        // Each dialog's x-show remains its source of truth. This layer manages focus and Escape only.
        const candidates = [...document.querySelectorAll('[x-show]')].filter(el =>
            /^[a-zA-Z][\w]*$/.test(el.getAttribute('x-show')) &&
            (el.getAttribute('role') === 'dialog' || (el.classList.contains('fixed') && el.classList.contains('inset-0'))));
        const dialogs = candidates.filter(el => !candidates.some(parent => parent !== el && parent.contains(el)))
            .map(el => {
                el.dataset.managedDialog = ''; el.setAttribute('role', 'dialog'); el.setAttribute('aria-modal', 'true'); el.tabIndex = -1;
                if (!el.hasAttribute('aria-labelledby')) {
                    const heading = el.querySelector('h1,h2,h3');
                    if (heading) { heading.id ||= 'dialog-heading-' + ++nextId; el.setAttribute('aria-labelledby', heading.id); }
                    else el.setAttribute('aria-label', 'Details');
                }
                return { el, open: false, trigger: null };
            });
        const synchronize = () => dialogs.forEach(dialog => {
            const open = visible(dialog.el);
            if (open === dialog.open) return;
            dialog.open = open;
            if (open) {
                // Safari does not necessarily focus a button when it is clicked.
                dialog.trigger = lastPointerTrigger?.isConnected ? lastPointerTrigger : document.activeElement;
                (focusable(dialog.el)[0] || dialog.el).focus();
            } else if (dialog.trigger?.isConnected) dialog.trigger.focus();
        });
        const observer = new MutationObserver(synchronize);
        dialogs.forEach(dialog => observer.observe(dialog.el, { attributes: true, attributeFilter: ['style'] }));
        synchronize();
        document.addEventListener('keydown', event => {
            if (document.body.hasAttribute('data-nav-open')) {
                if (event.key === 'Escape') { event.preventDefault(); setNavigation(false); }
                else if (event.key === 'Tab') trap(event, nav);
                return;
            }
            const dialog = dialogs.filter(d => d.open).at(-1);
            if (!dialog) return;
            if (event.key === 'Escape') {
                event.preventDefault(); event.stopPropagation();
                window.Alpine.$data(dialog.el)[dialog.el.getAttribute('x-show')] = false;
                const trigger = dialog.trigger;
                requestAnimationFrame(() => trigger?.isConnected && trigger.focus());
            } else if (event.key === 'Tab') trap(event, dialog.el);
        });
    });
})();
