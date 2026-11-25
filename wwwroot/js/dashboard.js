/**
 * AUB Digital Academy — Executive Command Center Dashboard JS
 * Client-side enhancements for interactive timeframe filtering & report printing.
 */
document.addEventListener('DOMContentLoaded', function () {
    const timeframeSelect = document.getElementById('timeframeSelect');
    if (timeframeSelect) {
        timeframeSelect.addEventListener('change', function () {
            const selected = this.value;
            console.log('Dashboard timeframe filter selected:', selected);
        });
    }

    const exportBtn = document.getElementById('exportReportBtn');
    if (exportBtn && !exportBtn.getAttribute('onclick')) {
        exportBtn.addEventListener('click', function () {
            window.print();
        });
    }
});
