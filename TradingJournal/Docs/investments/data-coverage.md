# Audit dat investičního screeneru

Ve screeneru použij **Změřit pokrytí všech akcií**. Kontrola čte uložená data pro celý aktuální katalog, nezávisle na stránkování a filtrech. Nevolá zdroje výkazů. Ukazuje zvlášť aktuální cenu, 252 navazujících cen, výkazy a použitelné ocenění. Neúplnost není negativním skóre firmy.

Stejnou kontrolu lze spustit lokálně:

```powershell
dotnet build Tools/ScreeningWorker/ScreeningWorker.csproj -o tmp/coverage-worker
dotnet tmp/coverage-worker/ScreeningWorker.dll --audit --local
```

Výsledkem je `coverage-audit.json` v pracovním adresáři. Obsahuje všechny tickery, zdroje a období výkazů, stav kontrol a zaznamenané chyby poskytovatelů. Starší uložená data nemusí mít původní zdroj cen ani přesný HTTP stav; tyto informace se zaznamenají při nové aktualizaci. `retrievedAt` znamená poslední pokus o aktualizaci, nikoli datum publikace výkazu.

## Výchozí lokální měření 16. 9. 2026

Před novým stažením výkazů: 200 akcií, aktuální cena 197, souvislá historie 197, základní vstupy výkazů 29, použitelné ocenění s aktuálními fundamenty 13, všechny kontroly 13. Samotný odhad s confidence alespoň 60 existoval u 15 akcií, ale u dvou nebyly vyhovující fundamenty. Jde o výchozí stav, nikoli výsledek plošné opravy dat. Celkem 34 finančních společností a REIT potřebuje samostatný model.

## Zdroje a zachování údajů

- SEC zůstává primární. FMP se zkouší i po HTTP chybě, časovém limitu, cooldownu nebo nenalezeném tickeru, a také při neúplných vstupech.
- Nastav `Screening:SecUserAgent` s kontaktem a volitelně `FinancialModelingPrep:ApiKey` v User Secrets / prostředí. Přítomnost klíče nezaručuje oprávnění plánu k jednotlivým endpointům. Chyby HTTP jsou součástí auditu; klíče nejsou jeho součástí.
- Částečně dostupné výkazy se zachovávají. Doplňování mezi zdroji vyžaduje stejné období, měnu a jednotky. Celé zadlužení se nekombinuje s jeho složkami z jiného zdroje. Úplný alternativní dataset může nahradit neúplný primární jako celek, pokud není starší.
- FMP zachovává nuly, skutečné datum podání, měnu, zdroj a označení odvozeného začátku fiskálního roku. Vážený průměr akcií se nevydává za aktuální počet. Chybějící datum podání se nedoplňuje odhadem.
- Ocenění V2 zůstává v `ValuationRuns`; audit a metadata trhu jsou v JSON cache `ScreeningDocuments`. Není nutná nová databázová migrace.

Pro aktualizaci uložených výkazů použij **Aktualizovat** ve screeneru nebo existující denní worker. Nový build je třeba použít také v publikované kopii workeru. Samotné spuštění auditu ceny ani výkazy neobnovuje.

Dokumentace zdrojů: [SEC Company Facts API](https://www.sec.gov/search-filings/edgar-application-programming-interfaces), [FMP Income Statement](https://site.financialmodelingprep.com/developer/docs/stable/income-statement).
