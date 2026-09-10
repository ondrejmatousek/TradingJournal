// Please see documentation at https://learn.microsoft.com/aspnet/core/client-side/bundling-and-minification
// for details on configuring this project to bundle and minify static web assets.

(() => {
    const navigation = document.getElementById('app-navigation');
    const toggle = document.querySelector('[aria-controls="app-navigation"]');
    if (navigation && toggle) {
        navigation.addEventListener('keydown', event => {
            if (event.key === 'Escape' && navigation.classList.contains('show')) {
                bootstrap.Collapse.getOrCreateInstance(navigation).hide();
                toggle.focus();
            }
        });
        navigation.addEventListener('click', event => {
            if (event.target.closest('a') && window.innerWidth < 992)
                bootstrap.Collapse.getOrCreateInstance(navigation).hide();
        });
    }
    // Keep table semantics and localize horizontal scrolling, including tables
    // injected later by the journal, COT and invoice pages.
    function prepareTables() {
        const fieldLabels = { Description: 'Popis', Quantity: 'Množství', Unit: 'Měrná jednotka', UnitPrice: 'Cena za jednotku', VatRate: 'DPH %' };
        document.querySelectorAll('.invoice-item-input > input').forEach(input => {
            const label = fieldLabels[input.name.split('.').pop()];
            if (!label) return;
            const wrapper = document.createElement('label');
            wrapper.className = 'invoice-field-wrapper';
            const title = document.createElement('span');
            title.textContent = label;
            input.before(wrapper);
            wrapper.append(title, input);
            // The existing VAT toggle targets the input itself.
            if (input.hasAttribute('data-vat-only')) wrapper.classList.add('invoice-vat-field');
        });
        document.querySelectorAll('main table').forEach(table => {
            let region = table.closest('.responsive-table-region, .table-responsive, .screening-table, .insiders-table, .journal-trade-table-wrap');
            if (!region) {
                region = document.createElement('div');
                region.className = 'responsive-table-region';
                table.before(region);
                region.append(table);
            }
            if (!region.hasAttribute('tabindex')) {
                region.tabIndex = 0;
                region.setAttribute('role', 'region');
                region.setAttribute('aria-label', table.caption?.textContent || 'Tabulka – posuňte vodorovně pro další sloupce');
            }
        });
    }
    prepareTables();
    const main = document.querySelector('main');
    if (main) new MutationObserver(prepareTables).observe(main, { childList: true, subtree: true });
})();
