(() => {
    const collator = new Intl.Collator(undefined, { numeric: true, sensitivity: 'base' });

    function comparable(rawValue) {
        const value = (rawValue ?? '').toString().trim();
        if (!value) return { type: 'empty', value: '' };

        const numberCandidate = value.replace(/[₱$,%\s]/g, '').replace(/,/g, '');
        if (/^-?\d+(\.\d+)?$/.test(numberCandidate)) {
            return { type: 'number', value: Number(numberCandidate) };
        }

        const looksLikeDate = /^\d{4}-\d{2}-\d{2}/.test(value)
            || /\b(?:Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Oct|Nov|Dec)[a-z]*\b/i.test(value)
            || /^\d{1,2}[/-]\d{1,2}[/-]\d{2,4}/.test(value);
        if (looksLikeDate) {
            const timestamp = Date.parse(value.replace(' · ', ' '));
            if (!Number.isNaN(timestamp)) return { type: 'number', value: timestamp };
        }

        return { type: 'text', value };
    }

    function compareValues(leftRaw, rightRaw, direction) {
        const left = comparable(leftRaw);
        const right = comparable(rightRaw);
        if (left.type === 'empty' && right.type !== 'empty') return 1;
        if (right.type === 'empty' && left.type !== 'empty') return -1;

        const result = left.type === 'number' && right.type === 'number'
            ? left.value - right.value
            : collator.compare(left.value.toString(), right.value.toString());
        return direction === 'asc' ? result : -result;
    }

    const sortIcons = {
        none: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="m8 7 4-4 4 4M12 3v18m4-4-4 4-4-4"/></svg>',
        asc: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M12 19V5m-6 6 6-6 6 6"/></svg>',
        desc: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M12 5v14m6-6-6 6-6-6"/></svg>'
    };

    function setIndicator(indicator, direction = 'none') {
        if (indicator) indicator.innerHTML = sortIcons[direction] ?? sortIcons.none;
    }

    function resetHeadings(headings) {
        headings.forEach(heading => {
            setSortState(heading, 'none');
            heading.title = 'Sort ascending';
            setIndicator(heading.querySelector('.table-sort-indicator'));
        });
    }

    function prepareHeading(heading, onSort) {
        if (heading.dataset.sortControlReady === 'true') return;
        heading.dataset.sortControlReady = 'true';
        heading.classList.add('table-sort-heading');
        heading.tabIndex = 0;
        heading.setAttribute('role', heading.matches('th') ? 'columnheader' : 'button');
        setSortState(heading, 'none');
        heading.title = 'Sort ascending';

        const indicator = document.createElement('span');
        indicator.className = 'table-sort-indicator';
        indicator.setAttribute('aria-hidden', 'true');
        setIndicator(indicator);
        heading.appendChild(indicator);

        const activate = () => onSort();
        heading.addEventListener('click', activate);
        heading.addEventListener('keydown', event => {
            if (event.key === 'Enter' || event.key === ' ') {
                event.preventDefault();
                activate();
            }
        });
    }

    function createSortToolbar(anchor, columns, onSort, placement = 'before') {
        if (!columns.length) return { sync: () => {} };

        const toolbar = document.createElement('div');
        toolbar.className = 'table-sort-toolbar';
        toolbar.setAttribute('role', 'group');
        toolbar.setAttribute('aria-label', 'Table sorting controls');

        const label = document.createElement('span');
        label.className = 'table-sort-toolbar__label';
        label.innerHTML = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><path d="M3 6h18M6 12h12m-8 6h4"/></svg><span>Sort records</span>';

        const selectWrap = document.createElement('label');
        selectWrap.className = 'table-sort-toolbar__select-wrap';
        selectWrap.innerHTML = '<span class="sr-only">Sort by column</span>';
        const select = document.createElement('select');
        select.className = 'table-sort-toolbar__select';
        select.setAttribute('aria-label', 'Sort by column');
        columns.forEach(column => {
            const option = document.createElement('option');
            option.value = column.index.toString();
            option.textContent = column.label;
            select.appendChild(option);
        });
        selectWrap.appendChild(select);

        const directions = document.createElement('div');
        directions.className = 'table-sort-toolbar__directions';
        const ascButton = document.createElement('button');
        ascButton.type = 'button';
        ascButton.setAttribute('aria-label', 'Sort ascending');
        ascButton.className = 'table-sort-toolbar__direction';
        ascButton.dataset.direction = 'asc';
        ascButton.innerHTML = `${sortIcons.asc}<span>Ascending</span>`;
        const descButton = document.createElement('button');
        descButton.type = 'button';
        descButton.setAttribute('aria-label', 'Sort descending');
        descButton.className = 'table-sort-toolbar__direction';
        descButton.dataset.direction = 'desc';
        descButton.innerHTML = `${sortIcons.desc}<span>Descending</span>`;
        directions.append(ascButton, descButton);

        toolbar.append(label, selectWrap, directions);
        if (placement === 'prepend') anchor.prepend(toolbar);
        else anchor.before(toolbar);

        let activeDirection = null;
        const apply = direction => {
            activeDirection = direction;
            onSort(Number(select.value), direction);
        };

        select.addEventListener('change', () => apply(activeDirection ?? 'asc'));
        ascButton.addEventListener('click', () => apply('asc'));
        descButton.addEventListener('click', () => apply('desc'));

        return {
            sync(columnIndex, direction) {
                select.value = columnIndex.toString();
                activeDirection = direction;
                [ascButton, descButton].forEach(button => {
                    const isActive = button.dataset.direction === direction;
                    button.setAttribute('aria-pressed', isActive ? 'true' : 'false');
                });
            }
        };
    }

    function enhanceTable(table) {
        if (table.dataset.sortReady === 'true' || table.dataset.sortable === 'false') return;
        const headings = Array.from(table.querySelectorAll(':scope > thead th, :scope > thead td'));
        if (!headings.length) return;
        table.dataset.sortReady = 'true';

        const columns = headings.map((heading, columnIndex) => ({
            heading,
            index: columnIndex,
            label: heading.textContent.trim()
        })).filter(column => {
            const label = column.label.toLowerCase();
            return column.heading.dataset.sortable !== 'false'
                && column.heading.colSpan <= 1
                && label
                && label !== 'action'
                && label !== 'actions';
        });

        let toolbar;
        const applySort = (columnIndex, direction) => {
            const heading = headings[columnIndex];
            if (!heading) return;
            resetHeadings(headings);
            setSortState(heading, direction === 'asc' ? 'ascending' : 'descending');
            heading.title = direction === 'asc' ? 'Sort descending' : 'Sort ascending';
            setIndicator(heading.querySelector('.table-sort-indicator'), direction);

            table.querySelectorAll(':scope > tbody').forEach(body => {
                const allRows = Array.from(body.children).filter(row => row.matches('tr'));
                const sortableRows = allRows.filter(row => !row.hasAttribute('data-sort-fixed')
                    && !Array.from(row.cells).some(cell => cell.colSpan > 1)
                    && row.cells.length > columnIndex);
                const fixedRows = allRows.filter(row => !sortableRows.includes(row));
                sortableRows.sort((left, right) => {
                    const leftCell = left.cells[columnIndex];
                    const rightCell = right.cells[columnIndex];
                    return compareValues(leftCell?.dataset.sortValue ?? leftCell?.textContent, rightCell?.dataset.sortValue ?? rightCell?.textContent, direction);
                });
                [...sortableRows, ...fixedRows].forEach(row => body.appendChild(row));
            });
            toolbar?.sync(columnIndex, direction);
        };

        headings.forEach((heading, columnIndex) => {
            const label = heading.textContent.trim().toLowerCase();
            if (heading.dataset.sortable === 'false' || heading.colSpan > 1 || !label || label === 'action' || label === 'actions') return;

            prepareHeading(heading, () => {
                const direction = heading.dataset.sortDirection === 'ascending' ? 'desc' : 'asc';
                applySort(columnIndex, direction);
            });
        });

        toolbar = createSortToolbar(table, columns, applySort);
    }

    function enhanceSortableGrid(grid) {
        if (grid.dataset.sortReady === 'true') return;
        const headings = Array.from(grid.querySelectorAll('[data-grid-sort]'));
        if (!headings.length) return;
        grid.dataset.sortReady = 'true';

        const columns = headings.map(heading => ({
            heading,
            index: Number(heading.dataset.gridSort),
            label: heading.textContent.trim()
        })).filter(column => Number.isInteger(column.index) && column.heading.dataset.sortable !== 'false');

        let toolbar;
        const applySort = (columnIndex, direction) => {
            const heading = headings.find(candidate => Number(candidate.dataset.gridSort) === columnIndex);
            if (!heading) return;
            resetHeadings(headings);
            setSortState(heading, direction === 'asc' ? 'ascending' : 'descending');
            heading.title = direction === 'asc' ? 'Sort descending' : 'Sort ascending';
            setIndicator(heading.querySelector('.table-sort-indicator'), direction);

            const currentRows = Array.from(grid.querySelectorAll('[data-sort-row]'));
            const parent = currentRows[0]?.parentElement;
            if (!parent || currentRows.some(row => row.parentElement !== parent)) return;
            currentRows.sort((left, right) => {
                const leftCell = left.querySelectorAll('[data-sort-cell]')[columnIndex];
                const rightCell = right.querySelectorAll('[data-sort-cell]')[columnIndex];
                return compareValues(leftCell?.dataset.sortValue ?? leftCell?.textContent, rightCell?.dataset.sortValue ?? rightCell?.textContent, direction);
            });
            currentRows.forEach(row => parent.appendChild(row));
            toolbar?.sync(columnIndex, direction);
        };

        headings.forEach(heading => {
            const columnIndex = Number(heading.dataset.gridSort);
            if (!Number.isInteger(columnIndex) || heading.dataset.sortable === 'false') return;
            prepareHeading(heading, () => {
                const direction = heading.dataset.sortDirection === 'ascending' ? 'desc' : 'asc';
                applySort(columnIndex, direction);
            });
        });

        toolbar = createSortToolbar(grid, columns, applySort, 'prepend');
    }

    function initializeSorting(root = document) {
        root.querySelectorAll('table').forEach(enhanceTable);
        root.querySelectorAll('[data-sort-grid]').forEach(enhanceSortableGrid);
    }

    function setSortState(heading, direction) {
        heading.dataset.sortDirection = direction;
        if (heading.matches('th')) heading.setAttribute('aria-sort', direction);
        else heading.setAttribute('aria-label', heading.textContent.trim() + ': sort ' + direction);
    }

    document.addEventListener('DOMContentLoaded', () => initializeSorting());
})();

