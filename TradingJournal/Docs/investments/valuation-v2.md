# Valuation Engine V2.0

## Rozsah a tok dat

Stejný engine zpracovává celý `InvestmentUniverse` a aktivní záznamy v `InvestmentListings`.
Výpočet neobsahuje výjimky pro AAPL ani cílovou hodnotu převzatou od analytiků.
Databázový záznam stejného symbolu přepisuje definici v původním seznamu; `Enabled=false` jej vyřadí.
Nový záznam potřebuje ticker, název a správný sektor. SEC identifikátor musí být ověřitelný u poskytovatele.

1. Worker stáhne ceny a SEC Company Facts. Katalog zahrnuje také nové tickery z DB.
2. Parser oddělí roční, průběžné a okamžikové údaje; zachová jednotky, data podání, tagy a accession.
3. Engine vybere strategii, validuje vstupy, normalizuje historii a sestaví scénáře.
4. Uloží vstupy a auditní výsledek, následně aktualizuje cache a kompaktní snapshot.
5. UI čte snapshot; kompletní audit načte až po otevření konkrétního tickeru. Nepočítá DCF při zobrazení tabulky.

V2 nezávisí na úplnosti staršího formuláře fundamentální kvality. Chybějící `Assets` nebo `Liabilities`
pro staré kvalitativní skóre proto samo o sobě nezablokuje dostupné vstupy FCFF.
Historie sezónnosti není vstupem DCF. Pro techniku zůstává dosavadní krátká cenová historie.

## Strategie a omezení pokrytí

| Typ | Chování |
|---|---|
| Běžné nefinanční firmy ve známých sektorech | FCFF DCF, pokud jsou splněny datové podmínky |
| Financial Services, banky, pojišťovny | Samostatná strategie; `UnsupportedCompanyType`, žádný průmyslový DCF |
| REIT / Real Estate | Samostatná strategie; bez ověřeného AFFO/NAV žádný DCF |
| Neznámý sektor | Vyžaduje klasifikaci; žádný automatický průmyslový fallback |
| Chybějící výkazy, dluh, akcie nebo čerstvá cena | Výsledek bez fair value s konkrétním důvodem |
| Jiné měny / nepodporovaná taxonomie | Bez domyšlené konverze a bez ocenění |

Zpracování všech tickerů **neznamená dostupnost číselného ocenění všech firem**. Platební sítě a burzy
zařazené do Financial Services jsou zatím také konzervativně vyloučené. Není implementován plný bankovní
ani REIT model; jejich nahrazení generickým DCF by bylo zavádějící.

## Data a účetní konzistence

- SEC USD a shares jsou absolutní jednotky. Display scale z HTML výkazů se nepřenáší do Company Facts.
- Nejnovější dostupné podání vyhrává mezi duplicitními údaji pro stejný interval; alternativní tagy se nesčítají.
- Budoucí podání se nepoužijí. Vyžadují se alespoň tři srovnatelné roční tržby/EBIT a dvě období pracovního kapitálu.
- TTM = poslední FY + aktuální kumulativní YTD − odpovídající předchozí YTD. YTD musí začínat den po FY.
  Chybějící komponenta se nepřevádí na nulu. Odvození obsahuje všechny tři zdrojové fakty.
- Vážené počty akcií v TTM se přepočtou délkami období.
- Preferují se datované aktuální shares upravené poměrem diluted/basic; jinak se použijí vážené diluted shares.
  Obě varianty jsou označeny jako proxy. Nápadný nesoulad splitu/emise zastaví ocenění.
- Dluh = term aggregate **nebo** current + noncurrent; navíc short borrowings **nebo** commercial paper.
- NWC = receivables + inventory − payables. Jde o výslovně označený neúplný odhad;
  ostatní provozní položky nejsou bez klasifikace zahrnuty. Cash ani dluh nejsou součástí NWC.
- SBC zůstává v provozních nákladech; v FCFF se znovu nepřičítá. Odkupy samy nevytvářejí dodatečnou hodnotu.

## Rovnice a prognóza

```
NOPAT = EBIT × (1 − normalizovaná daň)
FCFF = NOPAT + D&A − CapEx − ΔNWC
Ke = risk-free + beta × ERP
WACC = E/(D+E) × Ke + D/(D+E) × Kd × (1 − daň)
PV(FCFF_t) = FCFF_t / (1 + WACC)^t
Terminal reinvestment = NOPAT_11 × g / ROIC
Terminal FCFF = NOPAT_11 × (1 − g/ROIC)
TV_10 = Terminal FCFF / (WACC − g)
EV = suma PV(FCFF_1..10) + PV(TV_10)
Equity = EV − debt + excess cash + nonoperating investments − other claims
Fair value/share = Equity / diluted share proxy
Buy Below = Base fair value × (1 − margin of safety)
```

Počáteční růst je medián 3Y/5Y CAGR a posledního FY růstu, omezený verzovanou politikou.
Při kratší historii se použije dostupné období a je to v auditu uvedeno. Růst postupně klesá k terminálnímu,
marže se vrací k pětiletému mediánu. Daň je historický medián s limity; nejde o ověřenou budoucí statutární sazbu.

