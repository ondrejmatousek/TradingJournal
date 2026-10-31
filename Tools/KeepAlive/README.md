# Bezplatné udržování Trading Journalu v běhu

Externí monitor: **UptimeRobot Free**, kontrola HTTP každých **5 minut**.

Monitorovaná adresa ověřená z posledního úspěšného Azure nasazení:

```text
https://tradingjournal-dmdvfwfrgzdfdga3.westeurope-01.azurewebsites.net/health/live
```

Nastavení při ručním vytvoření monitoru:

| Nastavení | Hodnota |
| --- | --- |
| Typ | HTTP(s) |
| Název | Trading Journal – keep alive |
| Interval | 5 minut |
| Očekávaný stav | HTTP 200 |
| Přihlášení | žádné |

Endpoint již existuje na veřejném nasazení a vrací `status: Healthy`.
Metoda `HealthController.Live` neotevírá databázové připojení ani nespouští
dotazy nebo obnovu dat. Při studeném startu nadále probíhá běžná inicializace
aplikace, včetně stávající kontroly migrací.

V pracovní kopii je navíc doplněna explicitní podpora HEAD a
`Cache-Control: no-store, no-cache`; tyto úpravy se projeví při příštím nasazení.
Už nasazený GET endpoint je pro monitor použitelný bez nového deploymentu.

## Aktivace

Dne 31. 10. 2026 byla na uživatelem zadanou e-mailovou adresu odeslána žádost
o aktivaci přes oficiální UptimeRobot agent-monitor API. Služba odpověděla
`status: ok`. Tato odpověď nepotvrzuje aktivní monitor ani doručení e-mailu.

Vlastník musí otevřít aktivační e-mail, kliknout na odkaz a na následné stránce
potvrdit tlačítkem **Activate**. Teprve tím vznikne účet/monitor a začnou
pravidelné kontroly. Po aktivaci zkontrolujte URL, interval a první úspěšnou
kontrolu v dashboardu UptimeRobot. Pokud monitor již existuje, nevytvářejte
další duplicitní monitor stejné adresy.

## Omezení

Ping může omezit uspávání po neaktivitě, ale není zárukou dostupnosti Free
tarifu a neodstraňuje kvóty Azure ani restart platformy. Databázi neudržuje
aktivní. GitHub Actions pro tento ping nepoužíváme: repozitář je soukromý
a pětiminutový plán znamená 8 640 běhů za 30 dní; zaokrouhlování spotřeby
každého jobu na minutu by překročilo běžné bezplatné měsíční limity.

Zdroje:

- [UptimeRobot Free – pětiminutový interval](https://help.uptimerobot.com/en/articles/11604710-who-should-use-uptimerobot-s-free-plan)
- [Oficiální aktivační postup](https://uptimerobot.com/quick-monitor-setup/)
- [Azure App Service – uspávání a Always On](https://learn.microsoft.com/azure/app-service/configure-common?tabs=portal)
- [Kvóty Azure App Service](https://learn.microsoft.com/en-us/azure/app-service/web-sites-monitor)
- [Zaokrouhlování minut GitHub Actions](https://docs.github.com/en/actions/how-tos/monitor-workflows/view-job-execution-time)
