/* ============================================================================
   Watchdog UI — клиентская логика.
   Тянет данные из /api/watchdog/* и рендерит таблицы.
   ============================================================================ */

(function () {
    'use strict';

    const API = '/api/watchdog';
    const state = {
        period: 'week',
        services: [],
        stats: null,
        downtimes: []
    };

    // ------------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------------

    function $(id) {
        return document.getElementById(id);
    }

    function formatNumber(n, digits) {
        if (n === null || n === undefined) return '—';
        const d = digits === undefined ? 0 : digits;
        return Number(n).toLocaleString('ru-RU', {
            minimumFractionDigits: d,
            maximumFractionDigits: d
        });
    }

    function formatPercent(n) {
        if (n === null || n === undefined) return '—';
        return Number(n).toFixed(2) + '%';
    }

    function formatDateTime(iso) {
        if (!iso) return '—';
        const d = new Date(iso);
        if (isNaN(d.getTime())) return '—';
        return d.toLocaleString('ru-RU', {
            day: '2-digit',
            month: '2-digit',
            year: 'numeric',
            hour: '2-digit',
            minute: '2-digit',
            second: '2-digit'
        });
    }

    function uptimeClass(percent) {
        if (percent >= 99.5) return 'wd-value-good';
        if (percent >= 95) return 'wd-value-warn';
        return 'wd-value-bad';
    }

    function escapeHtml(s) {
        if (s === null || s === undefined) return '';
        return String(s)
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;')
            .replace(/'/g, '&#39;');
    }

    // ------------------------------------------------------------------------
    // API
    // ------------------------------------------------------------------------

    async function fetchJson(url) {
        const response = await fetch(url, { headers: { 'Accept': 'application/json' } });
        if (!response.ok) {
            throw new Error(`HTTP ${response.status} ${response.statusText} для ${url}`);
        }
        return response.json();
    }

    function statsUrl(period) {
        switch (period) {
            case 'today': return `${API}/stats/today`;
            case 'yesterday': return `${API}/stats/yesterday`;
            case 'week': return `${API}/stats/week`;
            case 'month': return `${API}/stats/month`;
            default: return `${API}/stats/week`;
        }
    }

    async function loadAll() {
        showLoading();
        try {
            const [services, stats] = await Promise.all([
                fetchJson(`${API}/services`),
                fetchJson(statsUrl(state.period))
            ]);

            state.services = services;
            state.stats = stats;

            // Период для downtime = тот же, что и stats.
            const from = stats.from.split('T')[0];
            const to = stats.to.split('T')[0];
            state.downtimes = await fetchJson(`${API}/downtimes?from=${from}&to=${to}`);

            render();
            hideError();
        } catch (err) {
            showError(err.message || String(err));
        } finally {
            hideLoading();
        }
    }

    // ------------------------------------------------------------------------
    // Render
    // ------------------------------------------------------------------------

    function render() {
        renderServices();
        renderOperations();
        renderDowntimes();
    }

    function renderServices() {
        const tbody = $('wd-services-table').querySelector('tbody');
        tbody.innerHTML = '';

        const services = state.stats?.services ?? [];
        if (services.length === 0) {
            $('wd-services-section').style.display = 'none';
            return;
        }

        for (const svc of services) {
            const http = svc.http || {};
            const isUp = http.totalChecks > 0 && http.failedChecks < http.totalChecks;
            const isUnknown = !http.totalChecks;
            const statusClass = isUnknown ? 'unknown' : (isUp ? 'up' : 'down');
            const statusText = isUnknown ? 'Нет данных' : (isUp ? 'UP' : 'DOWN');

            const tr = document.createElement('tr');
            tr.innerHTML = `
                <td>
                    <div class="wd-status ${statusClass}">
                        <span class="wd-status-dot"></span>
                        <span>${escapeHtml(svc.displayName || svc.name)}</span>
                    </div>
                </td>
                <td class="wd-col-num">${formatNumber(http.totalChecks)}</td>
                <td class="wd-col-num">${formatNumber(http.totalChecks - http.failedChecks)}</td>
                <td class="wd-col-num ${uptimeClass(http.uptimePercent)}">${formatPercent(http.uptimePercent)}</td>
                <td class="wd-col-num">${formatNumber(http.avgResponseMs, 1)} мс</td>
                <td class="wd-col-num">${formatNumber(http.totalDowntimeMinutes, 1)} мин</td>
            `;
            tbody.appendChild(tr);
        }

        $('wd-services-section').style.display = '';
    }

    function renderOperations() {
        const container = $('wd-ops-content');
        container.innerHTML = '';

        const services = state.stats?.services ?? [];
        let totalOps = 0;

        for (const svc of services) {
            const ops = svc.operations || {};
            totalOps += ops.totalCount || 0;

            const block = document.createElement('div');
            block.className = 'wd-service-block';

            const head = document.createElement('div');
            head.className = 'wd-service-head';
            head.innerHTML = `
                <span>${escapeHtml(svc.displayName || svc.name)}</span>
                <span class="wd-service-head-meta">
                    Всего: ${formatNumber(ops.totalCount)}
                    · Ошибок: <span class="${ops.failedCount > 0 ? 'wd-value-bad' : ''}">${formatNumber(ops.failedCount)}</span>
                    · Отменено: ${formatNumber(ops.cancelledCount)}
                </span>
            `;
            block.appendChild(head);

            const byType = ops.byType || [];
            if (byType.length === 0) {
                const empty = document.createElement('div');
                empty.className = 'wd-empty';
                empty.textContent = 'Нет данных об операциях за период.';
                block.appendChild(empty);
            } else {
                const wrapper = document.createElement('div');
                wrapper.className = 'wd-table-wrapper';
                const table = document.createElement('table');
                table.className = 'wd-table';
                table.innerHTML = `
                    <thead>
                        <tr>
                            <th>Тип операции</th>
                            <th class="wd-col-num">Всего</th>
                            <th class="wd-col-num">Успешно</th>
                            <th class="wd-col-num">Ошибок</th>
                            <th class="wd-col-num">Отменено</th>
                            <th class="wd-col-num">Ср. мс</th>
                        </tr>
                    </thead>
                    <tbody></tbody>
                `;
                const tbody = table.querySelector('tbody');
                for (const row of byType) {
                    const avgMs = row.totalCount > 0
                        ? row.totalDurationMs / row.totalCount
                        : 0;
                    const tr = document.createElement('tr');
                    tr.innerHTML = `
                        <td>${escapeHtml(row.displayName || row.type)}</td>
                        <td class="wd-col-num">${formatNumber(row.totalCount)}</td>
                        <td class="wd-col-num">${formatNumber(row.successCount)}</td>
                        <td class="wd-col-num ${row.failedCount > 0 ? 'wd-value-bad' : 'wd-value-muted'}">${formatNumber(row.failedCount)}</td>
                        <td class="wd-col-num ${row.cancelledCount > 0 ? 'wd-value-warn' : 'wd-value-muted'}">${formatNumber(row.cancelledCount)}</td>
                        <td class="wd-col-num">${formatNumber(avgMs, 1)}</td>
                    `;
                    tbody.appendChild(tr);
                }
                wrapper.appendChild(table);
                block.appendChild(wrapper);
            }

            container.appendChild(block);
        }

        $('wd-ops-section').style.display = totalOps > 0 || services.length > 0 ? '' : 'none';
    }

    function renderDowntimes() {
        const section = $('wd-downtimes-section');
        const tbody = $('wd-downtimes-table').querySelector('tbody');
        const empty = $('wd-downtimes-empty');
        tbody.innerHTML = '';

        const list = Array.isArray(state.downtimes) ? state.downtimes : [];
        if (list.length === 0) {
            section.style.display = '';
            $('wd-downtimes-table').parentElement.style.display = 'none';
            empty.style.display = '';
            return;
        }

        $('wd-downtimes-table').parentElement.style.display = '';
        empty.style.display = 'none';

        for (const d of list) {
            const tr = document.createElement('tr');

            const rawDuration = d.durationMinutes;
            const duration = rawDuration === null || rawDuration === undefined
                ? '<span class="wd-value-bad">ещё лежит</span>'
                : `${formatNumber(Math.max(0, rawDuration), 1)} мин`;

            tr.innerHTML = `
                <td>${escapeHtml(d.serviceName)}</td>
                <td>${formatDateTime(d.startTime)}</td>
                <td>${d.endTime ? formatDateTime(d.endTime) : '—'}</td>
                <td class="wd-col-num">${duration}</td>
                <td>${escapeHtml(d.reason || '—')}</td>
            `;
            tbody.appendChild(tr);
        }

        section.style.display = '';
    }

    // ------------------------------------------------------------------------
    // UI state
    // ------------------------------------------------------------------------

    function showLoading() {
        $('wd-loading').style.display = '';
    }

    function hideLoading() {
        $('wd-loading').style.display = 'none';
    }

    function showError(message) {
        const el = $('wd-error');
        el.textContent = 'Ошибка загрузки: ' + message;
        el.style.display = '';
    }

    function hideError() {
        $('wd-error').style.display = 'none';
    }

    // ------------------------------------------------------------------------
    // Clock
    // ------------------------------------------------------------------------

    function tickClock() {
        const el = $('wd-clock');
        if (!el) return;
        const now = new Date();
        el.textContent = now.toLocaleString('ru-RU', {
            day: '2-digit',
            month: '2-digit',
            year: 'numeric',
            hour: '2-digit',
            minute: '2-digit',
            second: '2-digit'
        });
    }

    // ------------------------------------------------------------------------
    // Init
    // ------------------------------------------------------------------------

    function bindPeriodButtons() {
        const buttons = document.querySelectorAll('.wd-period-btn');
        buttons.forEach(btn => {
            btn.addEventListener('click', () => {
                buttons.forEach(b => b.classList.remove('active'));
                btn.classList.add('active');
                state.period = btn.getAttribute('data-period');
                loadAll();
            });
        });
    }

    function bindRefresh() {
        const btn = $('wd-refresh');
        if (btn) {
            btn.addEventListener('click', () => loadAll());
        }
    }

    document.addEventListener('DOMContentLoaded', () => {
        tickClock();
        setInterval(tickClock, 1000);
        bindPeriodButtons();
        bindRefresh();
        loadAll();
        // Автообновление раз в минуту.
        setInterval(loadAll, 60000);
    });
})();