Roky 1–5 používají historické poměry D&A/Revenue a CapEx/Revenue, s minimální reinvesticí
`NOPAT × max(growth,0) / MaximumForecastRoic`. Roky 6–10 přecházejí k reinvestici `NOPAT × g / terminalROIC`.
Jde o odhadované ekonomické předpoklady, ne o firemní guidance.

Bear/Base/Bull mění růst, cílovou marži, WACC a terminální růst; každý má celý desetiletý výpočet.
Citlivost 5×5 přepočítává také přechodovou reinvestici, nejen terminální jmenovatel.
Neplatné spread WACC/g/ROIC, záporná equity nebo neuspořádané scénáře hodnotu nezveřejní.

## Předpoklady, nejistota a násobky

Konfigurace je v sekci `ValuationV2`; její úplná hodnota je uložená v každém reportu.
Výchozí ERP 5 %, beta 1, fallback risk-free 4,5 %, spread dluhu 2 %, terminální růst 2,5 %, ROIC 12 %,
maximální explicitní ROIC 60 %, provozní hotovost 2 % tržeb. Nejsou to měřená firemní data.
Při dostupnosti se používá aktuální zdrojovaná US Treasury 10Y sazba a účetní úrok/dluh jako odhad Kd.
WACC nemá minimální hranici 9 %.

DataQuality začíná na 100 a odečítá neúplnost: FY místo TTM 10, kratší historie 10,
částečné NWC 10, odhady financování 10, share proxy 5, fallback sazba 5, fallback daň 10,
neznámé krátkodobé dluhy 5, chybějící investiční položky 5.
Není to pravděpodobnost správnosti ceny. Současná datová cesta má úmyslně stav `LimitedData`.

Nejistota závisí na rozpětí scénářů, stabilitě růstu a kvalitě dat. Rezerva pro Low/Medium/High/VeryHigh
je 10/15/25/35 %. Nízká kvalita nemůže vytvořit doporučení ke vstupu. Technická zóna a fundamentální limit
jsou dvě oddělené podmínky. Cena pod Base sama o sobě není nákupní signál.

P/E, EV/EBITDA a P/FCF jsou popisné kontroly aktuálních násobků; bez zdrojované historické/peer reference
je referenční násobek a implikovaná hodnota N/A. Forward EPS a externí fair value nejsou vymyšlené.
Tyto metody se **neváží do výsledného DCF**. Firemní beta, peer dataset, úplné operating NWC,
ověřená lease klasifikace a analytické forecasty zůstávají datovým omezením této verze.

## Ukládání a nasazení

Migrace `20260910120000_ValuationV2` přidává `ValuationInputs`, `ValuationRuns`, `InvestmentListings`.
Původní `CompanyValuations` nemaže ani nepřepisuje. V2 historie je oddělená a UI ji označuje verzí.
Obsahové hashe zahrnují vstupy; identita reportu také ticker, sektor, datum, cenu, sazbu a politiku.
Opakované uložení stejného výsledku jej nepřepisuje. Snapshot má nový klíč `screening-results-v2`.
Před prvním novým během může UI správně vrátit „hodnocení zatím není připravené“ místo starých výsledků pod novým názvem.

Po nasazení kódu a migrace spustit Daily investment data s force, pokud dnešní běh už skončil.
Další denní běhy aktualizují vstupy a výpočty automaticky. Nasazení samo nespouští živý import.

## Ověření

- Release build: 0 chyb, 0 upozornění.
- 564 testů prošlo: všech 200 tickerů má explicitní routing i chování bez dat, nové symboly nemají speciální koeficienty,
  testují se jednotky, škálování firem, absence vstupů, TTM, budoucí data, split mismatch, FCFF, WACC, bridge a ukládání.
- AAPL fixture je ručně transkribovaný publikovaný FY2025/2024/2023 výkaz, nikoli živá odpověď SEC API.
  Je použit k nezávislé matematické kontrole; nelze jej vydávat za aktuální TTM ocenění produkce.
- Skutečná aplikace ověřena proti izolovanému PostgreSQL schématu: migrace, přihlášení, screener HTTP 200,
  čtení snapshotu a detail uloženého reportu se třemi scénáři. Testovací schéma bylo odstraněno.
- JavaScript prošel syntaktickou kontrolou. Plná vizuální kontrola všech rozlišení v prohlížeči není tímto testem nahrazena.
- Živý import všech poskytovatelů není z této sítě potvrzený; SEC vracela 403. To není nahrazeno tvrzením,
  že všech 200 firem má kompletní reálná data.

Reprodukce matematického auditu:

```powershell
$env:VALUATION_AUDIT_OUTPUT = "$pwd/tmp/valuation/aapl-v2-report.json"
dotnet test TradingJournal.Tests/TradingJournal.Tests.csproj -c Release --filter FullyQualifiedName~AaplValuationV2Tests
python Tools/ValuationVerification/verify_audit.py tmp/valuation/aapl-v2-report.json
```

Zdroj kontrolních výkazů: [Apple 2025 Form 10-K](https://www.sec.gov/Archives/edgar/data/320193/000032019325000079/aapl-20250927.htm).
Metodika terminální reinvestice: [Damodaran — terminal value and excess returns](https://pages.stern.nyu.edu/~adamodar/New_Home_Page/valquestions/termvalueexreturns.htm).
