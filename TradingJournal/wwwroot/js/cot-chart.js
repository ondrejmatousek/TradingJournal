(() => {
    const dataNode = document.querySelector("#cot-dashboard-data");
    if (!dataNode) return;

    const markets = JSON.parse(dataNode.textContent || "[]").map(market => ({
        ...market,
        history: (market.history || [])
            .map((point, index, rows) => {
                const previous = rows[index - 1];
                return {
                    ...point,
                    nonCommercialChange: previous ? point.nonCommercialNet - previous.nonCommercialNet : 0,
                    commercialsChange: previous && previous.commercialsNet !== null && previous.commercialsNet !== undefined
                        && point.commercialsNet !== null && point.commercialsNet !== undefined
                        ? point.commercialsNet - previous.commercialsNet
                        : null
                };
            })
            .sort((a, b) => a.date.localeCompare(b.date))
    }));
    const search = document.querySelector("#cot-market-search");
    const suggestions = document.querySelector("#cot-market-suggestions");
    const status = document.querySelector("#cot-market-status");
    const panels = [...document.querySelectorAll("[data-cot-panel]")];
    const summary = document.querySelector("#cot-market-summary");
    let activeMarket = markets[0] || null;
    let visibleSuggestions = [];
    let highlightedSuggestion = 0;
    let searchTimer = null;
    const chartState = new Map();

    const formatNumber = value => value === null || value === undefined
        ? "-"
        : new Intl.NumberFormat("cs-CZ", { maximumFractionDigits: 0 }).format(value);
    const formatPercent = value => value === null || value === undefined ? "-" : `${Math.round(value)} %`;
    const formatDate = value => value
        ? new Intl.DateTimeFormat("cs-CZ", { day: "2-digit", month: "2-digit", year: "numeric" }).format(new Date(`${value}T00:00:00`))
        : "-";
    const normalize = value => (value || "").toLowerCase().replace(/[^a-z0-9]+/g, "");
    const rollingIndex = (rows, key, lookback = 52) => rows.map((point, index) => {
        const slice = rows.slice(Math.max(0, index - lookback + 1), index + 1)
            .map(item => item[key])
            .filter(value => value !== null && value !== undefined);
        if (!slice.length || point[key] === null || point[key] === undefined) return null;
        const min = Math.min(...slice);
        const max = Math.max(...slice);
        return min === max ? 50 : (point[key] - min) / (max - min) * 100;
    });

    const matchingMarkets = value => {
        const term = normalize(value);
        if (!term) return markets.slice(0, 8);
        return markets
            .map(market => {
                const key = normalize(market.key);
                const name = normalize(market.name);
                const exact = key === term || name === term;
                const starts = key.startsWith(term) || name.startsWith(term);
                const includes = key.includes(term) || name.includes(term);
                return { market, score: exact ? 0 : starts ? 1 : includes ? 2 : 3 };
            })
            .filter(match => match.score < 3)
            .sort((a, b) => a.score - b.score || a.market.name.localeCompare(b.market.name))
            .slice(0, 8)
            .map(match => match.market);
    };
    const findMarket = value => matchingMarkets(value)[0] || activeMarket;

    const renderSuggestions = () => {
        visibleSuggestions = matchingMarkets(search.value);
        highlightedSuggestion = Math.min(highlightedSuggestion, Math.max(visibleSuggestions.length - 1, 0));
        suggestions.innerHTML = "";
        if (!visibleSuggestions.length) {
            const empty = document.createElement("div");
            empty.className = "cot-market-suggestion-empty";
            empty.textContent = "Žádný COT trh neodpovídá hledání";
            suggestions.append(empty);
            suggestions.hidden = false;
            search.setAttribute("aria-expanded", "true");
            return;
        }

        visibleSuggestions.forEach((market, index) => {
            const button = document.createElement("button");
            button.type = "button";
            button.className = "cot-market-suggestion";
            button.setAttribute("role", "option");
            button.setAttribute("aria-selected", String(index === highlightedSuggestion));
            const key = document.createElement("strong");
            key.textContent = market.key;
            const name = document.createElement("span");
            name.textContent = market.name;
            button.append(key, name);
            button.onmousedown = event => event.preventDefault();
            button.onclick = () => selectMarket(market);
            suggestions.append(button);
        });
        suggestions.hidden = false;
        search.setAttribute("aria-expanded", "true");
    };
    const hideSuggestions = () => {
        suggestions.hidden = true;
        search.setAttribute("aria-expanded", "false");
    };

    const updateHeader = () => {
        if (!activeMarket) {
            status.textContent = "Trh nenalezen";
            return;
        }

        const latest = activeMarket.latest;
        status.textContent = `${activeMarket.name} · ${(activeMarket.history || []).length} reportů`;
        document.querySelector("#cot-selected-market").textContent = `${activeMarket.name} · ${activeMarket.key}`;
        document.querySelector("#cot-selected-bias").textContent = latest?.bias || "Bez dat";
        document.querySelector("#cot-selected-date").textContent = formatDate(latest?.date);
        document.querySelector("#cot-selected-index").textContent = formatPercent(latest?.cotIndex);
        document.querySelector("#cot-selected-net").textContent = formatNumber(latest?.net);
        document.querySelector("#cot-selected-oi").textContent = formatNumber(latest?.openInterest);
        summary.textContent = activeMarket.isStale && activeMarket.staleMessage
            ? `${activeMarket.staleMessage} ${latest?.summary || ""}`.trim()
            : latest?.summary || "Pro tento trh zatím nejsou v DB žádná COT data.";

        const narrative = activeMarket.narrative || {};
        document.querySelector("#cot-narrative-headline").textContent = narrative.headline || "-";
        document.querySelector("#cot-narrative-overview").textContent = narrative.overview || "";
        renderList("#cot-bullish-arguments", narrative.bullishArguments || []);
        renderList("#cot-warning-arguments", narrative.warningArguments || []);
        document.querySelector("#cot-practical-use").textContent = narrative.practicalUse || "";
    };
    const renderList = (selector, items) => {
        const list = document.querySelector(selector);
        list.innerHTML = "";
        (items.length ? items : ["Bez aktuálního komentáře."]).forEach(text => {
            const item = document.createElement("li");
            item.textContent = text;
            list.append(item);
        });
    };

    const drawPanel = panel => {
        const type = panel.dataset.cotPanel;
        const canvas = panel.querySelector(".cot-chart-canvas");
        const tooltip = panel.querySelector(".cot-chart-tooltip");
        const dateLabels = panel.querySelectorAll(".cot-chart-dates span");
        const context = canvas.getContext("2d");
        const rows = activeMarket?.history || [];
        const visible = rows.slice(-156);
        const dpr = window.devicePixelRatio || 1;
        const rect = canvas.getBoundingClientRect();
        const width = Math.max(rect.width, 320);
        const height = type === "index" ? 310 : 270;
        canvas.width = width * dpr;
        canvas.height = height * dpr;
        canvas.style.height = `${height}px`;
        context.setTransform(dpr, 0, 0, dpr, 0, 0);
        context.clearRect(0, 0, width, height);
        chartState.set(canvas, []);

        const pad = { top: 24, right: 28, bottom: 30, left: 76 };
        const plotWidth = width - pad.left - pad.right;
        const plotHeight = height - pad.top - pad.bottom;
        const xAt = index => pad.left + (visible.length === 1 ? plotWidth / 2 : index * plotWidth / Math.max(visible.length - 1, 1));

        if (!visible.length) {
            context.fillStyle = "#8794a4";
            context.font = "14px Inter, sans-serif";
            context.fillText("Pro vybraný trh zatím nejsou data.", pad.left, pad.top + 28);
            dateLabels[0].textContent = "-";
            dateLabels[1].textContent = "-";
            return;
        }

        const commercialIndex = rollingIndex(visible, "commercialsNet");
        const getSeries = () => {
            if (type === "nonCommercials") return [
                { key: "nonCommercialNet", label: "Non-commercial net", color: "#f3bd65", width: 2.4 },
                { key: "nonCommercialChange", label: "Weekly change", color: "rgba(243,189,101,.36)", bars: true }
            ];
            if (type === "commercials") return [
                { key: "commercialsNet", label: "Commercial net", color: "#5bd7d4", width: 2.4 },
                { key: "commercialsChange", label: "Weekly change", color: "rgba(91,215,212,.34)", bars: true }
            ];
            if (type === "netPositions") return [
                { key: "nonCommercialNet", label: "Non-commercials", color: "#f3bd65", width: 2.2 },
                { key: "commercialsNet", label: "Commercials", color: "#5bd7d4", width: 2.2 },
                { key: "retailersNet", label: "Retailers", color: "#b89cff", width: 1.8, dash: [5, 4] }
            ];
            return [
                { key: "cotIndex", label: "Non-commercial index", color: "#f06b8d", width: 2.2 },
                { key: "commercialIndex", label: "Commercial index", color: "#4f7cff", width: 2.2, values: commercialIndex }
            ];
        };
        const series = getSeries();
        const values = series.flatMap(item => {
            const source = item.values || visible.map(point => point[item.key]);
            return source.filter(value => value !== null && value !== undefined);
        });
        if (!values.length) return;

        let min = type === "index" ? 0 : Math.min(...values);
        let max = type === "index" ? 100 : Math.max(...values);
        if (min === max) { min -= 1; max += 1; }
        const spread = max - min;
        if (type !== "index") {
            min -= spread * .1;
            max += spread * .1;
        }
        if (min < 0 && max > 0) {
            const zeroPadding = spread * .05;
            min = Math.min(min, -zeroPadding);
            max = Math.max(max, zeroPadding);
        }
        const yAt = value => pad.top + (max - value) / (max - min) * plotHeight;

        context.font = "11px Inter, sans-serif";
        context.textAlign = "right";
        context.fillStyle = "#8794a4";
        context.strokeStyle = "rgba(255,255,255,.1)";
        context.lineWidth = 1;
        for (let i = 0; i < 5; i++) {
            const y = pad.top + plotHeight * i / 4;
            const value = max - (max - min) * i / 4;
            context.beginPath();
            context.moveTo(pad.left, y);
            context.lineTo(width - pad.right, y);
            context.stroke();
            context.fillText(type === "index" ? `${Math.round(value)} %` : formatNumber(value), pad.left - 9, y + 4);
        }

        const zero = yAt(0);
        if (min < 0 && max > 0) {
            context.save();
            context.strokeStyle = "rgba(255,255,255,.25)";
            context.beginPath();
            context.moveTo(pad.left, zero);
            context.lineTo(width - pad.right, zero);
            context.stroke();
            context.restore();
        }
        if (type === "index") {
            [20, 80].forEach(level => {
                const y = yAt(level);
                context.save();
                context.strokeStyle = level === 80 ? "rgba(240,107,141,.42)" : "rgba(79,124,255,.38)";
                context.setLineDash([5, 5]);
                context.beginPath();
                context.moveTo(pad.left, y);
                context.lineTo(width - pad.right, y);
                context.stroke();
                context.restore();
            });
        }

        const hitPoints = [];
        const drawBars = item => {
            const baseline = min < 0 && max > 0 ? zero : yAt(0);
            const barWidth = Math.max(2, plotWidth / Math.max(visible.length, 1) * .52);
            visible.forEach((point, index) => {
                const value = point[item.key];
                if (value === null || value === undefined) return;
                const x = xAt(index);
                const y = yAt(value);
                context.fillStyle = value >= 0 ? item.color : item.color.replace(".36", ".52").replace(".34", ".52");
                context.fillRect(x - barWidth / 2, Math.min(y, baseline), barWidth, Math.max(Math.abs(baseline - y), 1));
            });
        };
        const drawLine = item => {
            const source = item.values || visible.map(point => point[item.key]);
            context.save();
            context.strokeStyle = item.color;
            context.lineWidth = item.width || 2;
            context.setLineDash(item.dash || []);
            context.beginPath();
            let started = false;
            source.forEach((value, index) => {
                if (value === null || value === undefined) return;
                const x = xAt(index);
                const y = yAt(value);
                if (!started) {
                    context.moveTo(x, y);
                    started = true;
                } else {
                    context.lineTo(x, y);
                }
                hitPoints.push({ x, y, point: visible[index], value, label: item.label });
            });
            context.stroke();
            context.restore();
        };

        series.filter(item => item.bars).forEach(drawBars);
        series.filter(item => !item.bars).forEach(drawLine);
        chartState.set(canvas, hitPoints);
        dateLabels[0].textContent = formatDate(visible[0].date);
        dateLabels[1].textContent = formatDate(visible[visible.length - 1].date);

        const showTooltip = event => {
            const points = chartState.get(canvas) || [];
            if (!points.length) return;
            const box = canvas.getBoundingClientRect();
            const x = event.clientX - box.left;
            const nearest = points.reduce((best, point) => Math.abs(point.x - x) < Math.abs(best.x - x) ? point : best);
            tooltip.innerHTML = `<strong>${formatDate(nearest.point.date)}</strong><span>${nearest.label}: ${type === "index" ? formatPercent(nearest.value) : formatNumber(nearest.value)}</span><span>Non-commercial net: ${formatNumber(nearest.point.nonCommercialNet)}</span><span>Commercial net: ${formatNumber(nearest.point.commercialsNet)}</span>`;
            tooltip.hidden = false;
            tooltip.style.left = `${Math.min(Math.max(nearest.x, 110), box.width - 110)}px`;
            tooltip.style.top = `${Math.max(nearest.y - 12, 10)}px`;
        };
        canvas.onmousemove = showTooltip;
        canvas.onmouseleave = () => { tooltip.hidden = true; };
    };

    const drawDashboard = () => {
        updateHeader();
        panels.forEach(drawPanel);
    };
    const selectMarket = market => {
        if (!market) return;
        activeMarket = market;
        search.value = market.name;
        hideSuggestions();
        drawDashboard();
    };
    const applySearch = (showSuggestions = false) => {
        const market = findMarket(search.value);
        if (!market) {
            status.textContent = "Trh nenalezen";
            return;
        }
        activeMarket = market;
        if (showSuggestions) renderSuggestions();
        drawDashboard();
    };

    search.addEventListener("input", () => {
        window.clearTimeout(searchTimer);
        searchTimer = window.setTimeout(() => applySearch(true), 180);
    });
    search.addEventListener("focus", renderSuggestions);
    search.addEventListener("blur", hideSuggestions);
    search.addEventListener("keydown", event => {
        if (event.key === "ArrowDown") {
            event.preventDefault();
            highlightedSuggestion = Math.min(highlightedSuggestion + 1, Math.max(visibleSuggestions.length - 1, 0));
            renderSuggestions();
        }
        if (event.key === "ArrowUp") {
            event.preventDefault();
            highlightedSuggestion = Math.max(highlightedSuggestion - 1, 0);
            renderSuggestions();
        }
        if (event.key === "Enter") {
            event.preventDefault();
            selectMarket(visibleSuggestions[highlightedSuggestion] || findMarket(search.value));
        }
        if (event.key === "Escape") hideSuggestions();
    });
    panels.forEach(panel => new ResizeObserver(() => drawPanel(panel)).observe(panel.querySelector(".cot-chart-stage")));
    drawDashboard();
})();
