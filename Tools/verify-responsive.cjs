// Run with PLAYWRIGHT_MODULE pointing to an installed Playwright package, or npm's normal resolution.
const { chromium } = require(process.env.PLAYWRIGHT_MODULE || 'playwright');
const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const base = process.env.BASE_URL || 'http://127.0.0.1:5197';
(async () => {
  const browser = await chromium.launch({ channel: 'chrome', headless: true });
  try {
    const page = await browser.newPage();
    for (const width of [320, 375, 768, 1024, 1440]) {
      await page.setViewportSize({ width, height: 900 });
      const response = await page.goto(base + '/Identity/Account/Login');
      assert.equal(response.status(), 200);
      assert(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1), `Login overflow at ${width}`);
      const toggle = page.locator('[aria-controls="app-navigation"]');
      if (width < 992) {
        await toggle.click();
        await page.waitForFunction(() => document.querySelector('#app-navigation').classList.contains('show'));
        assert.equal(await toggle.getAttribute('aria-expanded'), 'true');
        await page.locator('#app-navigation a').first().focus();
        await page.keyboard.press('Escape');
        await page.waitForFunction(() => !document.querySelector('#app-navigation').classList.contains('show'));
        assert(await toggle.evaluate(el => el === document.activeElement));
      } else assert.equal(await toggle.isVisible(), false);
    }
    // Layout fixture exercises authenticated components without creating accounts or loading customer data.
    await page.goto(base + '/Identity/Account/Login');
    for (const file of ['screening.css', 'insiders.css', 'investment-planner.css', 'responsive.css'])
      await page.addStyleTag({ url: base + '/css/' + file });
    await page.evaluate(() => {
      document.body.className = 'journal-shell app-section-shell';
      const main = document.querySelector('main');
      main.parentElement.className = 'app-workspace';
      main.className = 'app-workspace-main';
      const aside = document.createElement('aside'); aside.className = 'app-sidebar'; aside.textContent = 'Trading'; main.before(aside);
      main.innerHTML = `<section class="screening-page"><h1>Akciový screener</h1><div class="screening-filters"><label>Hledat<input placeholder="Ticker nebo firma"></label></div>
      <div class="screening-table"><table><thead><tr>${Array.from({length:12}, () => '<th>Hodnocení akcie</th>').join('')}</tr></thead><tbody><tr>${Array.from({length:12}, () => '<td>123 456 USD</td>').join('')}</tr></tbody></table></div>
      <dialog id="test-dialog"><div class="screening-dialog-header"><h2>Zdůvodnění hodnocení firmy</h2><button>Zavřít</button></div><div class="research-fields"><label>Tržby<input value="1000000"></label><label>Zdroj<input value="https://example.com"></label></div></dialog></section>
      <section class="journal-page"><div class="invoice-form-grid invoice-item-input"><input name="Items[0].Description"><input name="Items[0].Quantity"><input name="Items[0].Unit"><input name="Items[0].UnitPrice"><input name="Items[0].VatRate" data-vat-only class="d-none"><button>Odebrat</button></div></section>`;
    });
    await page.waitForFunction(() => document.querySelector('.invoice-field-wrapper'));
    for (const width of [320, 375, 768, 1024, 1440]) {
      await page.setViewportSize({ width, height: 900 });
      assert(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1), `Workspace overflow at ${width}`);
      if (width < 992) assert.equal(await page.locator('.app-sidebar').isVisible(), false);
      await page.locator('.screening-table').evaluate(el => el.scrollLeft = 500);
      assert(await page.locator('.screening-table').evaluate(el => el.scrollWidth <= el.clientWidth || el.scrollLeft > 0));
      await page.evaluate(() => document.querySelector('#test-dialog').showModal());
      assert(await page.locator('#test-dialog').evaluate(el => el.getBoundingClientRect().right <= innerWidth && el.scrollWidth <= el.clientWidth + 1), `Dialog overflow at ${width}`);
      await page.evaluate(() => document.querySelector('#test-dialog').close());
    }
    await page.setViewportSize({ width: 375, height: 900 });
    await fs.mkdir('tmp/responsive', { recursive: true });
    await page.screenshot({ path: 'tmp/responsive/mobile-components.png', fullPage: true });
    console.log('PASS: live login/navigation and component layout at 320, 375, 768, 1024, 1440 px.');
  } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
