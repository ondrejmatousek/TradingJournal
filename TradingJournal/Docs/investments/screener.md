# Investiční screener

## Použití

Investování → Akciový screener (`/Screening`). Výběr obsahuje 200 akcií obchodovaných v USA ve 12 sektorových skupinách; nejde o úplný index ani reprezentativní historický vzorek. Seznam je v `Services/Screening/ScreeningModels.cs` a sdílí jej původní EMA screener.

První načtení používá uloženou historii Trading → Sezónnost a cache nového screeneru. Aktualizovat data obnoví makro a následně jednotlivé tituly s ukazatelem průběhu. Zastavení dokončí právě běžící titul. Detail umožňuje aktualizovat jeden titul. Plánované běhy používají úspěšné výsledky 12 hodin a výsledky s chybou 15 minut; ruční aktualizace tuto lokální cache obchází. Limity poskytovatelů platí vždy.

## Zdroje a nastavení

- Ceny: existující `IUnderEma200HistoryProvider` (Yahoo, Alpha Vantage, Stooq); historie jednotlivých poskytovatelů se neslepují kvůli rozdílným úpravám cen.
- Sezónnost: existující `SeasonalityPrices`, pokud není novější souvislá historie z obnovy. Cenové výnosy, nikoliv total return. Automatické vyhodnocení úprav o splity/dividendy není součástí této verze.
- Fundamenty: SEC Company Facts, standardní US GAAP koncepty v USD, srovnatelné celé roční období. Neúplná nebo nestandardní data se nevymýšlejí. Nastavte `Screening:SecUserAgent` na identifikaci provozovatele aplikace s kontaktním e-mailem dle požadavků SEC; údaj nevkládejte do repozitáře. Výpadky a blokace SEC jsou viditelné v detailu a výkazy lze zadat ručně se zdrojem.
- Makro: FRED DGS10 a UNRATE, poslední dostupná hodnota a hodnota přibližně o 3 měsíce dříve. Denní řada expiruje za 10 dní, měsíční za 65 dní.
- Geopolitika / externí analýzy: vlastní datované záznamy s autorem, HTTPS zdrojem, horizontem, tezí a rizikem. Aplikace nestahuje obsah vložených URL a nepředstírá automatické procházení zpráv. Analýzy autorů se započítávají pouze po uživatelově označení prověření jejich historie. Odkazy se deduplikují. Makro a geopolitika expirují nejpozději po 90 dnech, analýzy po 180 dnech nebo po skončení zvoleného horizontu.

SEC API: https://www.sec.gov/search-filings/edgar-application-programming-interfaces
FRED: https://fred.stlouisfed.org/series/DGS10 a https://fred.stlouisfed.org/series/UNRATE

## Skóre

Kvalita 25 %, ocenění 30 %, makro 15 %, sezónnost 10 %, technika 10 %, geopolitika 5 %, analýzy 5 %. Dílčí skóre je 0–100, null znamená nevyhodnoceno. Pokrytí je součet vah dostupných složek, **není** statistická spolehlivost. Celkové skóre je dostupné jen s kvalitou, aktuálním oceněním a alespoň 80 % pokrytí. Chybějící složky nepřerozdělují váhu na dostupné. Příznaky ztráty, nekladného provozního cash flow a záporného vlastního kapitálu mění verdikt na nutnou revizi.

Kvalita je transparentní počáteční heuristika růstu, marží a rozvahy, nikoli sektorově normalizovaný rating. Ocenění používá pětiletý DCF kompozit CFO − capex, P/E, volitelný forward P/E, volitelné EV/EBITDA a FCF výnos. Výchozí váhy platných metod jsou 45/20/10/15/10 a při chybějící metodě se přerozdělují. Růst vychází z dostupné víceleté historie tržeb a FCF, je omezen na −5 až +10 %, WACC používá CAPM a konzervativní fallbacky. Bear/Base/Bull tvoří tři scénáře; hotovost a dluh upravují hodnotu vlastního kapitálu. Není modelováno budoucí ředění, sektorové násobky jsou heuristické a banky/REIT vyžadují jiný model. Finanční společnosti a REIT nedostávají tento univerzální DCF. Vlastní výkazy přebíjejí SEC; vymazání všech polí vrací automatický zdroj.

Váhy nejsou historicky kalibrované. Walk-forward test s point-in-time daty, historickým složením univerza, korporátními akcemi a náklady není implementován. Zobrazené skóre není pravděpodobnost výnosu nebo investiční doporučení.

## Uložení a provoz

Cache, osobní výzkum a stav denní aktualizace se ukládají do SQL tabulky `ScreeningDocuments`. JSON dokumenty mají SHA-256 klíče; osobní klíč obsahuje ID přihlášeného uživatele. Kontroler vyžaduje přihlášení a mutace antiforgery token. Starší JSON v `App_Data/screening` se při prvním čtení importuje pouze pokud SQL záznam ještě neexistuje. Při výpadku SQL se nepřepíná zpět na soubory. Zálohujte staré soubory před prvním nasazením migrace.

Samostatný worker i web používají stejnou databázi. Nasazení, secrets, firewall a aktivace denního plánu jsou popsané v `Tools/ScreeningWorker/README.md`. Na Azure se interní plánovač webu vypíná automaticky; aktualizaci zajišťuje nakonfigurovaný GitHub Actions workflow. Samotný push workflow denní plán neaktivuje.

## Ověření

`dotnet build TradingJournal/TradingJournal.csproj`
`dotnet test TradingJournal.Tests/TradingJournal.Tests.csproj`
`dotnet run --project TradingJournal/TradingJournal.csproj --launch-profile http`

Testy ověřují neúplné pokrytí, stárnutí cen/fundamentů, výpočet DCF, vyloučení budoucích dat, minimum pozorování a platnost/duplicity rozborů.



## Automatická denní aktualizace

Mimo Azure může běžet interní `ScreeningUpdateHostedService`, ve výchozím nastavení v 06:00 Europe/Prague, když aplikace běží. Jeho konfigurace je `Screening:Schedule` (Enabled, Hour, Minute, TimeZone, External). Při startu doplní poslední zmeškaný termín a uchovává stav v SQL. Pro Azure Free se používá externí GitHub workflow v 06:17 českého času; podrobnosti a aktivace jsou v README workeru.
