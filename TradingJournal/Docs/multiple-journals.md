# Více obchodních deníků pod jedním účtem

Domovská adresa a záložka **Trading Journal** vedou na `/TradingJournals`.
Uživatel vidí pouze své deníky, může je vytvářet, otevřít, upravit jejich název
a popis a smazat prázdný deník. Neprázdný deník se nesmaže; cizí ani neexistující
deník vrací HTTP 404. Vytváření a úpravy nepřebírají vlastníka z formuláře.

Obchody, kalendář, statistiky, analýza a obrázky jsou oddělené podle deníku.
Adresa `/journals/{journalId}/{action}/{id?}` drží výběr přímo v URL, takže
různé záložky prohlížeče mohou bezpečně pracovat s různými deníky.
Přidání obchodu vždy použije deník z ověřené cesty. Úprava obchodu jej nemůže
přesunout pod jiný účet nebo deník pomocí podvrženého formuláře.

Strategie, poznámky ke strategiím, Setup Review a materiály
ke kvalitě obchodování zůstávají společné nástroje pod uživatelským účtem.
Investiční část není závislá na zvoleném deníku.

## Migrace a zachování dat

Migrace `20261031090000_AddTradingJournals` je v obou existujících historiích
pro SQL Server i PostgreSQL. Přidává `TradingJournals` a cizí klíč
`JournalEntries.JournalId`. Pro každého vlastníka existujících obchodů založí
deník **Můj trading journal** a přiřadí mu všechny jeho stávající obchody.
Uživatel bez obchodů uvidí výzvu k vytvoření prvního deníku.

Původní hodnoty obchodů, jejich ID a oba způsoby ukládání obrázků se nemění.
Cizí klíč používá `Restrict`, takže ani souběžné přidání obchodu při mazání
deníku nemůže způsobit hromadné smazání obchodů. Návrat migrace odstraní jen
nové členění na deníky, obchody a jejich obrázky zachová.

Staré odkazy `/JournalEntries/Details/{id}` a odkazy na obrázky se po ověření
vlastníka přesměrují do správného deníku. Starý obecný přehled vede na seznam
deníků. Staré POST formuláře bez identifikátoru deníku se odmítají.

Při spuštění proti Neon PostgreSQL se pro migraci používá přímý endpoint bez
`-pooler`, pro běžné dotazy aplikace zůstává původní připojení.

## Ověření před nasazením

- Integrační testy přes skutečné MVC formuláře: CRUD, validace, ochrana CSRF,
  kontrola vlastnictví, izolace mezi dvěma deníky téhož uživatele, obrázky,
  vytvoření obchodu s podvrženým vlastníkem/deníkem a kompatibilita starých URL.
- Zkouška PostgreSQL migrace a jejího návratu na izolované kopii tabulky se
  vzorovými obchody dvou vlastníků; celá zkouška vrácena transakcí zpět.
- Skutečná SQL Server migrace izolované databáze se starým schématem: všechny
  3 vzorové obchody přiřazeny do 2 deníků, původní a galerijní obrázek zachovány.
- Přihlášení, vytvoření a přejmenování druhého deníku ověřeny v prohlížeči;
  při šířce 375 px seznam nemá vodorovný přesah.

Produkční nasazení používá stávající GitHub Actions workflow. Po publikaci se
musí úspěšně ověřit `/health/live` a `/health/deep`; druhý endpoint kontroluje
také tabulku a sloupce deníků a transakční vložení deníku, obchodu a obrázku,
které následně vrátí zpět. `/health/live` databázové dotazy neprovádí.
