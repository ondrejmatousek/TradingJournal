# Ocenění akcií ve Screeneru

Screener ukládá vstupní výkazy do `ScreeningMarketCache` a z nich při denním přepočtu vytvoří modelový odhad. Stránka pouze načítá uložený `ScreeningSnapshot`; výpočty nejsou v controlleru ani v JavaScriptu. Denní výsledek se ukládá také do normalizované tabulky `CompanyValuations` (unikátní ticker + datum) a do JSON cache jako fallback.

## Vstupy a omezení

Primárním zdrojem fundamentů je SEC Company Facts. Alpha Vantage slouží jako doplněk nebo fallback. Používáme provozní cash flow, capex, tržby, předchozí tržby, čistý zisk, aktiva, závazky, hotovost, dluh a počet akcií. SEC roční pozorování se ukládají kompaktně v cache a z jejich tržeb/FCF se počítá CAGR. Výpočet se vynechá při chybějícím či záporném FCF, nulových akciích, zastaralých výkazech a pro banky nebo REIT, dokud pro ně nebude přidán sektorový model.

## Valuační metody

* **DCF:** FCF = provozní cash flow − capex. Projekce trvá pět let a růst postupně konverguje k terminálnímu růstu 2 %. Terminální hodnota je `FCF5 × (1 + g) / (WACC − g)`. WACC používá CAPM (US10Y + beta × ERP) a náklad dluhu; při chybějících vstupech se zobrazí fallback a sazba je omezena na 9–20 %. Vždy platí WACC > g.
* **Relativní ocenění:** EPS × sektorový P/E, volitelně forward EPS × forward P/E a EV/EBITDA po převodu enterprise value na equity value. Násobky jsou konzervativní sektorové heuristiky; pokud SEC vstup neposkytne EBITDA nebo forward EPS, metoda se vynechá.
* **FCF kontrola:** FCF na akcii / požadovaný výnos 6 %.

Kompozitní hodnota používá váhy DCF 45 %, P/E 20 %, forward P/E 10 %, EV/EBITDA 15 % a FCF 10 %. Neplatné metody se vynechají a jejich váha se poměrně rozdělí mezi platné metody. Neplatná metoda se vynechá a její váha se poměrně rozdělí mezi platné metody. Bear/Base/Bull tvoří rozpětí konzervativního DCF a kompozitní hodnoty. Výsledky obsahují použitý model, sazby, kvalitu dat, váhy a textové vysvětlení.

## Skóre a zóna

Upside se počítá proti kompozitní hodnotě. Status je Strongly Undervalued (≥35 %), Undervalued (≥15 %), Fairly Valued (>−10 %), Overvalued (>−30 %) nebo Strongly Overvalued. Valuation Score je nelineární kombinace upside a confidence; vysoký upside s nízkou confidence proto nemůže vytvořit falešné skóre 100. Confidence hodnotí počet použitelných metod, historii vstupu a rizikové signály. Nákupní limit je nižší z bear hodnoty a základní hodnoty po rezervě 25–40 %.

Fair Value je modelový odhad, nikoli jistota ani investiční doporučení. Každý výsledek má datum výkazů, datum výpočtu a stav dat; při nedostatku vstupů se zobrazí N/A.

## Aktualizace

GitHub worker jednou denně aktualizuje ceny, SEC fundamenty a kontext. Při novém fundamentálním vstupu se valuation přepočítá a uloží do market cache i historie. Při změně samotné ceny se nemění fundamentální DCF; aktuální upside, sleva a vstupní zóna se přepočítají při načtení snapshotu. Vynucený refresh může fundamentální výpočet zopakovat.


