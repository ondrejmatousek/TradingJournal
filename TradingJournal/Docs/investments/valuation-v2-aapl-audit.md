# Kontrolní výpočet AAPL — V2.0

Toto je audit fixního FY2025/2024/2023 datasetu. Není to aktuální TTM ocenění ani cílová cena. Vstupní cena 315,33999634 USD pochází z předchozího auditovaného cache; neproběhl nový živý download. Stejný engine se používá pro všechny podporované tickery.

Výsledek: Bear **83.03**, Base **101.70**, Bull **127.22 USD**. Kvalita dat 50/100, nejistota VeryHigh, rezerva 35 %, limit 66.11 USD.

## Skutečné vstupy

| Metrika | Hodnota | Jednotka | Od | Do | Podáno | Tag |
|---|---:|---|---|---|---|---|
| Revenue | 416161000000 | USD | 2024-09-29 | 2025-09-27 | 2025-10-31 | Revenues |
| Ebit | 133050000000 | USD | 2024-09-29 | 2025-09-27 | 2025-10-31 | OperatingIncomeLoss |
| Tax | 20719000000 | USD | 2024-09-29 | 2025-09-27 | 2025-10-31 | IncomeTaxExpenseBenefit |
| Pretax | 132729000000 | USD | 2024-09-29 | 2025-09-27 | 2025-10-31 | IncomeLossFromContinuingOperationsBeforeIncomeTaxesExtraordinaryItemsNoncontrollingInterest |
| Da | 11698000000 | USD | 2024-09-29 | 2025-09-27 | 2025-10-31 | DepreciationDepletionAndAmortization |
| CapEx | 12715000000 | USD | 2024-09-29 | 2025-09-27 | 2025-10-31 | PaymentsToAcquirePropertyPlantAndEquipment |
| Ocf | 111482000000 | USD | 2024-09-29 | 2025-09-27 | 2025-10-31 | NetCashProvidedByUsedInOperatingActivities |
| NetIncome | 112010000000 | USD | 2024-09-29 | 2025-09-27 | 2025-10-31 | NetIncomeLoss |
| BasicShares | 14948500000 | shares | 2024-09-29 | 2025-09-27 | 2025-10-31 | WeightedAverageNumberOfSharesOutstandingBasic |
| DilutedShares | 15004697000 | shares | 2024-09-29 | 2025-09-27 | 2025-10-31 | WeightedAverageNumberOfDilutedSharesOutstanding |
| Cash | 35934000000 | USD | 2025-09-27 | 2025-09-27 | 2025-10-31 | CashAndCashEquivalentsAtCarryingValue |
| ShortInvestments | 18763000000 | USD | 2025-09-27 | 2025-09-27 | 2025-10-31 | MarketableSecuritiesCurrent |
| LongInvestments | 77723000000 | USD | 2025-09-27 | 2025-09-27 | 2025-10-31 | MarketableSecuritiesNoncurrent |
| Receivables | 39777000000 | USD | 2025-09-27 | 2025-09-27 | 2025-10-31 | AccountsReceivableNetCurrent |
| Inventory | 5718000000 | USD | 2025-09-27 | 2025-09-27 | 2025-10-31 | InventoryNet |
| Payables | 69860000000 | USD | 2025-09-27 | 2025-09-27 | 2025-10-31 | AccountsPayableCurrent |
| LongDebt | 78328000000 | USD | 2025-09-27 | 2025-09-27 | 2025-10-31 | LongTermDebtNoncurrent |
| CurrentDebt | 12350000000 | USD | 2025-09-27 | 2025-09-27 | 2025-10-31 | LongTermDebtCurrent |
| CommercialPaper | 7979000000 | USD | 2025-09-27 | 2025-09-27 | 2025-10-31 | CommercialPaper |

Zdroj a accession každé položky jsou ve fixture `TradingJournal.Tests/Fixtures/aapl-fy2025-facts.json`. Žádná metoda nemá váhu nastavenou podle požadované ceny: výsledkem je samotný FCFF DCF, relativní metody mají váhu 0 a bez reference hodnotu N/A.

## Předpoklady

