const assert = require('node:assert/strict');
const geometry = require('../../TradingJournal/wwwroot/js/seasonality-geometry.js');

for (const width of [300, 640, 1200, 2400]) {
    for (const day of [0, 1, 7, 90, 182, 233, 300, 363, 364]) {
        const x = geometry.xFor(width, day);
        assert.equal(geometry.dayForX(width, x), day, `width=${width}, day=${day}`);
    }
}

assert.equal(geometry.dayForX(1200, -100), 0);
assert.equal(geometry.dayForX(1200, 5000), 364);
console.log('Seasonality chart geometry tests passed.');
