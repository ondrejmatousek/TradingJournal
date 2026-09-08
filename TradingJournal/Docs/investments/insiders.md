# Insiders – architektura a pravidla

Modul sleduje společnosti z `InvestmentUniverse` a používá oficiální SEC EDGAR. Denní worker načte pro každý CIK submissions JSON, vybere nové formuláře 4, 4/A, 5 a 5/A a stáhne jejich ownership XML. První běh prochází 365 dní, další běhy překrývají 14 dní, aby zachytily zpoždění a opravy. `AccessionNumber` je unikátní, takže opakovaný běh filing nezdvojí.

Do hlavní tabulky vstupují pouze nedervátové transakce s kódem `P`, směrem `A`, kladným počtem kusů a uvedenou kladnou cenou. Granty (`A`), prodeje (`S`) a exercise opcí (`M`) se ukládají pro audit a budoucí detail, ale neprezentují se jako nákup za vlastní peníze. Form 4/A se uchovává a označuje jako oprava; bez spolehlivého odkazu na původní řádek se automaticky nemaže předchozí filing.

Databáze je normalizovaná do `InsiderFilings`, `InsiderReportingOwners`, `InsiderTransactions` a `InsiderImportRuns`. Zdrojová URL, datum podání a importu zůstávají u každého filingu. Vypočtená částka a skóre se neukládají, ale počítají se z původních polí, takže změna metodiky nevyžaduje přepis historie.

Skóre významnosti používá roli (25 %), částku (30 %), nárůst pozice (20 %), počet různých insiderů nakupujících stejnou firmu za 30 dní (15 %) a počet nákupních dní stejného insidera za 180 dní (10 %). Chybějící složka nedostane body a pokrytí se ukazuje zvlášť. Skóre je priorita pro další analýzu, nikoliv očekávaný výnos.

Zdroje: [SEC submissions API](https://www.sec.gov/search-filings/edgar-application-programming-interfaces), [SEC insider transaction codes a lhůty](https://www.sec.gov/files/forms-3-4-5.pdf), [SEC ownership XML specifikace](https://www.sec.gov/info/edgar/ownershipxmltechspec.htm), [SEC fair-access pravidla](https://www.sec.gov/about/developer-resources).
