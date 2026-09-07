# Denní aktualizace na GitHub Actions

Web zůstává na Azure App Service Free. Samostatný .NET 10 program stahuje ceny, výkazy a makro na standardním Linux runneru, bez spuštění webu nebo počítače uživatele. Oba procesy používají tabulku `dbo.ScreeningDocuments` ve stejné SQL databázi. JSON dokumenty jsou nyní uvnitř SQL; nejde o GitHub artifacts ani dočasný disk runneru. Osobní analýzy zůstávají oddělené podle uživatele. Chybějící geopolitika a analýzy traderů nemají nový datový zdroj.

## Pořadí prvního nasazení

1. Commit/push změn včetně migrace `AddScreeningDocuments` na nasazovanou větev. Stávající workflow `tradingJournal.yml` nasadí web. Web při startu aplikuje migraci; jeho SQL identita musí mít oprávnění ke změně schématu. Migrace pouze přidává tabulku, nemění existující záznamy. Readiness `/health/deep` nyní kontroluje i novou tabulku. Neaktivujte plán při neúspěšné migraci.
2. Pokud jsou ve staré cloudové cache ručně zadané analýzy, před nasazením ji zálohujte. Nový web při načtení převezme existující lokální JSON jen tehdy, když v SQL ještě není odpovídající klíč. Worker staré soubory na webu nevidí. Nasazení, které soubory odstraní, neumožní jejich následný import; nevkládejte je do GitHubu.
3. Připravte omezeného SQL uživatele pro worker ve stejné databázi (viz níže) a Azure federovanou identitu pro dočasné pravidlo firewallu.
4. V repozitáři Settings → Secrets and variables → Actions vyplňte níže uvedené secrets a variables. Hesla nevkládejte do chatu ani do souborů projektu.
5. Actions → Daily stock screening → Run workflow na výchozí větvi. Úloha ověří přístup a existenci tabulky, stáhne data a zveřejní souhrn počtů. Ověřte data ve screeneru. Neúplná data mohou zůstat nevyhodnocená; hotový běh není zárukou všech polí. Při selhání makra nebo neúplnosti všech tickerů je běh označen jako neúspěšný. Volba force dovolí znovu zkusit dokončený běh i čerstvou cache.
6. Teprve po úspěšném ověření nastavte repository variable `SCREENING_SCHEDULE_ENABLED=true`. Plán je 06:17 `Europe/Prague`, automaticky respektuje letní čas. Bez této proměnné se plánované běhy přeskakují. GitHub spouští plán pouze z výchozí větve a negarantuje přesný čas; může dojít ke zpoždění nebo vynechání běhu.

## GitHub secrets

| Název | Hodnota |
| --- | --- |
| `SCREENING_SQL_CONNECTION_STRING` | Připojení do stejné Azure SQL jako web, s omezeným účtem workeru; `Encrypt=True;TrustServerCertificate=False`. |
| `SCREENING_SEC_USER_AGENT` | Identifikace provozovatele s kontaktním e-mailem dle pravidel SEC. |
| `SCREENING_AZURE_CLIENT_ID` | ID aplikace / identity pro Azure OIDC přihlášení. |
| `SCREENING_AZURE_TENANT_ID` | ID tenant Azure. |
| `SCREENING_AZURE_SUBSCRIPTION_ID` | ID předplatného, kde je SQL server. |
| `SCREENING_ALPHA_VANTAGE_API_KEY` | Volitelné; záložní poskytovatel cen. |

Repository variables: `AZURE_SQL_RESOURCE_GROUP` (resource group SQL serveru), `AZURE_SQL_SERVER` (krátký název SQL serveru bez `.database.windows.net`) a `SCREENING_SCHEDULE_ENABLED` (nejdříve prázdné/false).

## SQL oprávnění a síť

Účet workeru nepotřebuje přístup k obchodnímu deníku ani provádění migrací. Po bezpečném vytvoření contained SQL uživatele `screening_worker` správcem databáze:

```sql
GRANT SELECT, INSERT, UPDATE ON OBJECT::dbo.ScreeningDocuments TO [screening_worker];
```

