(() => {
    'use strict';
    const root = document.getElementById('price-lab');
    if (!root) return;
    const $ = id => document.getElementById(id);
    const base = document.getElementById('screening-app').dataset.base;
    const token = document.querySelector('#screening-app [name="__RequestVerificationToken"]').value;
    const esc = value => String(value ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
    const num = (n, digits = 2) => n == null ? '—' : new Intl.NumberFormat('cs-CZ', { maximumFractionDigits: digits }).format(n);
    const money = n => n == null ? '—' : `${num(n)} USD`;
    const sourceLink = source => { try { const url = new URL(source); if (url.protocol === "https:" && !url.username && !url.password) return `<a href="${esc(url.href)}" target="_blank" rel="noopener noreferrer">${esc(source)}</a>`; } catch {} return esc(source); };
    const fields = ['requiredReturnPercent', 'growthPercent', 'marginPercent', 'beta', 'forwardEps', 'referencePe', 'sourceUrl', 'sourceDate'];
    let rows = [], current = null, busy = false;
    async function request(path, body) {
        const response = await fetch(base + path, { method: body === undefined ? 'GET' : 'POST', headers: body === undefined ? {} : { 'Content-Type': 'application/json', RequestVerificationToken: token }, body: body === undefined ? undefined : JSON.stringify(body) });
        if (!response.ok) {
            const text = await response.text();
            throw new Error(text.length < 500 && !text.includes('<') ? text : `Požadavek selhal (${response.status}).`);
        }
        if (!(response.headers.get('content-type') || '').includes('application/json')) throw new Error('Relace vypršela. Přihlas se znovu.');
        return response.json();
    }
    function setBusy(value) {
        busy = value;
        ['price-lab-save', 'price-lab-reset', 'price-lab-reload', 'price-lab-refresh'].forEach(id => { $(id).disabled = value; });
    }
    function render() {
        $('price-lab-rows').innerHTML = rows.map(r => {
            const scenarios = r.report.scenarios, basis = scenarios.find(s => s.name === 'Base');
            return `<tr><td><button type="button" data-lab-symbol="${esc(r.symbol)}">${esc(r.symbol)} ↗</button><small>${esc(r.name)}</small><strong>${money(r.price)}</strong><small>${esc(r.priceDate || 'chybí cena')}</small></td><td>${scenarios.length === 3 ? `${num(scenarios[0].fairValue)}–${money(scenarios[2].fairValue)}` : 'Chybí platné ocenění'}<small>Základ ${money(basis?.fairValue)}</small></td><td>${money(r.returnPrice)}<small>Požadavek ${num(r.settings.requiredReturnPercent, 1)} % ročně</small></td><td>${money(r.conservativeLimit)}<small>Rezerva ${num(r.report.marginOfSafety * 100, 0)} %</small></td><td>${r.reverseDcf.initialGrowthPercent == null ? 'Nelze určit' : `${num(r.reverseDcf.initialGrowthPercent)} %`}<small>První rok; další růst postupně zpomaluje</small></td><td>${r.reviewRequired ? 'Vyžaduje kontrolu' : 'Modelový scénář'}<small>Úplnost dat ${r.report.dataQuality}/100</small><small>Výkazy ${esc(r.financialPeriod || 'chybí')}</small></td></tr>`;
        }).join('');
    }
    function show(r) {
        current = r;
        $('price-lab-heading').textContent = `${r.symbol} · předpoklady ocenění`;
        const basis = r.report.scenarios.find(s => s.name === 'Base');
        const source = r.settings.sourceUrl;
        const sourceHtml = source ? `<p>Tvůj zdroj: ${sourceLink(source)} · ${esc(r.settings.sourceDate)}</p>` : '';
        $('price-lab-analysis').innerHTML = `<p>Aktuální závěrečná cena ${money(r.price)} (${esc(r.priceDate)}) · základ DCF ${money(basis?.fairValue)} · limit pro výnos ${num(r.settings.requiredReturnPercent)} %: <strong>${money(r.returnPrice)}</strong>.</p><p>Srovnávací EPS × P/E: <strong>${money(r.multipleValue)}</strong>${r.multipleValue == null ? ' · vyplň zdrojované EPS a referenční násobek.' : ''}</p><p><strong>Reverzní DCF: ${r.reverseDcf.initialGrowthPercent == null ? 'nelze určit' : `${num(r.reverseDcf.initialGrowthPercent)} % v prvním roce`}</strong> · ${esc(r.reverseDcf.explanation)}${r.reverseDcf.revenueYear10 == null ? '' : ` Tržby v roce 10: ${money(r.reverseDcf.revenueYear10)}.`}</p>${sourceHtml}<p>Finanční zdroje: ${r.financialSources.map(sourceLink).join(" · ") || "chybí"} · poslední podání ${esc(r.latestFiled || "chybí")}.</p><ul>${r.notes.map(n => `<li>${esc(n)}</li>`).join('')}</ul><details><summary>Vstupy, nejistota a citlivost ocenění</summary><p>${esc(r.report.modelVersion)} · ${esc(r.report.status)} · ${r.report.isTtm ? 'poslední čtyři kvartály' : 'roční výkazy'} · do ${esc(r.financialPeriod)}</p><ul>${r.report.assumptions.filter(a => ['InitialRevenueGrowth', 'TerminalMargin', 'Beta', 'WACC', 'RiskFree', 'CostOfDebt', 'ForwardGrowth', 'ForwardMargin', 'ForwardBeta'].includes(a.name)).map(a => `<li>${esc(a.name)}: ${num(a.value * (a.unit === 'ratio' && !a.name.includes('Beta') ? 100 : 1))}${a.unit === 'ratio' && !a.name.includes('Beta') ? ' %' : ''} · ${esc(a.source)} · ${esc(a.date)}</li>`).join('')}</ul><p>Počet akcií: ${esc(r.report.shareType)} · ${esc(r.report.sharesDate)}.</p><h4>Posledních pět ročních výkazů</h4><div class="screening-table"><table><thead><tr><th>Období</th><th>Tržby USD</th><th>EBIT marže</th><th>FCFF USD</th></tr></thead><tbody>${r.report.history.slice(-5).map(y => `<tr><td>${esc(y.end)}</td><td>${money(y.revenue)}</td><td>${num(y.ebitMargin == null ? null : y.ebitMargin * 100)} %</td><td>${money(y.fcff)}</td></tr>`).join("")}</tbody></table></div><ul>${r.report.warnings.map(w => `<li>${esc(w)}</li>`).join('')}</ul><div class="screening-table"><table><thead><tr><th>WACC</th><th>Terminální růst</th><th>Odhad ceny USD</th></tr></thead><tbody>${r.report.sensitivity.map(s => `<tr><td>${num(s.wacc * 100)} %</td><td>${num(s.growth * 100)} %</td><td>${money(s.fairValue)}</td></tr>`).join('')}</tbody></table></div></details>`;
        const form = $('price-lab-form');
        fields.forEach(key => { form.elements.namedItem(key).value = r.settings[key] ?? ''; });
        $('price-lab-message').textContent = '';
    }
    async function load() {
        const result = await request('/PriceLab');
        rows = result.rows; render();
        $('price-lab-status').textContent = `Výpočet ${new Date(result.checkedAt).toLocaleString('cs-CZ')} · ${rows.filter(r => r.returnPrice != null).length}/${rows.length} firem má podklady pro limit podle požadovaného výnosu. Výpočty vycházejí z uložených dat.`;
    }
    $('price-lab-rows').onclick = e => {
        const symbol = e.target.closest('[data-lab-symbol]')?.dataset.labSymbol;
        if (!symbol || busy) return;
        show(rows.find(r => r.symbol === symbol)); $('price-lab-dialog').showModal();
    };
    $('price-lab-close').onclick = () => $('price-lab-dialog').close();
    async function save(settings) {
        setBusy(true); $('price-lab-message').textContent = 'Přepočítávám a ukládám předpoklady…';
        try {
            const r = await request(`/PriceLab?symbol=${encodeURIComponent(current.symbol)}`, settings);
            rows = rows.map(old => old.symbol === r.symbol ? r : old); render(); show(r);
            $('price-lab-message').textContent = 'Předpoklady byly uložené pro tvůj účet.';
        } catch (error) { $('price-lab-message').textContent = error.message; }
        finally { setBusy(false); }
    }
    $('price-lab-form').onsubmit = e => {
        e.preventDefault(); if (busy) return;
        const form = e.currentTarget, settings = {};
        fields.forEach(key => { const value = form.elements.namedItem(key).value; settings[key] = key === 'sourceUrl' ? value : key === 'sourceDate' ? value || null : value === '' ? null : Number(value); });
        save(settings);
    };
    $('price-lab-reset').onclick = () => { if (!busy) save({ requiredReturnPercent: 10 }); };
    $('price-lab-reload').onclick = async () => {
        if (busy) return; setBusy(true);
        try { await load(); } catch (error) { $('price-lab-status').textContent = error.message; } finally { setBusy(false); }
    };
    $('price-lab-refresh').onclick = async () => {
        if (busy) return; setBusy(true); let failed = 0;
        try {
            for (const r of rows) {
                $('price-lab-status').textContent = `Aktualizuji cenu a výkazy ${r.symbol}…`;
                try { await request(`/Refresh?symbol=${encodeURIComponent(r.symbol)}`, {}); } catch { failed++; }
            }
            await load();
            if (failed) $('price-lab-status').textContent += ` ${failed} aktualizací selhalo; dostupná data zůstala zachovaná.`;
        } catch (error) { $('price-lab-status').textContent = error.message; }
        finally { setBusy(false); }
    };
    setBusy(true);
    load().catch(error => { $('price-lab-status').textContent = error.message; }).finally(() => setBusy(false));
})();
