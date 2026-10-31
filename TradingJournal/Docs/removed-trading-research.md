# Odstranění sezónnosti a COT ze sekce Trading Journal

Odstraněny byly oba controllery a stránky, navigace, klientské skripty a styly,
sezónní výpočtová/download služba, oba hosted updatery, jejich DI registrace,
COT modely, importy a napojení na Turso. Sdílený worker již nespouští sezónní
ani COT aktualizaci a jeho workflow nepotřebuje `TURSO_DATABASE_URL` ani
`TURSO_AUTH_TOKEN`. Lokální naplánovaná úloha ani publikovaný worker nebyly
při kontrole nalezeny.

Setup Review a export/import výsledků zůstávají funkční, ale nepřidávají již
automatický COT kontext. Existující výsledky a jejich původní `AiReviewJson`
se nepřepisují. Stabilní strategická pravidla se nemění; chybějící podklady
se nesmějí domýšlet. Pokyny pro review byly přizpůsobeny odstraněným službám.

Investiční screener používal část stejných cenových dat a seznam názvů aktiv.
Tato sdílená data jsou oddělena do obecných typů `MarketPrice`,
`MarketPriceEntity` a `InvestmentAssetCatalog`. Běžné získávání cen investiční
části zůstává přes její vlastní poskytovatele a cache; Turso již nevyužívá.
Investiční výpočty nad cenami nejsou součástí odstraněné obchodní stránky.

Migrace `20261031100000_RemoveTradingResearch` pro SQL Server i PostgreSQL:

- odstraní tabulky `CotReports` a `CotImportRuns` s odvozenými importovanými daty;
- přejmenuje sdílenou tabulku `SeasonalityPrices` na `MarketPrices` bez změny
  uložených cen, včetně přejmenování indexu a primárního klíče;
- nemění deníky, obchody, obrázky ani Setup Review.

Historické EF migrace se ponechávají, aby šlo vytvořit novou databázi a zachovat
existující migrační historii. Návrat migrace obnoví názvy a prázdné schéma COT,
neobnovuje odstraněná stažená COT data. Samostatná externí Turso databáze ani
jiné zdroje mimo tuto aplikaci se tímto kódem nemažou.

Ověření zahrnuje regresní testy deníků, Setup Review a screeneru, HTTP 404 pro
odstraněné cesty a skripty, skutečné spuštění a migraci na izolovaném SQL
Serveru a vratnou zkoušku PostgreSQL Up/Down v dočasném schématu. U obou
poskytovatelů se kontroluje zachování sdílených cenových dat.
