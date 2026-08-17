# Setup Review workflow pro Codex

Uživatel nic neexportuje ani neimportuje ručně. Při promptu:

`Analyzuj všechny nevyhodnocené setup review trady podle jejich strategie, zapiš výsledek do DB a vypiš stručný souhrn.`

Codex provede interně:

1. `pwsh -File Tools/SetupReview/Invoke-SetupReviewWorkflow.ps1 -Action Export`
2. načte `tmp/setup-review/pending/manifest.json` a vizuálně projde všechny cesty `imageFiles`,
3. použije `TradingJournal/Docs/strategies/petr-mucka/rubric.json` pro `MUCKA`,
4. vytvoří výsledky v paměti nebo do dočasného `tmp/setup-review/results.json`,
5. spustí `pwsh -File Tools/SetupReview/Invoke-SetupReviewWorkflow.ps1 -Action Import`,
6. ověří, že pending počet klesl a že DB řádky mají `ReviewedAt` a odpovídající status.

Script používá connection string z `TRADINGJOURNAL_CONNECTION_STRING`, `appsettings.Development.json`, `appsettings.json` nebo user-secrets. Obrázky se automaticky načtou z `TradeSetupReviewImages` do dočasného adresáře `tmp/setup-review`; uživatel s nimi nepracuje.