(() => {
    const calendarIcon = `
        <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">
            <rect x="3" y="5" width="18" height="16" rx="3"></rect>
            <path d="M8 3v4M16 3v4M3 10h18"></path>
        </svg>`;
    const clockIcon = `
        <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">
            <circle cx="12" cy="12" r="9"></circle><path d="M12 7v5l3 2"></path>
        </svg>`;
    const chevronIcon = `
        <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">
            <path d="m9 18 6-6-6-6"></path>
        </svg>`;

    let activePicker = null;

    function toDateKey(date) {
        const year = date.getFullYear();
        const month = String(date.getMonth() + 1).padStart(2, '0');
        const day = String(date.getDate()).padStart(2, '0');
        return `${year}-${month}-${day}`;
    }

    function fromDateKey(value) {
        if (!value) return null;
        const [year, month, day] = value.split('-').map(Number);
        if (!year || !month || !day) return null;
        const date = new Date(year, month - 1, day);
        return Number.isNaN(date.getTime()) ? null : date;
    }

    function dateLabel(value, format = { month: 'short', day: 'numeric', year: 'numeric' }) {
        const date = fromDateKey(value);
        return date ? date.toLocaleDateString('en-US', format) : '';
    }

    function enhanceDatePicker(input) {
        if (input.dataset.premiumPickerReady === 'true') return;
        input.dataset.premiumPickerReady = 'true';

        const hasTime = input.type === 'datetime-local';
        const wasRequired = input.required;
        const originalId = input.id || `premium-date-${Math.random().toString(36).slice(2, 9)}`;
        const externalLabel = document.querySelector(`label[for="${CSS.escape(originalId)}"]`);
        const label = input.dataset.pickerLabel || externalLabel?.textContent?.trim() || (hasTime ? 'Date and time' : 'Date');
        const placeholder = input.dataset.pickerPlaceholder || (hasTime ? 'Select date and time' : 'Select a date');
        const futureOnly = hasTime && /scheduledDate|proposedDeadline/i.test(input.name || '');
        const todayKey = toDateKey(new Date());
        const minimumDate = input.min ? input.min.slice(0, 10) : (futureOnly ? todayKey : '');

        input.id = originalId;
        input.required = false;
        input.classList.add('premium-date-native');
        input.tabIndex = -1;
        input.setAttribute('aria-hidden', 'true');

        let host = input.closest('.ui-date-control');
        if (!host) {
            host = document.createElement('div');
            host.className = 'ui-date-control';
            input.parentNode.insertBefore(host, input);
            host.appendChild(input);
        }
        host.classList.add('premium-date-picker');
        host.querySelectorAll('.ui-date-control__icon').forEach(icon => icon.remove());

        const trigger = document.createElement('button');
        trigger.type = 'button';
        trigger.id = `${originalId}-trigger`;
        trigger.className = 'premium-date-trigger';
        trigger.setAttribute('aria-haspopup', 'dialog');
        trigger.setAttribute('aria-expanded', 'false');
        if (input.getAttribute('aria-describedby')) trigger.setAttribute('aria-describedby', input.getAttribute('aria-describedby'));
        trigger.innerHTML = `
            <span class="premium-date-trigger__icon">${calendarIcon}</span>
            <span class="premium-date-trigger__copy">
                <span class="premium-date-trigger__label"></span>
                <span class="premium-date-trigger__value">
                    <span data-picker-date></span>
                    <span class="premium-date-trigger__separator" data-picker-separator>·</span>
                    <span data-picker-time></span>
                </span>
            </span>
            <span class="premium-date-trigger__end">
                <span class="premium-date-trigger__clock" data-picker-clock>${clockIcon}</span>
                <span class="premium-date-trigger__chevron">${chevronIcon}</span>
            </span>`;
        host.appendChild(trigger);

        if (externalLabel) externalLabel.setAttribute('for', trigger.id);

        const popover = document.createElement('section');
        popover.id = `${originalId}-popover`;
        popover.className = 'premium-date-popover';
        popover.classList.toggle('has-time', hasTime);
        popover.hidden = true;
        popover.setAttribute('role', 'dialog');
        popover.setAttribute('aria-modal', 'false');
        popover.setAttribute('aria-label', `Choose ${label.toLowerCase()}`);
        trigger.setAttribute('aria-controls', popover.id);
        popover.innerHTML = `
            <div class="premium-date-popover__handle" aria-hidden="true"></div>
            <header class="premium-date-popover__header">
                <div class="premium-date-popover__title">
                    <span class="premium-date-popover__kicker">${hasTime ? 'Schedule' : 'Calendar'}</span>
                    <strong data-picker-month></strong>
                </div>
                <div class="premium-date-popover__nav" aria-label="Calendar navigation">
                    <button type="button" data-picker-previous aria-label="Previous month">${chevronIcon}</button>
                    <button type="button" data-picker-next aria-label="Next month">${chevronIcon}</button>
                </div>
            </header>
            <div class="premium-date-quick" aria-label="Quick date choices">
                <button type="button" data-picker-offset="0">Today</button>
                <button type="button" data-picker-offset="1">Tomorrow</button>
                <button type="button" data-picker-offset="7">Next week</button>
            </div>
            <div class="premium-date-weekdays" aria-hidden="true">
                <span>Su</span><span>Mo</span><span>Tu</span><span>We</span><span>Th</span><span>Fr</span><span>Sa</span>
            </div>
            <div class="premium-date-grid" data-picker-grid></div>
            <div class="premium-time-panel" data-picker-time-panel ${hasTime ? '' : 'hidden'}>
                <div class="premium-time-panel__heading">
                    <span>${clockIcon}</span>
                    <div><strong>Preferred time</strong><small>Philippine Time · UTC+8</small></div>
                </div>
                <div class="premium-time-controls">
                    <label><span>Hour</span><select data-native-select="true" data-picker-hour aria-label="Hour"></select></label>
                    <span class="premium-time-colon" aria-hidden="true">:</span>
                    <label><span>Minute</span><select data-native-select="true" data-picker-minute aria-label="Minute"></select></label>
                    <label class="premium-time-period"><span>Period</span><select data-native-select="true" data-picker-period aria-label="AM or PM"><option>AM</option><option>PM</option></select></label>
                </div>
            </div>
            <p class="premium-date-error" data-picker-error role="alert" hidden></p>
            <footer class="premium-date-popover__footer">
                <div class="premium-date-selection">
                    <span>Selected</span><strong data-picker-summary>No date selected</strong>
                </div>
                <button type="button" class="premium-date-clear" data-picker-clear>Clear</button>
                <button type="button" class="premium-date-done" data-picker-done>Done</button>
            </footer>`;
        document.body.appendChild(popover);

        const monthTitle = popover.querySelector('[data-picker-month]');
        const grid = popover.querySelector('[data-picker-grid]');
        const summary = popover.querySelector('[data-picker-summary]');
        const error = popover.querySelector('[data-picker-error]');
        const hourSelect = popover.querySelector('[data-picker-hour]');
        const minuteSelect = popover.querySelector('[data-picker-minute]');
        const periodSelect = popover.querySelector('[data-picker-period]');
        const clearButton = popover.querySelector('[data-picker-clear]');
        const dateText = trigger.querySelector('[data-picker-date]');
        const timeText = trigger.querySelector('[data-picker-time]');
        const separator = trigger.querySelector('[data-picker-separator]');
        const triggerLabel = trigger.querySelector('.premium-date-trigger__label');
        const triggerClock = trigger.querySelector('[data-picker-clock]');
        clearButton.hidden = wasRequired;

        for (let hour = 1; hour <= 12; hour++) hourSelect?.add(new Option(String(hour).padStart(2, '0'), String(hour)));
        for (let minute = 0; minute < 60; minute += 5) {
            const minuteText = String(minute).padStart(2, '0');
            minuteSelect?.add(new Option(minuteText, minuteText));
        }

        let draftDate = '';
        let draftHour = 9;
        let draftMinute = '00';
        let draftPeriod = 'AM';
        let viewMonth = new Date(new Date().getFullYear(), new Date().getMonth(), 1);

        function readInputValue() {
            const [datePart, timePart = ''] = (input.value || '').split('T');
            draftDate = datePart || '';
            if (datePart) {
                const parsedDate = fromDateKey(datePart);
                if (parsedDate) viewMonth = new Date(parsedDate.getFullYear(), parsedDate.getMonth(), 1);
            } else {
                const now = new Date();
                viewMonth = new Date(now.getFullYear(), now.getMonth(), 1);
            }

            if (hasTime && timePart) {
                const [rawHour, rawMinute = '00'] = timePart.split(':');
                const hour24 = Number(rawHour);
                draftPeriod = hour24 >= 12 ? 'PM' : 'AM';
                draftHour = hour24 % 12 || 12;
                draftMinute = rawMinute.slice(0, 2);
                if (minuteSelect && !Array.from(minuteSelect.options).some(option => option.value === draftMinute)) {
                    minuteSelect.add(new Option(draftMinute, draftMinute));
                }
            } else if (hasTime) {
                const suggestedTime = new Date(Date.now() + 60 * 60 * 1000);
                suggestedTime.setMinutes(Math.ceil(suggestedTime.getMinutes() / 5) * 5, 0, 0);
                const suggestedHour = suggestedTime.getHours();
                draftHour = suggestedHour % 12 || 12;
                draftMinute = String(suggestedTime.getMinutes()).padStart(2, '0');
                draftPeriod = suggestedHour >= 12 ? 'PM' : 'AM';
            }

            if (hourSelect) {
                hourSelect.value = String(draftHour);
                hourSelect._premiumSelect?.refresh();
            }
            if (minuteSelect) {
                minuteSelect.value = draftMinute;
                minuteSelect._premiumSelect?.refresh();
            }
            if (periodSelect) {
                periodSelect.value = draftPeriod;
                periodSelect._premiumSelect?.refresh();
            }
        }

        function draftTimeLabel() {
            return `${draftHour}:${draftMinute} ${draftPeriod}`;
        }

        function updateSummary() {
            summary.textContent = draftDate
                ? `${dateLabel(draftDate)}${hasTime ? ` · ${draftTimeLabel()}` : ''}`
                : 'No date selected';
        }

        function showError(message = '') {
            error.hidden = !message;
            error.textContent = message;
            trigger.classList.toggle('is-invalid', Boolean(message));
            trigger.setAttribute('aria-invalid', message ? 'true' : 'false');
        }

        function renderCalendar() {
            monthTitle.textContent = viewMonth.toLocaleDateString('en-US', { month: 'long', year: 'numeric' });
            grid.replaceChildren();
            const year = viewMonth.getFullYear();
            const month = viewMonth.getMonth();
            const firstWeekday = new Date(year, month, 1).getDay();

            for (let index = 0; index < 42; index++) {
                const date = new Date(year, month, index - firstWeekday + 1);
                const key = toDateKey(date);
                const isOutside = date.getMonth() !== month;
                const isSelected = key === draftDate;
                const isToday = key === todayKey;
                const isDisabled = Boolean(minimumDate && key < minimumDate);
                const day = document.createElement('button');
                day.type = 'button';
                day.className = 'premium-date-day';
                day.dataset.date = key;
                day.disabled = isDisabled;
                day.setAttribute('aria-label', date.toLocaleDateString('en-US', { weekday: 'long', month: 'long', day: 'numeric', year: 'numeric' }));
                day.setAttribute('aria-pressed', isSelected ? 'true' : 'false');
                day.classList.toggle('is-outside', isOutside);
                day.classList.toggle('is-selected', isSelected);
                day.classList.toggle('is-today', isToday);
                day.innerHTML = `<span>${date.getDate()}</span><i aria-hidden="true"></i>`;
                day.addEventListener('click', () => {
                    draftDate = key;
                    viewMonth = new Date(date.getFullYear(), date.getMonth(), 1);
                    showError();
                    renderCalendar();
                    updateSummary();
                });
                grid.appendChild(day);
            }
        }

        function updateTrigger() {
            const [datePart, timePart = ''] = (input.value || '').split('T');
            const selectedDateLabel = dateLabel(datePart, { month: 'short', day: 'numeric', year: 'numeric' });
            triggerLabel.textContent = label;
            trigger.classList.toggle('has-value', Boolean(datePart));
            dateText.textContent = selectedDateLabel || placeholder;
            if (hasTime && timePart) {
                const [hourText, minuteText = '00'] = timePart.split(':');
                const hour24 = Number(hourText);
                const period = hour24 >= 12 ? 'PM' : 'AM';
                timeText.textContent = `${hour24 % 12 || 12}:${minuteText.slice(0, 2)} ${period}`;
            } else {
                timeText.textContent = '';
            }
            separator.hidden = !hasTime || !datePart || !timePart;
            triggerClock.hidden = !hasTime;
        }

        function positionPopover() {
            if (popover.hidden) return;
            const viewportPadding = 12;
            if (window.innerWidth <= 560) {
                popover.style.left = `${viewportPadding}px`;
                popover.style.right = `${viewportPadding}px`;
                popover.style.width = 'auto';
                popover.style.top = 'auto';
                popover.style.bottom = `${viewportPadding}px`;
                return;
            }

            popover.style.right = 'auto';
            popover.style.bottom = 'auto';
            popover.style.width = hasTime && window.innerWidth > 700 ? '560px' : '380px';
            const triggerBounds = trigger.getBoundingClientRect();
            const popoverBounds = popover.getBoundingClientRect();
            const left = Math.min(Math.max(viewportPadding, triggerBounds.left), window.innerWidth - popoverBounds.width - viewportPadding);
            const roomBelow = window.innerHeight - triggerBounds.bottom;
            const top = roomBelow >= popoverBounds.height + 12
                ? triggerBounds.bottom + 10
                : Math.max(viewportPadding, triggerBounds.top - popoverBounds.height - 10);
            popover.style.left = `${left}px`;
            popover.style.top = `${top}px`;
        }

        function closePicker(returnFocus = false) {
            if (popover.hidden) return;
            popover.hidden = true;
            popover.classList.remove('is-open');
            trigger.setAttribute('aria-expanded', 'false');
            trigger.classList.remove('is-open');
            if (activePicker?.popover === popover) activePicker = null;
            if (returnFocus) trigger.focus();
        }

        function openPicker() {
            if (activePicker && activePicker.popover !== popover) activePicker.close();
            readInputValue();
            renderCalendar();
            updateSummary();
            showError();
            popover.hidden = false;
            popover.classList.add('is-open');
            trigger.classList.add('is-open');
            trigger.setAttribute('aria-expanded', 'true');
            activePicker = { popover, trigger, close: closePicker, position: positionPopover };
            requestAnimationFrame(() => {
                positionPopover();
                popover.querySelector('.premium-date-day.is-selected:not(:disabled), .premium-date-day.is-today:not(:disabled), .premium-date-day:not(:disabled)')?.focus({ preventScroll: true });
            });
        }

        function commitValue() {
            if (!draftDate) {
                showError('Choose a date to continue.');
                return;
            }
            if (minimumDate && draftDate < minimumDate) {
                showError(`Choose ${dateLabel(minimumDate)} or a later date.`);
                return;
            }

            let value = draftDate;
            if (hasTime) {
                const hour12 = Number(hourSelect.value);
                const hour24 = periodSelect.value === 'PM' ? (hour12 % 12) + 12 : hour12 % 12;
                draftHour = hour12;
                draftMinute = minuteSelect.value;
                draftPeriod = periodSelect.value;
                value += `T${String(hour24).padStart(2, '0')}:${draftMinute}`;

                if (futureOnly && new Date(value).getTime() <= Date.now() + 60000) {
                    showError('Choose a time at least a few minutes from now.');
                    return;
                }
            }

            input.value = value;
            input.dispatchEvent(new Event('input', { bubbles: true }));
            input.dispatchEvent(new Event('change', { bubbles: true }));
            updateTrigger();
            showError();
            closePicker(true);
        }

        trigger.addEventListener('click', () => popover.hidden ? openPicker() : closePicker());
        popover.querySelector('[data-picker-previous]').addEventListener('click', () => {
            viewMonth = new Date(viewMonth.getFullYear(), viewMonth.getMonth() - 1, 1);
            renderCalendar();
        });
        popover.querySelector('[data-picker-next]').addEventListener('click', () => {
            viewMonth = new Date(viewMonth.getFullYear(), viewMonth.getMonth() + 1, 1);
            renderCalendar();
        });
        popover.querySelectorAll('[data-picker-offset]').forEach(button => button.addEventListener('click', () => {
            const date = new Date();
            date.setDate(date.getDate() + Number(button.dataset.pickerOffset));
            draftDate = toDateKey(date);
            viewMonth = new Date(date.getFullYear(), date.getMonth(), 1);
            showError();
            renderCalendar();
            updateSummary();
        }));
        [hourSelect, minuteSelect, periodSelect].filter(Boolean).forEach(select => select.addEventListener('change', () => {
            draftHour = Number(hourSelect.value);
            draftMinute = minuteSelect.value;
            draftPeriod = periodSelect.value;
            updateSummary();
        }));
        popover.querySelector('[data-picker-done]').addEventListener('click', commitValue);
        clearButton.addEventListener('click', () => {
            input.value = '';
            input.dispatchEvent(new Event('input', { bubbles: true }));
            input.dispatchEvent(new Event('change', { bubbles: true }));
            updateTrigger();
            closePicker(true);
        });
        popover.addEventListener('click', event => event.stopPropagation());
        input.addEventListener('input', updateTrigger);
        input.addEventListener('change', updateTrigger);
        input.addEventListener('premium-date-sync', () => requestAnimationFrame(updateTrigger));

        input.form?.addEventListener('submit', event => {
            if (!wasRequired || input.value) return;
            event.preventDefault();
            openPicker();
            showError(hasTime ? 'Choose a date and time before continuing.' : 'Choose a date before continuing.');
        });
        input.form?.addEventListener('reset', () => setTimeout(updateTrigger));

        updateTrigger();
    }

    function initializePremiumDatePickers(root = document) {
        root.querySelectorAll('input.ui-date-input[type="date"], input.ui-date-input[type="datetime-local"]').forEach(enhanceDatePicker);
    }

    document.addEventListener('pointerdown', event => {
        if (!activePicker) return;
        if (activePicker.popover.contains(event.target) || activePicker.trigger.contains(event.target)) return;
        const menu = event.target.closest('.premium-select-menu');
        if (menu && activePicker.popover.querySelector(`[aria-controls="${menu.id}"]`)) return;
        activePicker.close();
    });
    document.addEventListener('keydown', event => {
        if (event.key === 'Escape' && activePicker) activePicker.close(true);
    });
    window.addEventListener('resize', () => activePicker?.position());
    window.addEventListener('scroll', event => {
        if (!activePicker || activePicker.popover.contains(event.target)) return;
        if (event.target.nodeType === 1) {
            const menu = event.target.closest('.premium-select-menu');
            if (menu && activePicker.popover.querySelector(`[aria-controls="${menu.id}"]`)) return;
        }
        activePicker.close();
    }, true);
    document.addEventListener('DOMContentLoaded', () => initializePremiumDatePickers());
})();

