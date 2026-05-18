# Google sign-in setup

The app supports Google sign-in through ASP.NET Core Identity.

## Local development

Store Google OAuth credentials in user secrets:

```powershell
dotnet user-secrets set "Authentication:Google:ClientId" "<your-client-id>"
dotnet user-secrets set "Authentication:Google:ClientSecret" "<your-client-secret>"
```

Run the commands from the `TradingJournal` project folder.

## Google OAuth redirect URI

Add this redirect URI in Google Cloud Console for local development:

```text
https://localhost:7063/signin-google
```

If you run the HTTP profile only, use the HTTPS profile for Google sign-in because OAuth providers commonly require HTTPS redirect URLs.

## Runtime behavior

Google sign-in is enabled only when both configuration values are present. Without them, the app starts normally and hides the external login option.
