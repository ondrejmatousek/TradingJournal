# Lokální denní aktualizace


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

Tím vznikne úloha **TradingJournal daily investment data**, která každý den v 06:00 spustí screening, makro, insiders a uloží výsledek do SQL Serveru. Mimořádný běh:

```powershell
pwsh -File Tools/ScreeningWorker/run-local.ps1 -Force
```

Před spuštěním musí být v uživatelském nebo systémovém prostředí nastavena `SCREENING__SECUSERAGENT`; případný `ALPHAVANTAGE__APIKEY` je volitelný. Skripty nevkládají tajné údaje do repozitáře.

GitHub workflow `.github/workflows/screening-daily.yml` už nemá denní trigger. Zůstal pouze ruční `workflow_dispatch` pro případné mimořádné použití.