| Název | Hodnota | Jednotka | Odhad | Zdroj / důvod |
|---|---:|---|---|---|
| Tax | 0.1561000233558604374326635475 | ratio | False | SEC Tax/Pretax history — Normalized tax applies to NOPAT and debt tax shield. |
| RevenueCagr3Y | 0.04215218532463 | ratio | False | SEC annual history — Calendar-day CAGR; shorter available history used when full window is unavailable. |
| OperatingNwcRevenue | -0.0654135138207326554895720668 | ratio | True | SEC receivables/inventory/payables — Partial operating NWC; no cash or financing items. |
| InitialRevenueGrowth | 0.04215218532463 | ratio | True | Historical model — No external forward estimates. Ten-year projection is a scenario, not a prediction. |
| TerminalMargin | 0.3151022287007556868311020753 | ratio | True | SEC five-year median — Mean reversion. |
| DaRevenue | 0.0292684798035981433886992213 | ratio | True | SEC five-year median — Explicit period asset intensity. |
| CapExRevenue | 0.0285923007683577494553661114 | ratio | True | SEC five-year median — Reinvestment checked against implied ROIC. |
| TerminalRoic | 0.12 | ratio | True | ValuationOptions — Stable reinvestment=g/ROIC; growth is not free. |
| MaximumForecastRoic | 0.60 | ratio | True | ValuationOptions — Explicit-period reinvestment floor NOPAT × positive growth / maximum ROIC; estimated guardrail, not measured ROIC. |
| RiskFree | 0.045 | ratio | True | ValuationOptions.FallbackRiskFree — USD nominal risk-free rate. |
| Beta | 1 | ratio | True | ValuationOptions.FallbackBeta — Estimated beta; a regression/bottom-up company beta is not available. |
| ERP | 0.05 | ratio | True | ValuationOptions.Erp — Versioned equity risk premium policy. |
| CostOfDebt | 0.065 | ratio | True | Risk-free + configured spread — Accounting approximation; not current bond yield. |
| Debt | 98657000000 | USD | False | SEC balance sheet — Term aggregate OR current+noncurrent; short borrowing OR commercial paper, never both. |
| OperatingCashReserve | 8323220000.00 | USD | True | ValuationOptions.OperatingCashRevenue — Cash reserve retained in operations; only excess enters bridge. |
| NonOperatingInvestments | 96486000000 | USD | True | SEC marketable securities — Assumed liquid non-operating securities, no cash-equivalent duplication; strategic investments excluded. |
| ShareCount | 15004697000 | shares | True | https://www.sec.gov/Archives/edgar/data/320193/000032019325000079/aapl-20250927.htm — Period weighted diluted shares (fallback) |
| EquityWeight | 0.9795751269362908313469337137 | ratio | True | Market price × share basis — Market capitalization proxy; debt book value approximates market value. |
| WACC | 0.0941800128025401683533321860 | ratio | True | CAPM and market equity weights — No hard 9% floor; estimated beta/debt cost. |

## Base — projekce v mld. USD

| Rok | Tržby | EBIT | NOPAT | D&A | CapEx | ΔNWC | FCFF | PV FCFF |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| 1 | 433.703096 | 138.458595 | 116.845205 | 12.693830 | 24.907675 | -4.005043 | 108.636404 | 99.285677 |
| 2 | 451.158078 | 143.823260 | 121.372446 | 13.204711 | 22.487840 | -1.141792 | 113.231108 | 94.577581 |
| 3 | 468.455744 | 149.121768 | 125.843857 | 13.710987 | 22.884035 | -1.131501 | 117.802311 | 89.926456 |
| 4 | 485.523831 | 154.331368 | 130.240238 | 14.210544 | 23.235821 | -1.116484 | 122.331445 | 85.345965 |
| 5 | 502.288480 | 159.428932 | 134.542072 | 14.701220 | 23.540525 | -1.096635 | 126.799402 | 80.848751 |
| 6 | 518.674734 | 164.391123 | 138.729665 | 15.180821 | 20.895027 | -1.071882 | 134.087342 | 78.136709 |
| 7 | 534.607071 | 169.194562 | 142.783287 | 15.647136 | 27.745726 | -1.042190 | 131.726888 | 70.154089 |
| 8 | 550.009954 | 173.816006 | 146.683323 | 16.097955 | 34.889143 | -1.007557 | 128.899693 | 62.739588 |
| 9 | 564.808411 | 178.232527 | 150.410425 | 16.531084 | 42.297521 | -0.968019 | 125.612007 | 55.876881 |
| 10 | 578.928621 | 182.421699 | 153.945667 | 16.944361 | 49.940027 | -0.923653 | 121.873653 | 49.547535 |

## Rekonciliace (absolutní USD)

| Položka | Hodnota |
|---|---:|
| terminalNopat | 157794309031.81202828714860982 |
| terminalReinvestment | 32873814381.627505893155960379 |
| terminalFcff | 124920494650.18452239399264944 |
| terminalValue | 1805731013764.6817784080716279 |
| pvTerminal | 734117004572.06181016425237077 |
| pvExplicit | 766439234812.11054932665102745 |
| enterpriseValue | 1500556239384.1723594909033982 |
| debt | 98657000000 |
| excessCash | 27610780000.00 |
| investments | 96486000000 |
| otherClaims | 0 |
| equityValue | 1525996019384.1723594909033982 |
| shares | 15004697000 |
| fairValue | 101.70122191632209297468008839 |

`(766439234812.11054932665102745 + 734117004572.06181016425237077 − 98657000000 + 27610780000.00 + 96486000000) / 15004697000 = 101.70122191632209297468008839 USD`

Nezávislý Python Decimal výpočet z výkazů a předpokladů zrekonstruoval všech deset let, terminální hodnotu, bridge i rezervu. C# test navíc kontroluje parsování a uložení/načtení. Nejde o test proti pevně předepsané cílové ceně.

Nižší výsledek není kalibrován směrem k Morningstar. Omezený historický dataset, odhad beta, marží, růstu a reinvestic mohou dát významně jiný výsledek než profesionální analytik s vlastním forecastem. Model je označen LimitedData; tuto kontrolní hodnotu nelze prezentovat jako jednoznačnou aktuální fair value Apple.