Úloha si drží session lock `EdgeDesk.ScreeningDaily` přes `sp_getapplock`, aby se dvě instance workeru nepřekrývaly; standardní členství v databázové roli public je dostačující. Spojení držící lock nepoužívá pooling a po skončení se skutečně uzavře.

Azure federovaná identita má issuer `https://token.actions.githubusercontent.com`, audience `api://AzureADTokenExchange` a subject `repo:ondrejmatousek/TradingJournal:ref:refs/heads/<výchozí-větev>`. Nahraďte větev podle repozitáře. U této identity stačí vlastní Azure role na konkrétním SQL serveru s akcemi `Microsoft.Sql/servers/read` a `Microsoft.Sql/servers/firewallRules/read`, `write`, `delete`; není potřeba Contributor celého předplatného. Přihlašování do SQL používá samostatný omezený SQL účet; OIDC slouží pouze správě firewallu.

SQL server musí mít povolené veřejné síťové rozhraní. Workflow povolí jen jednu ověřenou IPv4 adresu runneru, po běhu pravidlo odstraní v kroku `always()`. Neotevírejte celý internet. Jestli Azure používá výhradně private endpoint, standardní GitHub runner se nepřipojí a tento postup vyžaduje jiné síťové propojení. Po tvrdém ukončení runneru může cleanup selhat; zkontrolujte a odstraňte osiřelá pravidla s prefixem `gh-screening-`.

## Provoz a limity

- Celý job má limit 45 minut, vlastní aktualizace 35 minut; nepřidávají se automatické retry celé úlohy. Jednotlivé zdroje mají vlastní timeouty a limity. Při ukončení zůstávají již uložená data zachovaná.
- `concurrency` zabraňuje souběhu workflow. SQL lock chrání i před nezávislým ručním workerem. Záznam dokončeného dne brání duplicitnímu dennímu běhu.
- Web při detekci Azure App Service (`WEBSITE_SITE_NAME`) vypne svůj interní plánovač screeneru. Mimo Azure lze nastavit `Screening__Schedule__External=true`. Ruční tlačítko zůstává dostupné. Externí plán se zapíná pouze v GitHubu.
- GitHub Free má 2 000 minut standardního Linux runneru měsíčně pro soukromé repozitáře, sdílených s dalšími úlohami. Délku běhu je potřeba měřit; cena není garantována. Nenahráváme cenová data do artifactů ani Actions cache. Nastavte v GitHub billing rozpočet s blokováním placené spotřeby, pokud nechcete čerpat nad bezplatnou kvótu. Nastavení rozpočtu není součástí YAML a musí se ověřit v účtu. SQL/API se účtují samostatně.
- U veřejného repozitáře GitHub automaticky vypíná plán po 60 dnech bez aktivity. Sledujte poslední běh ve screeneru a GitHub Actions.

## Lokální ověření

```powershell
dotnet test TradingJournal.Tests/TradingJournal.Tests.csproj
dotnet publish Tools/ScreeningWorker/ScreeningWorker.csproj -c Release -o tmp/screening-worker-publish
dotnet tmp/screening-worker-publish/ScreeningWorker.dll --help
# Následující příkazy vyžadují připojení a SEC identifikaci bezpečně nastavené v prostředí:
dotnet tmp/screening-worker-publish/ScreeningWorker.dll --check
dotnet tmp/screening-worker-publish/ScreeningWorker.dll --run
```

`--check` nestahuje ceny ani neprovádí migrace. Čte tabulku a kontroluje přibalený seznam SEC. Návratové kódy: 0 dokončeno/přeskočeno, 1 konfigurace/SQL/neočekávaná chyba, 2 neúspěšné makro nebo všechny tituly neúplné, 3 přerušení/timeout.

Zdroje: [GitHub plánování](https://docs.github.com/en/actions/reference/workflows-and-actions/events-that-trigger-workflows#schedule), [GitHub billing](https://docs.github.com/en/billing/concepts/product-billing/github-actions), [Azure OIDC](https://github.com/Azure/login#login-with-openid-connect-oidc-recommended), [Azure SQL firewall](https://learn.microsoft.com/en-us/azure/azure-sql/database/firewall-configure).
