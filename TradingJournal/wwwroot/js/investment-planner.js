(function () {
    'use strict';
    function project(p, offset = 0, fee = p.fee) {
        const growth = Math.pow((1 + (p.rate + offset) / 100) * (1 - fee / 100), 1 / 12);
        const points = [{ month: 0, value: p.initial, paid: p.initial }];
        for (let month = 1; month <= p.years * 12; month++) {
            points.push({ month, value: points[month - 1].value * growth + p.monthly, paid: p.initial + month * p.monthly });
        }
        return points;
    }
    if (typeof module !== 'undefined') module.exports = { project };
    if (typeof document === 'undefined') return;
    const root = document.getElementById('investment-planner');
    if (!root) return;
    const defaults = { initial: 200000, monthly: 10000, years: 30, rate: 9, inflation: 2, fee: 0.2 };
    const p = { ...defaults };
    let scenario = 0;
    const money = n => new Intl.NumberFormat('cs-CZ', { style: 'currency', currency: 'CZK', maximumFractionDigits: 0 }).format(n);
    const decimal = n => new Intl.NumberFormat('cs-CZ', { maximumFractionDigits: 1 }).format(n);
    const short = n => Math.abs(n) >= 1e6 ? `${decimal(n / 1e6)} mil.` : `${decimal(n / 1000)} tis.`;
    const put = (id, value) => { document.getElementById(id).textContent = value; };
    function render() {
        const base = project(p, scenario), low = project(p, -3), high = project(p, 3);
        const end = base.at(-1), real = end.value / Math.pow(1 + p.inflation / 100, p.years);
        put('plan-value', money(end.value)); put('plan-real', money(real)); put('plan-paid', money(end.paid));
        put('plan-range', `${short(low.at(-1).value)} Kč – ${short(high.at(-1).value)} Kč`);
        put('plan-gain', money(end.value - end.paid));
        document.getElementById('plan-gain').style.color = end.value >= end.paid ? '#60c57c' : '#fb8896';
        put('plan-cost', `dopad nákladů ${money(project(p, scenario, 0).at(-1).value - end.value)}`);
        const name = scenario === 0 ? 'Základní' : scenario < 0 ? 'Pesimistický' : 'Optimistický';
        put('plan-scenario-label', `${name} scénář`);
        const milestones = [1e6, 5e6, 1e7].map(value => ({ value, point: base.find(pt => pt.value >= value) }));
        const first = milestones[0].point;
        put('plan-insight', `${name} scénář: ${first ? first.month === 0 ? 'Milník 1 mil. Kč je již dosažen počátečním vkladem.' : `Na 1 mil. Kč dosáhneš za ${decimal(first.month / 12)} let.` : 'Milníku 1 mil. Kč v tomto horizontu nedosáhneš.'} Reálně (po inflaci) bude majetek za ${p.years} let odpovídat ${money(real)}.`);
        const badges = document.getElementById('plan-milestones'); badges.replaceChildren(document.createTextNode('MILNÍKY'));
        milestones.forEach(m => { const span = document.createElement('span'); span.textContent = `⚑ ${short(m.value)} Kč · ${m.point ? m.point.month === 0 ? 'dosaženo' : `za ${decimal(m.point.month / 12)} let` : 'mimo horizont'}`; badges.append(span); });
        const maxValue = Math.max(...high.map(pt => pt.value), end.paid, p.initial, 1);
        const unit = Math.pow(10, Math.floor(Math.log10(maxValue))), top = Math.ceil(maxValue / unit) * unit;
        const x = month => 55 + month / (p.years * 12) * 915, y = value => 340 - value / top * 280;
        const path = (points, field = 'value') => points.map((pt, i) => `${i ? 'L' : 'M'}${x(pt.month).toFixed(2)},${y(pt[field]).toFixed(2)}`).join(' ');
        let svg = '<title id="plan-chart-title">Projekce majetku</title><desc id="plan-chart-description"></desc>';
        for (let i = 0; i <= 4; i++) { const value = top * i / 4; svg += `<line x1="55" x2="970" y1="${y(value)}" y2="${y(value)}" stroke="#303659"/><text x="0" y="${y(value) - 7}">${value ? short(value) : '0'}</text>`; }
        svg += `<path d="${path(high)} ${path([...low].reverse()).replace(/^M/, 'L')} Z" fill="#3968bd" opacity=".22"/><path d="${path(base, 'paid')}" fill="none" stroke="#7c85a4" stroke-dasharray="5 6" stroke-width="1.5"/>`;
        milestones.filter(m => m.point).forEach((m, i) => { const px = x(m.point.month), labelX = Math.max(105, Math.min(905, px)), labelY = 23 + i * 24; svg += `<line x1="${px}" x2="${px}" y1="${labelY + 10}" y2="340" stroke="#4b5480" stroke-dasharray="4 5"/><text x="${labelX}" y="${labelY}" text-anchor="middle">${short(m.value)} · ${decimal(m.point.month / 12)} r.</text>`; });
        svg += `<path d="${path(base)}" fill="none" stroke="#4b93ff" stroke-width="3"/><text x="965" y="${Math.max(16, y(end.value) - 12)}" text-anchor="end" style="fill:#a8cdff;font-weight:700">${short(end.value)} Kč</text>`;
        for (let i = 0; i <= 4; i++) svg += `<text x="${x(p.years * 12 * i / 4)}" y="375" text-anchor="${i === 0 ? 'start' : i === 4 ? 'end' : 'middle'}">${i ? `rok ${decimal(p.years * i / 4)}` : 'dnes'}</text>`;
        document.getElementById('plan-chart').innerHTML = svg;
        put('plan-chart-description', `${name} scénář, konečná hodnota ${money(end.value)}, vloženo ${money(end.paid)}. Rozpětí ${money(low.at(-1).value)} až ${money(high.at(-1).value)}.`);
    }
    function sync(key) {
        root.querySelector(`[data-field="${key}"]`).value = p[key];
        root.querySelector(`[data-slider="${key}"]`).value = p[key];
    }
    root.querySelectorAll('[data-field], [data-slider]').forEach(input => {
        input.addEventListener('input', () => {
            if (input.value === '' || !Number.isFinite(input.valueAsNumber)) return;
            const key = input.dataset.field || input.dataset.slider;
            p[key] = Math.max(Number(input.min), Math.min(Number(input.max), input.valueAsNumber));
            if (key === 'years') p[key] = Math.round(p[key]);
            if (input.dataset.slider) sync(key); else root.querySelector(`[data-slider="${key}"]`).value = p[key];
            render();
        });
        input.addEventListener('change', () => sync(input.dataset.field || input.dataset.slider));
    });
    root.querySelectorAll('[data-scenario]').forEach(button => button.addEventListener('click', () => {
        scenario = Number(button.dataset.scenario);
        root.querySelectorAll('[data-scenario]').forEach(b => b.setAttribute('aria-pressed', String(b === button)));
        render();
    }));
    document.getElementById('plan-reset').addEventListener('click', () => { Object.assign(p, defaults); Object.keys(p).forEach(sync); scenario = 0; root.querySelectorAll('[data-scenario]').forEach(b => b.setAttribute('aria-pressed', String(b.dataset.scenario === '0'))); render(); });
    render();
})();
