# Ocenění pěti firem

Na začátku screeneru je samostatné ocenění MSFT, META, AAPL, CTAS a RSG. Výběr vznikl kontrolou skutečně uložených datasetů celého katalogu dne 1. 11. 2026. Jde o technický pilot, ne doporučení těchto akcií. Úplnost dat není pravděpodobností správného ocenění.

## Co obrazovka počítá

- Pásmo Bear/Base/Bull stávajícího desetiletého FCFF DCF. Jde o scénáře, nikoliv statistický interval.
- Hodnotu pro požadovanou výnosnost vlastního kapitálu, výchozí 10 % ročně, nastavitelnou 4–30 %. Používá FCFE, ne FCFF diskontované sazbou akcionáře.
- Limit po dodatečné bezpečnostní rezervě stávajícího modelu, uvedené samostatně.
- Reverzní DCF: počáteční růst tržeb vysvětlující současnou cenu při ostatních nezměněných předpokladech a WACC. Růst postupně konverguje k terminálnímu růstu v desátém roce; nejde o desetileté CAGR.
- Samostatnou kontrolu EPS příštích 12 měsíců × zvolené referenční P/E, pokud uživatel dodá oba údaje, HTTPS zdroj a datum. Rozdíl vůči DCF přes 30 % vyžaduje revizi, nikoliv automatické průměrování.

Reverzní výpočet prohledá počáteční růst −10 až 50 % a řešení zpřesní bisekcí. Při více nalezených řešeních, neplatném modelu či chybějícím průsečíku nevrací vymyšlený růst. Pro každý pokus se znovu počítají investice, pracovní kapitál a cash flow; nestačí přepočítat pouze terminální hodnotu.

## Výnos akcionáře a dluh

Pro výpočet nákupního limitu se předpokládá konstantní nominální dluh, jeho průběžné refinancování za konstantní náklad a nulové čisté nové zadlužování. Při `I = dluh × náklad dluhu × (1 − daň)` je roční `FCFE = FCFF − I`. Terminální hodnota akcionářských cash flow v roce 10 je:

`FCFF_11 / (požadovaný výnos − g) − I / požadovaný výnos`.

Diskontují se explicitní FCFE i tato terminální hodnota. Přičítá se přebytečná hotovost a neprovozní investice, odečítají ostatní nároky a výsledek se dělí počtem akcií. Dluh se podruhé neodečítá. Jde o podmíněnou hodnotu při uvedené politice financování, nikoli garantované investiční IRR. Při změně zadlužování nebo nákladů financování je nutná nová projekce.

## Vlastní výhled a ukládání

První rok růstu, EBIT marži v roce 10 a beta lze zadat pro konkrétní firmu. Vlastní vstupy potřebují HTTPS zdroj bez přihlašovacích údajů a datum publikace nejvýše dnešní, nejstarší dva roky. Výpočet přizná uživatelské předpoklady v auditu; nepovažuje je za ověřený konsenzus.

Nastavení se ukládá odděleně pro uživatele a ticker do existující tabulky `ScreeningDocuments`. Neupravuje sdílené výkazy, denní screening ani historické `ValuationRuns`. Není potřeba databázová migrace. POST vyžaduje přihlášení a antiforgery token. GET pouze čte uložená data a počítá scénáře. Samostatné tlačítko aktualizuje jen pět pilotních firem přes existující poskytovatele.

Bez platného ocenění, aktuální ceny a kvality podkladů alespoň 60/100 se nákupní limit nezobrazuje. Nedostupné údaje se nenahrazují nulou.

## Ověření skutečných dat 1. 11. 2026

První kontrola proběhla nad izolovanou SQL kopií uložených datasetů. Následně byly ceny a výkazy pilotních firem aktualizovány přes existující poskytovatele také v produkci a znovu ověřeny. Ceny pocházely z 30. 10. 2026.

| Firma | Použité období | Roční historie | Úplnost modelových podkladů | Reverzní DCF |
|---|---|---:|---:|---|
| MSFT | 30. 9. 2026 | 11 let | 65/100 | řešení nalezeno |
| META | 30. 6. 2026 | 11 let | 65/100 | řešení nalezeno |
| CTAS | 31. 8. 2026 | 11 let | 60/100 | řešení nalezeno |
| RSG | 30. 9. 2026 | 11 let | 60/100 | řešení nalezeno |
| AAPL | 27. 6. 2026 | 11 let | 70/100 | řešení nalezeno |

Po čerstvém stažení byl z výběru vyřazen také ZTS, protože zdroj neposkytl povinný EBIT; nahradil ho AAPL. Původně zvažované GOOGL a PG chybí některé povinné vstupy, ADBE nesplňuje práh úplnosti. Nebyly doplněny umělými hodnotami ani vybrány místo funkčních kandidátů. Podrobné strojové výsledky jsou dočasný lokální ověřovací výstup, ne součást repozitáře.

Matematické testy ověřují mimo jiné nekonečný konstantní FCFE příklad, soulad s DCF u firmy bez dluhu, vliv požadovaného výnosu, zpětné nalezení známého růstu a odmítnutí více řešení. HTTP testy ověřují formuláře, antiforgery, validaci, reset, srovnávací ocenění a oddělení účtů.

## Meze této verze

Výchozí růst a marže stále vycházejí z historie. Automatický konsenzus, výhled managementu, firemní beta a historické/oborové násobky vyžadují další zdrojovaná data. Technické ověření neprokazuje schopnost předpovídat výnosy. Historická kalibrace vyžaduje data skutečně dostupná k jednotlivým minulým datům, nikoliv dnešní výkazy dosazené zpětně. Banky, REIT a specifické cyklické modely se tímto pilotem neřeší.

Metodické zdroje: [CFA Institute: FCFF a FCFE](https://www.cfainstitute.org/insights/professional-learning/refresher-readings/2026/free-cash-flow-valuation), [Damodaran: růst, marže a reinvestice](https://pages.stern.nyu.edu/~adamodar/New_Home_Page/littlebook/growthvaluedrivers.htm).

## Pravidelná aktualizace

Workflow `price-lab-daily.yml` aktualizuje pouze pět pilotních firem v pracovní dny ve 23:17 UTC a lze jej spustit ručně. Používá `ScreeningWorker --run --pilot-only --force`, existující distribuovaný zámek a ověřuje, že všech pět firem má použitelný limit. Neprovádí databázové migrace ani import insiders. Připojení je v samostatném GitHub Actions secretu `PRICE_LAB_CONNECTION_STRING`; původní ruční workflow celého katalogu se nemění.
