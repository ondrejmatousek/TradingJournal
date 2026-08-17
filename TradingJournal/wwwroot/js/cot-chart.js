(() => {
    const charts = document.querySelectorAll("[data-cot-chart]");
    if (!charts.length) return;

    const formatNumber = value => new Intl.NumberFormat("cs-CZ", { maximumFractionDigits: 0 }).format(value);
    const formatDate = value => new Intl.DateTimeFormat("cs-CZ", { day: "2-digit", month: "2-digit", year: "2-digit" }).format(new Date(`${value}T00:00:00`));

    charts.forEach(chart => {
        const data = JSON.parse(chart.dataset.cotChart).sort((a, b) => a.date.localeCompare(b.date));
        const canvas = chart.querySelector(".cot-chart-canvas");
        const stage = chart.querySelector(".cot-chart-stage");
        const tooltip = chart.querySelector(".cot-chart-tooltip");
        const dateLabels = chart.querySelectorAll(".cot-chart-dates span");
        let range = 26;
        let series = "net";
        let points = [];

        const draw = () => {
            const visible = data.slice(-range);
            const rect = canvas.getBoundingClientRect();
            const dpr = window.devicePixelRatio || 1;
            const width = Math.max(rect.width, 260);
            const height = 230;
            canvas.width = width * dpr;
            canvas.height = height * dpr;
            canvas.style.height = `${height}px`;
            const context = canvas.getContext("2d");
            context.scale(dpr, dpr);
            context.clearRect(0, 0, width, height);

            const padding = { top: 18, right: 18, bottom: 24, left: 52 };
            const plotWidth = width - padding.left - padding.right;
            const plotHeight = height - padding.top - padding.bottom;
            const values = series === "index"
                ? visible.map(point => point.cotIndex).filter(value => value !== null)
                : series === "participants"
                    ? visible.flatMap(point => [point.commercialsNet, point.retailersNet]).filter(value => value !== null)
                : visible.flatMap(point => series === "longShort" ? [point.longPosition, point.shortPosition] : [point.netPosition]);
            if (!values.length) return;

            let min = Math.min(...values);
            let max = Math.max(...values);
            if (series === "index") { min = 0; max = 100; }
            if (min === max) { min -= 1; max += 1; }
            const spread = max - min;
            min -= spread * .08;
            max += spread * .08;

            context.font = "11px Inter, sans-serif";
            context.textAlign = "right";
            context.fillStyle = "#8693a2";
            context.strokeStyle = "rgba(255,255,255,.1)";
            context.lineWidth = 1;
            for (let index = 0; index < 4; index++) {
                const y = padding.top + plotHeight * index / 3;
                const value = max - (max - min) * index / 3;
                context.beginPath();
                context.moveTo(padding.left, y);
                context.lineTo(width - padding.right, y);
                context.stroke();
                context.fillText(series === "index" ? `${Math.round(value)} %` : formatNumber(value), padding.left - 8, y + 4);
            }

            const xAt = index => padding.left + (visible.length === 1 ? plotWidth / 2 : index * plotWidth / (visible.length - 1));
            const yAt = value => padding.top + (max - value) / (max - min) * plotHeight;
            points = [];
            const drawLine = (key, color, dash = []) => {
                context.beginPath();
                context.strokeStyle = color;
                context.lineWidth = 2.5;
                context.setLineDash(dash);
                visible.forEach((point, index) => {
                    const value = point[key];
                    if (value === null) return;
                    const x = xAt(index);
                    const y = yAt(value);
                    points.push({ x, y, point, value, key });
                    index === 0 || !visible[index - 1][key] ? context.moveTo(x, y) : context.lineTo(x, y);
                });
                context.stroke();
                context.setLineDash([]);
            };

            if (series === "index") drawLine("cotIndex", "#f39ab5", [5, 4]);
            if (series === "net") drawLine("netPosition", "#24dfcf");
            if (series === "longShort") {
                drawLine("longPosition", "#24dfcf");
                drawLine("shortPosition", "#f39ab5");
            }
            if (series === "participants") {
                drawLine("commercialsNet", "#f3bd65");
                drawLine("retailersNet", "#b89cff", [5, 4]);
            }
            dateLabels[0].textContent = formatDate(visible[0].date);
            dateLabels[1].textContent = formatDate(visible[visible.length - 1].date);
        };

        const showTooltip = event => {
            if (!points.length) return;
            const rect = canvas.getBoundingClientRect();
            const x = event.clientX - rect.left;
            const nearest = points.reduce((best, point) => Math.abs(point.x - x) < Math.abs(best.x - x) ? point : best);
            const net = nearest.point.netPosition;
            const index = nearest.point.cotIndex;
            tooltip.innerHTML = `<strong>${formatDate(nearest.point.date)}</strong><span>Net: ${formatNumber(net)}</span><span>Long: ${formatNumber(nearest.point.longPosition)} · Short: ${formatNumber(nearest.point.shortPosition)}</span><span>52W index: ${index === null ? "-" : `${Math.round(index)} %`}</span>`;
            tooltip.hidden = false;
            tooltip.style.left = `${Math.min(Math.max(nearest.x, 88), rect.width - 88)}px`;
            tooltip.style.top = `${Math.max(nearest.y - 12, 8)}px`;
        };

        chart.querySelectorAll(".cot-range").forEach(button => button.addEventListener("click", () => {
            range = Number(button.dataset.range);
            chart.querySelectorAll(".cot-range").forEach(item => item.classList.toggle("is-active", item === button));
            draw();
        }));
        chart.querySelectorAll(".cot-series").forEach(button => button.addEventListener("click", () => {
            series = button.dataset.series;
            chart.querySelectorAll(".cot-series").forEach(item => item.classList.toggle("is-active", item === button));
            draw();
        }));
        canvas.addEventListener("mousemove", showTooltip);
        canvas.addEventListener("mouseleave", () => { tooltip.hidden = true; });
        new ResizeObserver(draw).observe(stage);
        draw();
    });
})();
