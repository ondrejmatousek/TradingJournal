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
    function macroText(macro) {
        $('screen-macro').replaceChildren();
        $('screen-macro').append(document.createTextNode('MAKRO · '));
        if (!macro.length) { $('screen-macro').append(document.createTextNode('Bez uložených dat. Aktualizuj data nebo přidej doložený vlastní rozbor.')); return; }
        macro.forEach(m => { const span = document.createElement('span'); const label = m.series === 'DGS10' ? 'Výnos US 10Y' : m.series === 'UNRATE' ? 'Nezaměstnanost USA' : 'Geopolitické riziko'; span.innerHTML = `${safeLink(m.source, label)}: ${pct(m.value)} · ${esc(m.date)}${m.priorValue == null ? '' : ` · před 3 měsíci ${pct(m.priorValue)}`}　`; $('screen-macro').append(span); });
    }
    function render() {
        const visible = rows;
        const pages = Math.max(1, Math.ceil((window.screeningTotal ?? visible.length) / 25));
        page = Math.max(1, Math.min(page, pages));
        $('screen-page').textContent = `Strana ${page} / ${pages} · 25 akcií na stránku`;
        $('screen-prev').disabled = page <= 1;
        $('screen-next').disabled = page >= pages;
        $('screen-rows').innerHTML = visible.length ? visible.map(r => `<tr><td><button type="button" data-symbol="${esc(r.symbol)}">${esc(r.symbol)} ↗</button><small title="${esc(r.name)}">${esc(r.name)}</small><small>${esc(r.sector)}</small></td><td>${number(r.price, 2)}<small>${esc(r.priceDate || 'chybí cena')}</small></td><td>${r.valuation ? `${number(r.valuation.low)}–${number(r.valuation.high)}` : '—'}<small>${r.valuation ? `základ ${number(r.valuation.base)}` : 'chybí ocenění'}</small></td><td class="${r.discount > 0 ? 'screen-positive' : ''}">${pct(r.discount)}</td>${['quality','macro','seasonality','technical','geopolitics','analysts'].map(k => `<td>${number(pillar(r,k),0)}${k === 'seasonality' ? `<small>${r.seasonality[1]?.years || 0} let · ${pct(r.seasonality[1]?.median)}</small>` : ''}</td>`).join('')}<td><strong>${number(r.score)} / 100</strong><small>pokrytí ${r.coverage} %</small><small>${esc(r.verdict)}</small></td></tr>`).join('') : '<tr><td colspan="11">Pro tento filtr nejsou žádné akcie.</td></tr>';
    }
    let loadVersion = 0;
    async function load(targetPage = page) {
        const version = ++loadVersion;
        request('/UpdateStatus').then(({schedule, run}) => {
            const time = `${String(schedule.hour).padStart(2, '0')}:${String(schedule.minute).padStart(2, '0')}`;
            const last = run ? ` Poslední běh: ${new Date(run.startedAt).toLocaleString('cs-CZ')}, ${run.processed} akcií, ${run.incomplete} neúplných. ${run.completedAt ? 'Dokončen.' : 'Zatím nedokončen.'}` : ' První aktualizace proběhne automaticky.';
            $('screen-schedule').textContent = (schedule.external ? 'Aktualizaci zajišťuje externí denní úloha; stav jejího nastavení je v GitHub Actions.' : schedule.enabled ? `Automatická aktualizace denně v ${time} (${schedule.timeZone}), pokud aplikace běží.` : 'Automatická aktualizace je vypnutá.') + last;
        }).catch(() => { $('screen-schedule').textContent = 'Stav automatické aktualizace není dostupný.'; });
        const query = new URLSearchParams({ page: targetPage, pageSize: 25, search: $('screen-search').value, sector: $('screen-sector').value, filter: $('screen-filter').value, sort: $('screen-sort').value });
        const data = await request(`/Data?${query}`);
        if (version !== loadVersion) return;
        rows = data.rows; page = data.page; window.screeningTotal = data.total; macroText(data.macro);
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
        $('detail-body').innerHTML = `<p>${esc(r.verdict)} · pokrytí daty ${r.coverage} % · cena ${number(r.price,2)} USD (${esc(r.priceDate || 'chybí')}) · EMA200 ${pct(r.emaDistance)}</p>${r.refreshMessage ? `<p>${esc(r.refreshMessage)}</p>` : ''}<div class="screening-pillars">${r.pillars.map(p => `<article><strong>${esc(p.label)} · ${number(p.score)} / 100</strong><small> · váha ${p.weight} %</small><p>${esc(p.explanation)}</p></article>`).join('')}</div><h3>Ocenění a předpoklady</h3>${r.valuation ? `<p>Pesimistický ${number(r.valuation.low,2)} USD · základní ${number(r.valuation.base,2)} USD · optimistický ${number(r.valuation.high,2)} USD.</p><p>Růst cash flow ${pct(r.valuation.growth)} po 5 let, požadovaný výnos ${pct(r.valuation.discountRate)}, terminální růst ${pct(r.valuation.terminalGrowth)}. Pesimistický: růst −3 p. b., diskont 12 %, terminál 1,5 %. Optimistický: růst +3 p. b., diskont 9 %, terminál 2,5 %.</p><p>Sleva: ${pct(r.discount)}. Potenciál k základní hodnotě: ${r.price > 0 && r.discount != null ? pct((r.valuation.base/r.price-1)*100) : '—'}.</p>` : '<p>Bez vhodných fundamentů se hodnota nevymýšlí. Pro banky a REIT tento model nepoužíváme.</p>'}${r.fundamentals ? `<p>Výkazy za období do ${esc(r.fundamentals.periodEnd)}, podáno ${esc(r.fundamentals.filed)} · ${safeLink(r.fundamentals.source,'zdroj výkazů')}.</p><p>Tržby ${number(r.fundamentals.revenue,0)} USD · FCF ${number(r.fundamentals.operatingCashFlow-r.fundamentals.capitalExpenditure,0)} USD · počet akcií ${number(r.fundamentals.shares,0)}.</p>` : ''}<h3>Sezónnost</h3>${r.seasonality.map(s => `<p>${s.days} obchodních dní · ${s.years} pozorování · medián ${pct(s.median)} · kladná období ${pct(s.winRate)} · nejhorší výsledek ${pct(s.worst)} · medián posledních 5 let ${pct(s.recentMedian)}</p>`).join('')}<h3>Co prověřit</h3><ul>${r.risks.map(risk => `<li>${esc(risk)}</li>`).join('') || '<li>Žádný automatický příznak není důkazem absence rizik.</li>'}</ul><h3>Vlastní zdroje a investiční teze</h3>${r.evidence.length ? r.evidence.map(e => `<article><strong>${esc(e.author)} · ${esc(e.pillar)} · ${esc(e.published)}</strong><p>${safeLink(e.sourceUrl,'Otevřít původní analýzu')} · horizont ${e.horizonDays} dní · hodnocení autora ${number(e.score)}/100</p><p>${esc(e.thesis)}</p><p>Riziko / vyvrácení teze: ${esc(e.risk)}</p></article>`).join('') : '<p>Zatím nejsou uložené zdrojované rozbory. Chybějící geopolitika a analýzy nemají skóre.</p>'}`;
    }
    async function open(symbol) {
        selected = rows.find(r => r.symbol === symbol); $('research-form').hidden = true; detail(); $('screen-detail').showModal();
    }
    $('screen-rows').addEventListener('click', e => { const b = e.target.closest('[data-symbol]'); if (b) open(b.dataset.symbol); });
    $('detail-close').onclick = () => $('screen-detail').close();
    const navigate = p => load(p).catch(e => { $('screen-status').textContent = e.message; });
    let filterTimer;
    ['screen-search','screen-sector','screen-filter','screen-sort'].forEach(id => $(id).addEventListener('input', () => { clearTimeout(filterTimer); filterTimer = setTimeout(() => navigate(1), 250); }));
    $('screen-prev').onclick=()=>{if(page>1) navigate(page-1);}; $('screen-next').onclick=()=>{navigate(page+1);};
    document.querySelectorAll('[data-preset]').forEach(button => button.onclick = () => {
        const preset = button.dataset.preset;
        const filter = $('screen-filter'), sort = $('screen-sort');
        filter.value = preset === 'opportunity' ? 'discount' : preset === 'season' ? 'season' : preset === 'complete' ? 'rated' : 'all';
        sort.value = preset === 'quality' ? 'score' : preset === 'opportunity' ? 'discount' : preset === 'season' ? 'season' : preset === 'complete' ? 'coverage' : 'coverage';
        document.querySelectorAll('[data-preset]').forEach(item => item.classList.toggle('is-active', item === button));
        navigate(1);
    });
    $('screen-refresh').onclick = async () => {
        if (refreshing) return; refreshing = true; stopping = false;
        $('screen-refresh').disabled = true; $('screen-stop').hidden = false;
        let done = 0, failed = 0, incomplete = 0, macroError = '';
        try {
            if (!rows.length) {
                $('screen-status').textContent = 'Znovu načítám uložený screening…';
                await load();
            }
            try { macroText(await request('/RefreshMacro', {})); } catch(e) { macroError = e.message; }
            // Sequential batches preserve predictable provider load and allow stopping without losing completed rows.
            for (const row of [...rows]) {
                if (stopping) break;
                $('screen-status').textContent = `Aktualizuji ${row.symbol} · ${done}/${rows.length}. Obnova může trvat několik minut.`;
                try { const result = await request(`/Refresh?symbol=${encodeURIComponent(row.symbol)}`, {}); rows[rows.findIndex(r => r.symbol === result.symbol)] = result; if (result.refreshMessage || !result.fundamentals) incomplete++; } catch { failed++; }
                done++; render();
            }
            $('screen-status').textContent = `${stopping ? 'Zastaveno' : 'Dokončeno'}: ${done}/${rows.length}, chyb požadavků ${failed}, neúplných dat ${incomplete}. Výkazy ${rows.filter(r => r.fundamentals).length}/${rows.length}, ocenění ${rows.filter(r => r.valuation).length}/${rows.length}. ${macroError} Podrobnosti poskytovatelů jsou v detailu akcie.`;
        } catch (error) {
            $('screen-status').textContent = error.message;
        } finally { refreshing = false; $('screen-refresh').disabled = false; $('screen-stop').hidden = true; }
    };
    $('screen-stop').onclick = () => { stopping = true; $('screen-status').textContent = 'Zastavím po právě probíhající akcii. Hotové výsledky zůstanou uložené.'; };
    $('detail-refresh').onclick = async () => {
        const symbol = selected.symbol; $('detail-refresh').disabled = true;
        try { const result = await request(`/Refresh?symbol=${encodeURIComponent(symbol)}`, {}); rows[rows.findIndex(r => r.symbol === symbol)] = result; if (selected.symbol === symbol) { selected = result; detail(); } render(); }
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


