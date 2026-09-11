(() => {
    'use strict';
    const app = document.getElementById('screening-app');
    if (!app) return;
    const $ = id => document.getElementById(id);
    const base = app.dataset.base;
    const token = app.querySelector('[name="__RequestVerificationToken"]').value;
    let rows = [], selected = null, stopping = false, refreshing = false, research = null, page = 1;
    const number = (n, digits = 1) => n == null ? '—' : new Intl.NumberFormat('cs-CZ', { maximumFractionDigits: digits }).format(n);
    const esc = value => String(value ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
    const pct = n => n == null ? '—' : `${number(n)} %`;
    const safeLink = (url, label) => { try { const u = new URL(url); if (u.protocol !== 'https:' || u.username || u.password) return esc(label); return `<a href="${esc(u.href)}" target="_blank" rel="noopener noreferrer">${esc(label)}</a>`; } catch { return esc(label); } };
    const pillar = (r, key) => r.pillars.find(p => p.key === key)?.score;
    async function request(path, body, signal) {
        const response = await fetch(base + path, { method: body === undefined ? 'GET' : 'POST', headers: body === undefined ? {} : { 'Content-Type': 'application/json', 'RequestVerificationToken': token }, body: body === undefined ? undefined : JSON.stringify(body), signal });
        if (!response.ok) { const message = await response.text(); throw new Error(message.length < 500 && !message.includes('<') ? message : `Požadavek selhal (${response.status}).`); }
        if (!(response.headers.get('content-type') || '').includes('application/json')) throw new Error('Relace vypršela. Přihlas se znovu.');
        return response.json();
    }
    function render() {
        const visible = rows;
        const pages = Math.max(1, Math.ceil((window.screeningTotal ?? visible.length) / 25));
        page = Math.max(1, Math.min(page, pages));
        $('screen-page').textContent = `Strana ${page} / ${pages} · 25 akcií na stránku`;
        $('screen-prev').disabled = page <= 1;
        $('screen-next').disabled = page >= pages;
        $('screen-rows').innerHTML = visible.length ? visible.map(r => `<tr><td><button type="button" data-symbol="${esc(r.symbol)}">${esc(r.symbol)} ↗</button><small title="${esc(r.name)}">${esc(r.name)}</small><small>${esc(r.sector)}</small></td><td>${number(r.price, 2)}<small>${esc(r.priceDate || 'chybí cena')}</small></td><td>${r.valuation ? `${number(r.valuation.low)}–${number(r.valuation.high)}` : '—'}<small>${r.valuation ? `základ ${number(r.valuation.base)}` : 'chybí ocenění'}</small></td><td class="${r.discount > 0 ? 'screen-positive' : r.discount != null ? 'screen-negative' : ''}">${pct(r.discount)}</td><td><strong>${esc(r.entry?.label || "Čeká na přepočet")}</strong><small>${r.entry?.zoneLow != null ? `${number(r.entry.zoneLow,2)}–${number(r.entry.zoneHigh,2)} USD` : "Zóna neurčena"}</small><small>${r.valuation?.buyBelow > 0 ? `Limit ${number(r.valuation.buyBelow,2)} USD` : ""}</small></td><td><strong>Kvalita dat ${number(r.valuation?.confidence)} / 100</strong><small>Nejistota ${esc(r.valuation?.uncertainty || "nevyhodnocena")}</small><small>${esc(r.valuation?.status || r.entry?.label || r.verdict)}</small></td></tr>`).join('') : '<tr><td colspan="6">Pro tento filtr nejsou žádné akcie.</td></tr>';
    }
    let loadVersion = 0;
    async function load(targetPage = page) {
        const version = ++loadVersion;
        request('/UpdateStatus').then(({schedule, run}) => {
            const time = `${String(schedule.hour).padStart(2, '0')}:${String(schedule.minute).padStart(2, '0')}`;
            const last = run ? ` Poslední běh: ${new Date(run.startedAt).toLocaleString('cs-CZ')}, ${run.processed} akcií, ${run.incomplete} neúplných. ${run.completedAt ? 'Dokončen.' : 'Zatím nedokončen.'}` : ' První aktualizace proběhne automaticky.';
            $('screen-schedule').textContent = (schedule.external ? 'Aktualizaci zajišťuje externí denní úloha; stav jejího nastavení je v GitHub Actions.' : schedule.enabled ? `Automatická aktualizace denně v ${time} (${schedule.timeZone}), pokud aplikace běží.` : 'Automatická aktualizace je vypnutá.') + last;
        }).catch(() => { $('screen-schedule').textContent = 'Stav automatické aktualizace není dostupný.'; });
        const query = new URLSearchParams({ page: targetPage, pageSize: 25, search: $('screen-search').value, sector: $('screen-sector').value, filter: $('screen-filter').value, sort: $('screen-sort').value, minDiscount: $('screen-min-discount').value, minScore: $('screen-min-score').value, minConfidence: $('screen-min-confidence').value });
        const data = await request(`/Data?${query}`);
        if (version !== loadVersion) return;
        rows = data.rows; page = data.page; window.screeningTotal = data.total;
        $('screen-count').textContent = `${data.total} / ${data.universeTotal}`;
        $('screen-rated').textContent = data.rated;
        $('screen-discount').textContent = data.discounted;
        $('screen-status').textContent = `Hodnocení vypočteno ${new Date(data.calculatedAt).toLocaleString('cs-CZ')}`;
        const sector = $('screen-sector').value;
        $('screen-sector').innerHTML = '<option value="">Všechny sektory</option>' + data.sectors.map(s => `<option>${esc(s)}</option>`).join('');
        $('screen-sector').value = sector; render();
        if (selected) selected = rows.find(r => r.symbol === selected.symbol) || selected;
    }
    function detail() {
        const r = selected;
        $('detail-title').textContent = `${r.symbol} · ${r.name}`;
        $('detail-body').innerHTML = `<p>${esc(r.entry?.label || r.verdict)} · pokrytí daty ${r.coverage} % · cena ${number(r.price,2)} USD (${esc(r.priceDate || 'chybí')}) · EMA200 ${pct(r.emaDistance)}</p>${r.refreshMessage ? `<p>${esc(r.refreshMessage)}</p>` : ''}<div class="screening-pillars">${r.pillars.map(p => `<article><strong>${esc(p.label)} · ${number(p.score)} / 100</strong><small> · váha ${p.weight} %</small><p>${esc(p.explanation)}</p></article>`).join('')}</div><h3>Zdůvodnění vstupu</h3><p>${r.entry?.zoneLow != null ? `Oblast pro sledování vstupu: ${number(r.entry.zoneLow,2)}–${number(r.entry.zoneHigh,2)} USD. Platí pouze při splnění podmínek stabilizace.` : "Nákupní zóna není potvrzena."}</p><ul>${(r.entry?.reasons || ["Nové hodnocení vznikne při denním přepočtu."]).map(reason => `<li>${esc(reason)}</li>`).join("")}</ul><h3>Ocenění a předpoklady</h3>${r.valuation ? `<p>Pesimistický ${number(r.valuation.low,2)} USD · základní ${number(r.valuation.base,2)} USD · optimistický ${number(r.valuation.high,2)} USD.</p><p>Model ${esc(r.valuation.model || "Legacy DCF")}: růst ${pct(r.valuation.growth)}, WACC ${pct(r.valuation.discountRate)}, terminální růst ${pct(r.valuation.terminalGrowth)}.</p><p>Sleva: ${pct(r.discount)}. Kvalita dat: ${number(r.valuation.confidence)}/100; nejistota ${esc(r.valuation.uncertainty || "nevyhodnocena")}.</p>` : '<p>Bez vhodných fundamentů se hodnota nevymýšlí. Pro banky a REIT tento model nepoužíváme.</p>'}${r.fundamentals ? `<p>Výkazy za období do ${esc(r.fundamentals.periodEnd)}, podáno ${esc(r.fundamentals.filed)} · ${safeLink(r.fundamentals.source,'zdroj výkazů')}.</p><p>Tržby ${number(r.fundamentals.revenue,0)} USD · FCF ${number(r.fundamentals.operatingCashFlow-r.fundamentals.capitalExpenditure,0)} USD · počet akcií ${number(r.fundamentals.shares,0)}.</p>` : ''}<h3>Co prověřit</h3><ul>${r.risks.map(risk => `<li>${esc(risk)}</li>`).join('') || '<li>Žádný automatický příznak není důkazem absence rizik.</li>'}</ul><h3>Vlastní zdroje a investiční teze</h3>${r.evidence.length ? r.evidence.map(e => `<article><strong>${esc(e.author)} · ${esc(e.pillar)} · ${esc(e.published)}</strong><p>${safeLink(e.sourceUrl,'Otevřít původní analýzu')} · horizont ${e.horizonDays} dní · hodnocení autora ${number(e.score)}/100</p><p>${esc(e.thesis)}</p><p>Riziko / vyvrácení teze: ${esc(e.risk)}</p></article>`).join('') : '<p>Zatím nejsou uložené zdrojované rozbory. Chybějící geopolitika a analýzy nemají skóre.</p>'}`;
        if (r.valuation && r.valuation.model !== "ValuationV2.0") {
            $('detail-body').insertAdjacentHTML('beforeend', `<h3>Kompozitní ocenění</h3><p>Fair Value ${number(r.valuation.compositeFairValue ?? r.valuation.base,2)} USD · ${esc(r.valuation.status || 'N/A')} · confidence ${number(r.valuation.confidence)}/100 · valuation score ${number(r.valuation.valuationScore)}/100.</p><p>DCF ${number(r.valuation.dcfFairValue,2)} · P/E ${number(r.valuation.relativeFairValue,2)} · forward P/E ${number(r.valuation.forwardPeFairValue,2)} · EV/EBITDA ${number(r.valuation.evEbitdaFairValue,2)} · FCF ${number(r.valuation.fcfFairValue,2)} USD. Váhy: ${esc(r.valuation.weights || '—')}</p><p>WACC ${pct(r.valuation.discountRate)} · bezriziková sazba ${pct(r.valuation.riskFreeRate)} · beta ${number(r.valuation.beta)} · ERP ${pct(r.valuation.equityRiskPremium)} · náklad dluhu ${pct(r.valuation.costOfDebt)} · daň ${pct(r.valuation.taxRate)}${r.valuation.waccFallbackUsed ? ' · použity konzervativní fallbacky' : ''}.</p><p>${esc(r.valuation.explanation || '')}</p><button type="button" id="valuation-history">Načíst historii ocenění</button><div id="valuation-history-result"></div>`);
        }
    }
    async function loadValuationAudit(r) {
        const id=r.valuationRunId || r.valuation?.runId;
        if(!id) return;
        const host=document.createElement('section');host.className='valuation-audit';host.textContent='Načítám audit ocenění…';$('detail-body').append(host);
        try {
            const data=await request(`/ValuationDetail?symbol=${encodeURIComponent(r.symbol)}&id=${encodeURIComponent(id)}`);
            if(selected?.symbol!==r.symbol || !host.isConnected)return;
            const v=data.report;
            const table=(headers,values)=>`<div class="screening-table"><table><thead><tr>${headers.map(x=>`<th>${esc(x)}</th>`).join('')}</tr></thead><tbody>${values.map(row=>`<tr>${row.map(x=>`<td>${esc(x)}</td>`).join('')}</tr>`).join('')}</tbody></table></div>`;
            host.innerHTML=`<h3>Proč ocenění vyšlo právě takto</h3><p>${esc(v.modelVersion)} · ${esc(v.status)} · ${v.isTtm?'TTM':'FY'} do ${esc(v.financialPeriodEnd)} · kvalita dat ${number(v.dataQuality)}/100 · nejistota ${esc(v.uncertainty)}</p><p>Rezerva ${pct(v.marginOfSafety*100)} · nákupní limit ${number(v.buyBelow,2)} USD. Počet akcií: ${esc(v.shareType)} (${esc(v.sharesDate)}).</p><ul>${v.warnings.map(x=>`<li>${esc(x)}</li>`).join('')}</ul>`;
            host.insertAdjacentHTML('beforeend',`<h3>Historie (částky v USD)</h3>${table(['Období','Tržby','Růst %','EBIT','Marže %','D&A','CapEx','ΔNWC','FCFF','Diluted shares'],v.history.map(x=>[x.end,number(x.revenue,0),number(x.revenueGrowth == null ? null : x.revenueGrowth*100),number(x.ebit,0),number(x.ebitMargin == null ? null : x.ebitMargin*100),number(x.da,0),number(x.capEx,0),number(x.changeNwc,0),number(x.fcff,0),number(x.dilutedShares,0)]))}`);
            for(const scenario of v.scenarios) {
                const details=document.createElement('details');details.open=scenario.name==='Base';
                details.innerHTML=`<summary>${esc(scenario.name)} · ${number(scenario.fairValue,2)} USD</summary><p>WACC ${pct(scenario.wacc*100)} · terminální růst ${pct(scenario.terminalGrowth*100)} · ROIC ${pct(scenario.terminalRoic*100)}</p>${table(['Rok','Růst %','Tržby','Marže %','EBIT','Daň %','NOPAT','D&A','CapEx','ΔNWC','FCFF','PV FCFF'],scenario.years.map(x=>[x.year,number(x.revenueGrowth*100),number(x.revenue,0),number(x.ebitMargin*100),number(x.ebit,0),number(x.taxRate*100),number(x.nopat,0),number(x.da,0),number(x.capEx,0),number(x.changeNwc,0),number(x.fcff,0),number(x.presentValueFcff,0)]))}${table(['Přechod k hodnotě akcií','USD'],[['Terminální NOPAT',number(scenario.terminalNopat,2)],['Terminální reinvestice',number(scenario.terminalReinvestment,2)],['Terminální FCFF',number(scenario.terminalFcff,2)],['Terminální hodnota (nediskontovaná)',number(scenario.terminalValue,2)],['PV explicitních toků',number(scenario.pvExplicit,2)],['PV terminální hodnoty',number(scenario.pvTerminal,2)],['Enterprise value',number(scenario.enterpriseValue,2)],['− Dluh',number(scenario.debt,2)],['+ Přebytečná hotovost',number(scenario.excessCash,2)],['+ Neprovozní investice',number(scenario.investments,2)],['− Další nároky',number(scenario.otherClaims,2)],['Equity value',number(scenario.equityValue,2)],['Počet akcií',number(scenario.shares,2)],['Hodnota na akcii',number(scenario.fairValue,2)]] )}`;host.append(details);
            }
            const growths=[...new Set(v.sensitivity.map(x=>x.growth))],rates=[...new Set(v.sensitivity.map(x=>x.wacc))];
            host.insertAdjacentHTML('beforeend',`<h3>Citlivost WACC × terminální růst</h3>${table(['WACC / růst',...growths.map(x=>pct(x*100))],rates.map(rate=>[pct(rate*100),...growths.map(g=>number(v.sensitivity.find(x=>x.wacc===rate&&x.growth===g)?.fairValue,2))]))}<h3>Kontrola násobky</h3>${table(['Metoda','Aktuální násobek','Referenční násobek','Implikovaná hodnota','Zdroj / omezení'],v.crossChecks.map(x=>[x.method,number(x.currentMultiple,2),number(x.referenceMultiple,2),number(x.impliedValue,2),x.source+' · '+x.explanation]))}<h3>Předpoklady</h3>${table(['Vstup','Hodnota','Jednotka','Datum','Zdroj','Odhad','Zdůvodnění'],v.assumptions.map(x=>[x.name,number(x.value,6),x.unit,x.date,x.source,x.isEstimated?'ano':'ne',x.explanation]))}<h3>Normalizace</h3>${table(['Metrika','Původní','Normalizovaná','Důvod'],v.normalizations.map(x=>[x.metric,number(x.original,6),number(x.normalized,6),x.reason]))}`);
            host.insertAdjacentHTML('beforeend', '<button type="button" id="valuation-history">Načíst historii ocenění</button><div id="valuation-history-result"></div>');
            if(data.financials?.latest) {
                const details=document.createElement('details');details.innerHTML=`<summary>Zdrojové výkazy a jednotky</summary>${table(['Metrika','Hodnota','Jednotka','Od','Do','Podáno','XBRL tag','Accession','Zdroj','Odvození'],Object.entries(data.financials.latest.facts).map(([key,x])=>[key,number(x.value,4),x.unit,x.start,x.end,x.filed,x.tag,x.accession,x.source,x.derivation]))}`;host.append(details);
            }
        } catch(error){host.textContent=error.message;}
    }
    $('detail-body').addEventListener('click', async e => {
        if (!e.target.matches('#valuation-history')) return;
        e.target.disabled = true;
        try { const history = await request(`/ValuationHistory?symbol=${encodeURIComponent(selected.symbol)}&days=365`); const last = history[history.length - 1]; const fair = last?.value?.base ?? last?.fairValueBase; $('valuation-history-result').innerHTML = history.length ? `<p>Historických bodů: ${history.length}. Poslední: ${esc(last.date)} · ${number(fair,2)} USD.</p>` : '<p>Historie zatím není k dispozici; vznikne po denním běhu workeru.</p>'; }
        catch (error) { $('valuation-history-result').textContent = error.message; }
        finally { e.target.disabled = false; }
    });
    async function open(symbol) {
        selected = rows.find(r => r.symbol === symbol); $('research-form').hidden = true; detail(); $('screen-detail').showModal(); await loadValuationAudit(selected);
    }
    $('screen-rows').addEventListener('click', e => { const b = e.target.closest('[data-symbol]'); if (b) open(b.dataset.symbol); });
    $('detail-close').onclick = () => $('screen-detail').close();
    const navigate = p => load(p).catch(e => { $('screen-status').textContent = e.message; });
    let filterTimer;
    ['screen-search','screen-sector','screen-filter','screen-sort','screen-min-discount','screen-min-score','screen-min-confidence'].forEach(id => $(id).addEventListener('input', () => { clearTimeout(filterTimer); filterTimer = setTimeout(() => navigate(1), 250); }));
    $('screen-prev').onclick=()=>{if(page>1) navigate(page-1);}; $('screen-next').onclick=()=>{navigate(page+1);};
    document.querySelectorAll('[data-preset]').forEach(button => button.onclick = () => {
        const preset = button.dataset.preset;
        const filter = $('screen-filter'), sort = $('screen-sort');
        filter.value = preset === 'opportunity' ? 'candidate' : preset === 'complete' ? 'rated' : 'all';
        sort.value = preset === 'quality' ? 'score' : preset === 'opportunity' ? 'discount' : preset === 'complete' ? 'coverage' : 'coverage';
        document.querySelectorAll('[data-preset]').forEach(item => item.classList.toggle('is-active', item === button));
        navigate(1);
    });
    $('screen-refresh').onclick = async () => {
        if (refreshing) return; refreshing = true; stopping = false;
        $('screen-refresh').disabled = true; $('screen-stop').hidden = false;
        let done = 0, failed = 0, incomplete = 0;
        try {
            if (!rows.length) {
                $('screen-status').textContent = 'Znovu načítám uložený screening…';
                await load();
            }
            // Sequential batches preserve predictable provider load and allow stopping without losing completed rows.
            for (const row of [...rows]) {
                if (stopping) break;
                $('screen-status').textContent = `Aktualizuji ${row.symbol} · ${done}/${rows.length}. Obnova může trvat několik minut.`;
                try { const result = await request(`/Refresh?symbol=${encodeURIComponent(row.symbol)}`, {}); rows[rows.findIndex(r => r.symbol === result.symbol)] = result; if (result.refreshMessage || !result.fundamentals) incomplete++; } catch { failed++; }
                done++; render();
            }
            $('screen-status').textContent = `${stopping ? 'Zastaveno' : 'Dokončeno'}: ${done}/${rows.length}, chyb požadavků ${failed}, neúplných dat ${incomplete}. Výkazy ${rows.filter(r => r.fundamentals).length}/${rows.length}, ocenění ${rows.filter(r => r.valuation).length}/${rows.length}. Podrobnosti poskytovatelů jsou v detailu akcie.`;
        } catch (error) {
            $('screen-status').textContent = error.message;
        } finally { refreshing = false; $('screen-refresh').disabled = false; $('screen-stop').hidden = true; }
    };
    $('screen-stop').onclick = () => { stopping = true; $('screen-status').textContent = 'Zastavím po právě probíhající akcii. Hotové výsledky zůstanou uložené.'; };
    $('detail-refresh').onclick = async () => {
        const symbol = selected.symbol; $('detail-refresh').disabled = true;
        try { const result = await request(`/Refresh?symbol=${encodeURIComponent(symbol)}`, {}); rows[rows.findIndex(r => r.symbol === symbol)] = result; if (selected.symbol === symbol) { selected = result; detail(); await loadValuationAudit(selected); } render(); }
        catch(e) { $('screen-status').textContent = e.message; }
        finally { $('detail-refresh').disabled = false; }
    };
    const fields = [['periodEnd','Konec účetního období','date'],['filed','Datum zveřejnění','date'],['revenue','Roční tržby','number'],['previousRevenue','Tržby předchozího roku','number'],['operatingCashFlow','Provozní cash flow','number'],['capitalExpenditure','Capex (kladně)','number'],['netIncome','Čistý zisk','number'],['assets','Aktiva','number'],['liabilities','Závazky','number'],['shares','Aktuální počet akcií','number'],['source','Odkaz na výkazy','url']];
    function evidenceForm() {
        $('evidence-fields').innerHTML = research.evidence.map((e,i) => `<div class="evidence-entry" data-index="${i}"><label>Oblast<select data-key="pillar">${[['macro','Makro'],['geopolitics','Geopolitika'],['analysts','Analýza autora']].map(([v,l]) => `<option value="${v}" ${e.pillar===v?'selected':''}>${l}</option>`).join('')}</select></label><label>Skóre příznivosti 0–100<input type="number" data-key="score" min="0" max="100" value="${e.score}" required /></label><label>Datum publikace<input type="date" data-key="published" value="${esc(e.published)}" required /></label><label>Horizont dní<input type="number" data-key="horizonDays" min="1" max="365" value="${e.horizonDays}" required /></label><label>Autor / instituce<input data-key="author" maxlength="200" value="${esc(e.author)}" required /></label><label>HTTPS odkaz<input type="url" data-key="sourceUrl" value="${esc(e.sourceUrl)}" required /></label><label>Teze a dopad na tuto firmu<textarea data-key="thesis" maxlength="3000" required>${esc(e.thesis)}</textarea></label><label>Riziko a co by tezi vyvrátilo<textarea data-key="risk" maxlength="3000" required>${esc(e.risk)}</textarea></label><label><span><input type="checkbox" data-key="trackRecordVerified" ${e.trackRecordVerified?'checked':''} /> Prověřil jsem historii autora (včetně neúspěchů)</span></label><button type="button" data-remove="${i}">Odebrat rozbor</button></div>`).join('');
    }
    function collectEvidence() { research.evidence = [...$('evidence-fields').querySelectorAll('.evidence-entry')].map(entry => Object.fromEntries([...entry.querySelectorAll('[data-key]')].map(input => [input.dataset.key, input.type === 'checkbox' ? input.checked : input.type === 'number' ? Number(input.value) : input.value]))); }
    $('detail-research').onclick = async () => {
        try {
            research = await request(`/Research?symbol=${encodeURIComponent(selected.symbol)}`);
            $('fundamental-fields').innerHTML = fields.map(([key,label,type]) => `<label>${label}<input data-fundamental="${key}" type="${type}" ${type==='number'?'step="any"':''} value="${esc(research.fundamentals?.[key] ?? '')}" /></label>`).join('');
            evidenceForm(); $('research-error').textContent = ''; $('research-form').hidden = false;
        } catch(e) { $('screen-status').textContent = e.message; }
    };
    $('evidence-add').onclick = () => { collectEvidence(); research.evidence.push({pillar:'geopolitics',score:50,published:new Date().toISOString().slice(0,10),horizonDays:90,author:'',sourceUrl:'',thesis:'',risk:'',trackRecordVerified:false}); evidenceForm(); };
    $('evidence-fields').onclick = e => { const b=e.target.closest('[data-remove]'); if(b) {collectEvidence();research.evidence.splice(Number(b.dataset.remove),1);evidenceForm();} };
    $('research-form').onsubmit = async e => {
        e.preventDefault(); collectEvidence(); const inputs=[...$('fundamental-fields').querySelectorAll('input')];
        if(inputs.some(i=>i.value) && inputs.some(i=>!i.value)) { $('research-error').textContent='Výkazy vyplň celé, nebo všechna pole ponech prázdná.';return; }
        research.fundamentals=inputs.every(i=>!i.value)?null:{...Object.fromEntries(inputs.map(i=>[i.dataset.fundamental,i.type==='number'?Number(i.value):i.value])),currency:'USD'};
        const submit=e.submitter; submit.disabled=true;
        try {await request(`/Research?symbol=${encodeURIComponent(selected.symbol)}`,research);await load();detail();$('research-form').hidden=true;$('screen-status').textContent='Výzkum uložen a hodnocení přepočítáno.';} catch(error){$('research-error').textContent=error.message;} finally{submit.disabled=false;}
    };
    load().then(() => { $('screen-status').textContent='Zobrazená jsou uložená data. Pro nové ceny a fundamenty spusť aktualizaci.'; }).catch(e => { $('screen-status').textContent=e.message; });
})();

