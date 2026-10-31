# TradingJournal Setup Review

When the user asks to analyze all unreviewed setup review trades, use the existing workflow in `Tools/SetupReview`.

1. Run `pwsh -File Tools/SetupReview/Invoke-SetupReviewWorkflow.ps1 -Action Export`.
2. Read `tmp/setup-review/pending/manifest.json` and visually inspect every image listed by every pending review.
3. For strategy code `MUCKA` or aliases mapped to it, use `TradingJournal/Docs/strategies/petr-mucka/rubric.json` as the stable rules source. Do not reread the source PDFs during normal reviews.
4. COT and seasonality modules have been removed. Reviews must not query their former services or tables, and the manifest no longer supplies automatic COT context. Evaluate supplied screenshots and the stable strategy rubric without inventing missing macro data.
5. Do not infer values that are not visible. Use `Nelze určit z dodaných screenshotů.` in the analysis and add the missing item to `missingInformation`.
6. Create the structured result JSON using the contract in `TradingJournal/ViewModels/SetupReviewViewModel.cs`, with one result per `tradeId`.
7. Write the results temporarily to `tmp/setup-review/results.json` and run `pwsh -File Tools/SetupReview/Invoke-SetupReviewWorkflow.ps1 -Action Import`.
8. Verify that imported records no longer have status `Nevyhodnoceno`, have `ReviewedAt`, and contain `AiReviewJson`.

The user should only need to write the short analysis prompt. Temporary files are implementation details and may be deleted after verification.
