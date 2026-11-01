# Lokální denní aktualizace

Worker je jednorázový .NET proces. Webová aplikace i worker používají stejnou lokální SQL Server databázi (SSMS je pouze nástroj pro její správu).

## První nastavení

```powershell
$env:SCREENING__SECUSERAGENT = 'TradingJournal/1.0 contact: vas@email.cz'
dotnet publish Tools/ScreeningWorker/ScreeningWorker.csproj -c Release -o Tools/ScreeningWorker/publish
dotnet Tools/ScreeningWorker/publish/ScreeningWorker.dll --check
```

Připojení se načítá z `ConnectionStrings:DefaultConnection`. V Development režimu je výchozí:
`Server=localhost\SQLEXPRESS;Database=TradingJournal;Trusted_Connection=True;TrustServerCertificate=True;Encrypt=False`.

## Automatické spuštění ve Windows

```powershell
pwsh -File Tools/ScreeningWorker/Install-LocalTask.ps1
```

Tím vznikne úloha **TradingJournal daily investment data**, která každý den v 06:00 spustí screening, makro a insiders a uloží výsledek do SQL Serveru. Mimořádný běh:

```powershell
pwsh -File Tools/ScreeningWorker/run-local.ps1 -Force
```

Před spuštěním musí být v uživatelském nebo systémovém prostředí nastavena `SCREENING__SECUSERAGENT`; případný `ALPHAVANTAGE__APIKEY` je volitelný. Skripty nevkládají tajné údaje do repozitáře.

GitHub workflow `.github/workflows/screening-daily.yml` už nemá denní trigger. Zůstal pouze ruční `workflow_dispatch` pro případné mimořádné použití.

## Pouze akciový screener a následný audit

Pro obnovu cen, výkazů a ocenění bez ostatních denních importů:

```powershell
dotnet build Tools/ScreeningWorker/ScreeningWorker.csproj -o tmp/coverage-refresh
dotnet tmp/coverage-refresh/ScreeningWorker.dll --run --force --screening-only --local
dotnet tmp/coverage-refresh/ScreeningWorker.dll --audit --local
```

Obnova používá stejný databázový zámek jako celý denní worker. Audit ukládá `coverage-audit.json` v pracovním adresáři. Dokončení obnovy znamená, že byly zpracovány všechny akcie; dostupnost a důvěryhodnost výsledků ukazuje až audit.

Pilotní aktualizace:
ScreeningWorker --run --pilot-only --force
Aktualizuje makro kontext a pouze MSFT, META, AAPL, CTAS a RSG; ověří použitelná ocenění 5/5. Pravidelný běh řeší price-lab-daily.yml nad samostatným PRICE_LAB_CONNECTION_STRING. Neprovádí migrace ani import insiders.