(() => {
    let activeSelect = null;
    let selectSequence = 0;
    const instances = new Set();

    const icons = {
        chevron: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round"><path d="m7 10 5 5 5-5"/></svg>',
        check: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.6" stroke-linecap="round" stroke-linejoin="round"><path d="m5 12 4 4L19 6"/></svg>',
        search: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><circle cx="11" cy="11" r="7"/><path d="m20 20-3.6-3.6"/></svg>',
        user: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round"><circle cx="12" cy="8" r="4"/><path d="M4 21a8 8 0 0 1 16 0"/></svg>',
        technician: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><path d="M14.7 6.3a1 1 0 0 0 0 1.4l1.6 1.6a1 1 0 0 0 1.4 0l3-3a6 6 0 0 1-7.8 7.8l-5.6 5.6a2 2 0 0 1-2.8-2.8l5.6-5.6a6 6 0 0 1 7.8-7.8Z"/></svg>',
        package: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linejoin="round"><path d="m12 3 9 4.5-9 4.5-9-4.5L12 3Z"/><path d="m3 7.5 9 4.5 9-4.5V17l-9 4-9-4V7.5Z"/><path d="M12 12v9"/></svg>',
        device: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><rect x="3" y="4" width="18" height="13" rx="2"/><path d="M8 21h8M12 17v4"/></svg>',
        phone: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round"><rect x="5" y="2" width="14" height="20" rx="3"/><path d="M9 5h6M11 18h2"/></svg>',
        shield: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linejoin="round"><path d="M12 3 4 6v5c0 5 3.4 8.6 8 10 4.6-1.4 8-5 8-10V6l-8-3Z"/><path d="m9 12 2 2 4-4"/></svg>',
        status: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round"><circle cx="12" cy="12" r="9"/><path d="m8 12 2.5 2.5L16 9"/></svg>',
        globe: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round"><circle cx="12" cy="12" r="9"/><path d="M3 12h18M12 3a15 15 0 0 1 0 18M12 3a15 15 0 0 0 0 18"/></svg>',
        currency: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round"><circle cx="12" cy="12" r="9"/><path d="M14.5 8.5c-.6-.6-1.4-1-2.5-1-1.7 0-3 1-3 2.3 0 3.7 6 1.4 6 5 0 1.5-1.4 2.7-3.3 2.7-1.2 0-2.3-.4-3.1-1.2M12 5.5v13"/></svg>',
        sort: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round"><path d="M3 6h18M6 12h12m-8 6h4"/></svg>',
        clock: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round"><circle cx="12" cy="12" r="9"/><path d="M12 7v5l3 2"/></svg>',
        generic: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><path d="M8 9h8M8 15h8"/><rect x="3" y="4" width="18" height="16" rx="3"/></svg>'
    };

    function iconFor(select, option) {
        const key = `${select.name} ${select.id} ${Array.from(select.attributes).map(attribute => attribute.name).join(' ')}`.toLowerCase();
        const value = (option?.value ?? '').toLowerCase();
        if (key.includes('tech')) return icons.technician;
        if (key.includes('customer')) return icons.user;
        if (key.includes('item')) return icons.package;
        if (key.includes('device')) return value.includes('phone') ? icons.phone : icons.device;
        if (key.includes('role')) return icons.shield;
        if (key.includes('status')) return icons.status;
        if (key.includes('timezone')) return icons.globe;
        if (key.includes('currency')) return icons.currency;
        if (key.includes('sort') || select.classList.contains('table-sort-toolbar__select')) return icons.sort;
        if (key.includes('picker-hour') || key.includes('picker-minute') || key.includes('picker-period')) return icons.clock;
        return icons.generic;
    }

    function optionParts(text) {
        const value = (text ?? '').replace(/\s+/g, ' ').trim();
        const middleDot = value.split(' · ');
        if (middleDot.length > 1) return { title: middleDot.shift(), meta: middleDot.join(' · ') };
        const parenthetical = value.match(/^(.*?)\s*\(([^()]*)\)$/);
        if (parenthetical) return { title: parenthetical[1].trim(), meta: parenthetical[2].trim() };
        return { title: value, meta: '' };
    }

    function enhanceSelect(select) {
        if (!(select instanceof HTMLSelectElement)
            || select.dataset.premiumSelectReady === 'true'
            || select.multiple
            || Number(select.getAttribute('size') || 1) > 1
            || select.dataset.nativeSelect === 'true') return;

        select.dataset.premiumSelectReady = 'true';
        const id = `premium-select-${++selectSequence}`;
        const originalRect = select.getBoundingClientRect();
        const originalStyles = getComputedStyle(select);
        const wrapper = document.createElement('div');
        wrapper.className = 'premium-select';
        const hasExplicitWidth = Array.from(select.classList).some(className => /^!?w-\[/.test(className));
        if (select.classList.contains('!w-auto') || hasExplicitWidth) {
            wrapper.classList.add('premium-select--auto');
            if (originalRect.width) wrapper.style.width = `${Math.ceil(originalRect.width)}px`;
        } else {
            wrapper.style.width = '100%';
            if (originalStyles.maxWidth && originalStyles.maxWidth !== 'none') wrapper.style.maxWidth = originalStyles.maxWidth;
        }
        if (select.classList.contains('text-xs')
            || select.classList.contains('text-[12px]')
            || select.matches('[data-picker-hour], [data-picker-minute], [data-picker-period]')) {
            wrapper.classList.add('premium-select--compact');
        }

        select.before(wrapper);
        wrapper.appendChild(select);
        select.classList.add('premium-select__native');
        select.tabIndex = -1;

        const trigger = document.createElement('button');
        trigger.type = 'button';
        trigger.className = 'premium-select__trigger';
        trigger.setAttribute('aria-haspopup', 'listbox');
        trigger.setAttribute('aria-expanded', 'false');
        trigger.setAttribute('aria-controls', `${id}-menu`);
        trigger.innerHTML = `
            <span class="premium-select__leading" data-select-leading aria-hidden="true"></span>
            <span class="premium-select__value" data-select-value></span>
            <span class="premium-select__chevron" aria-hidden="true">${icons.chevron}</span>`;
        wrapper.appendChild(trigger);

        const menu = document.createElement('div');
        menu.id = `${id}-menu`;
        menu.className = 'premium-select-menu';
        menu.setAttribute('role', 'listbox');
        menu.setAttribute('aria-label', select.getAttribute('aria-label') || select.name || 'Select an option');
        menu.hidden = true;
        menu.innerHTML = `
            <div class="premium-select-menu__search" data-select-search-wrap hidden>
                <span aria-hidden="true">${icons.search}</span>
                <input type="search" autocomplete="off" spellcheck="false" placeholder="Search options…" aria-label="Search options" data-select-search />
            </div>
            <div class="premium-select-menu__options" data-select-options></div>
            <div class="premium-select-menu__empty" data-select-empty hidden>No matching options</div>`;
        document.body.appendChild(menu);

        const valueElement = trigger.querySelector('[data-select-value]');
        const leadingElement = trigger.querySelector('[data-select-leading]');
        const searchWrap = menu.querySelector('[data-select-search-wrap]');
        const searchInput = menu.querySelector('[data-select-search]');
        const optionsElement = menu.querySelector('[data-select-options]');
        const emptyElement = menu.querySelector('[data-select-empty]');
        let validationShown = false;

        function selectableOptions() {
            return Array.from(select.options).filter(option => !option.hidden
                && !(select.required && option.value === ''));
        }

        function sync() {
            const selected = select.selectedOptions[0] || select.options[0];
            const parts = optionParts(selected?.textContent || 'Select an option');
            valueElement.textContent = parts.title || 'Select an option';
            valueElement.title = selected?.textContent?.trim() || '';
            leadingElement.innerHTML = iconFor(select, selected);
            const isPlaceholder = !selected || (select.required && selected.value === '');
            wrapper.classList.toggle('is-placeholder', isPlaceholder);
            wrapper.classList.toggle('is-disabled', select.disabled);
            wrapper.classList.toggle('is-invalid', validationShown && !select.validity.valid);
            trigger.disabled = select.disabled;
            trigger.setAttribute('aria-required', select.required ? 'true' : 'false');
        }

        function renderOptions(query = '') {
            const normalizedQuery = query.trim().toLowerCase();
            optionsElement.replaceChildren();
            const options = selectableOptions().filter(option => !normalizedQuery
                || option.textContent.toLowerCase().includes(normalizedQuery));

            options.forEach(option => {
                const parts = optionParts(option.textContent);
                const button = document.createElement('button');
                button.type = 'button';
                button.className = 'premium-select-option';
                button.dataset.value = option.value;
                button.setAttribute('role', 'option');
                button.setAttribute('aria-selected', option.selected ? 'true' : 'false');
                button.disabled = option.disabled;
                button.innerHTML = `
                    <span class="premium-select-option__icon" aria-hidden="true">${iconFor(select, option)}</span>
                    <span class="premium-select-option__copy">
                        <strong></strong>
                        <small ${parts.meta ? '' : 'hidden'}></small>
                    </span>
                    <span class="premium-select-option__check" aria-hidden="true">${icons.check}</span>`;
                button.querySelector('strong').textContent = parts.title;
                button.querySelector('small').textContent = parts.meta;
                button.addEventListener('click', () => {
                    if (option.disabled) return;
                    select.value = option.value;
                    select.dispatchEvent(new Event('input', { bubbles: true }));
                    select.dispatchEvent(new Event('change', { bubbles: true }));
                    sync();
                    close(true);
                });
                optionsElement.appendChild(button);
            });
            emptyElement.hidden = options.length > 0;
        }

        function position() {
            if (menu.hidden) return;
            const rect = trigger.getBoundingClientRect();
            const viewportPadding = 12;
            const menuWidth = Math.min(
                Math.max(rect.width, selectableOptions().length > 6 ? 310 : 250),
                window.innerWidth - viewportPadding * 2);
            const availableBelow = window.innerHeight - rect.bottom - viewportPadding;
            const availableAbove = rect.top - viewportPadding;
            const placeAbove = availableBelow < 220 && availableAbove > availableBelow;
            const availableHeight = Math.max(150, Math.min(340, placeAbove ? availableAbove - 8 : availableBelow - 8));
            let left = Math.min(rect.left, window.innerWidth - menuWidth - viewportPadding);
            left = Math.max(viewportPadding, left);
            menu.style.width = `${menuWidth}px`;
            menu.style.left = `${left}px`;
            menu.style.maxHeight = `${availableHeight}px`;
            menu.dataset.placement = placeAbove ? 'top' : 'bottom';
            if (placeAbove) {
                menu.style.top = 'auto';
                menu.style.bottom = `${window.innerHeight - rect.top + 7}px`;
            } else {
                menu.style.bottom = 'auto';
                menu.style.top = `${rect.bottom + 7}px`;
            }
        }

        function focusSelectedOption() {
            const selected = optionsElement.querySelector('[aria-selected="true"]');
            const first = optionsElement.querySelector('.premium-select-option:not(:disabled)');
            (selected || first)?.focus({ preventScroll: true });
            (selected || first)?.scrollIntoView({ block: 'nearest' });
        }

        function open() {
            if (select.disabled) return;
            activeSelect?.close();
            sync();
            const options = selectableOptions();
            const hasSearch = options.length > 6;
            searchWrap.hidden = !hasSearch;
            searchInput.value = '';
            renderOptions();
            menu.hidden = false;
            wrapper.classList.add('is-open');
            trigger.setAttribute('aria-expanded', 'true');
            activeSelect = instance;
            position();
            requestAnimationFrame(() => {
                position();
                if (hasSearch) searchInput.focus({ preventScroll: true });
                else focusSelectedOption();
            });
        }

        function close(restoreFocus = false) {
            if (menu.hidden) return;
            menu.hidden = true;
            wrapper.classList.remove('is-open');
            trigger.setAttribute('aria-expanded', 'false');
            if (activeSelect === instance) activeSelect = null;
            if (restoreFocus) trigger.focus({ preventScroll: true });
        }

        function moveOptionFocus(direction) {
            const buttons = Array.from(optionsElement.querySelectorAll('.premium-select-option:not(:disabled)'));
            if (!buttons.length) return;
            const currentIndex = buttons.indexOf(document.activeElement);
            const nextIndex = direction === 'first' ? 0
                : direction === 'last' ? buttons.length - 1
                : Math.max(0, Math.min(buttons.length - 1, currentIndex + direction));
            buttons[nextIndex].focus({ preventScroll: true });
            buttons[nextIndex].scrollIntoView({ block: 'nearest' });
        }

        const instance = { wrapper, trigger, menu, sync, close, position, refresh() {
            sync();
            if (!menu.hidden) renderOptions(searchInput.value);
        }};
        select._premiumSelect = instance;
        instances.add(instance);

        trigger.addEventListener('click', () => menu.hidden ? open() : close());
        trigger.addEventListener('keydown', event => {
            if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
                event.preventDefault();
                if (menu.hidden) open();
                requestAnimationFrame(() => moveOptionFocus(event.key === 'ArrowDown' ? 'first' : 'last'));
            } else if (event.key === 'Escape') close(true);
        });
        searchInput.addEventListener('input', () => renderOptions(searchInput.value));
        searchInput.addEventListener('keydown', event => {
            if (event.key === 'ArrowDown') {
                event.preventDefault();
                moveOptionFocus('first');
            } else if (event.key === 'Escape') {
                event.preventDefault();
                close(true);
            }
        });
        menu.addEventListener('keydown', event => {
            if (event.target === searchInput) return;
            if (event.key === 'ArrowDown') {
                event.preventDefault();
                moveOptionFocus(1);
            } else if (event.key === 'ArrowUp') {
                event.preventDefault();
                moveOptionFocus(-1);
            } else if (event.key === 'Home') {
                event.preventDefault();
                moveOptionFocus('first');
            } else if (event.key === 'End') {
                event.preventDefault();
                moveOptionFocus('last');
            } else if (event.key === 'Escape') {
                event.preventDefault();
                close(true);
            } else if (event.key === 'Tab') close();
        });
        select.addEventListener('input', sync);
        select.addEventListener('change', () => {
            validationShown = true;
            sync();
        });
        select.addEventListener('invalid', event => {
            event.preventDefault();
            validationShown = true;
            sync();
            open();
        });
        select.form?.addEventListener('reset', () => {
            validationShown = false;
            requestAnimationFrame(sync);
        });
        sync();
    }

    function initializeSelects(root = document) {
        if (root instanceof HTMLSelectElement) enhanceSelect(root);
        root.querySelectorAll?.('select').forEach(enhanceSelect);
    }

    function syncAll() {
        instances.forEach(instance => {
            if (instance.wrapper.isConnected) instance.sync();
        });
    }

    document.addEventListener('DOMContentLoaded', () => {
        initializeSelects();
        const observer = new MutationObserver(records => {
            const selectsToRefresh = new Set();
            records.forEach(record => {
                record.addedNodes.forEach(node => {
                    if (node.nodeType === Node.ELEMENT_NODE) initializeSelects(node);
                });
                const owner = record.target instanceof HTMLSelectElement
                    ? record.target
                    : record.target.parentElement?.closest?.('select');
                if (owner?._premiumSelect) selectsToRefresh.add(owner);
            });
            selectsToRefresh.forEach(select => select._premiumSelect.refresh());
        });
        observer.observe(document.body, { childList: true, subtree: true, characterData: true });
    });

    document.addEventListener('pointerdown', event => {
        if (!activeSelect) return;
        if (!activeSelect.menu.contains(event.target) && !activeSelect.wrapper.contains(event.target)) activeSelect.close();
    });
    document.addEventListener('click', () => requestAnimationFrame(syncAll));
    document.addEventListener('keydown', event => {
        if (event.key === 'Escape') activeSelect?.close(true);
    });
    window.addEventListener('resize', () => activeSelect?.position());
    window.addEventListener('scroll', event => {
        if (activeSelect && !activeSelect.menu.contains(event.target)) activeSelect.close();
    }, true);
})();
