using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using TradingJournal.Models;

namespace TradingJournal.Services;

internal static class JournalSessionExport
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static async Task<Stream> CreateAsync(
        TradingJournalBook journal,
        DateOnly date,
        IReadOnlyList<JournalEntry> trades,
        CancellationToken cancellationToken)
    {
        var file = new FileStream(Path.Combine(Path.GetTempPath(), Path.GetRandomFileName()),
            FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 4096,
            FileOptions.Asynchronous | FileOptions.DeleteOnClose);

        try
        {
            await using (var zip = await ZipArchive.CreateAsync(file, ZipArchiveMode.Create, leaveOpen: true,
                entryNameEncoding: null, cancellationToken))
            {
                var manifest = new
                {
                    schemaVersion = 1,
                    journal = new { journal.Id, journal.Name },
                    sessionDate = date,
                    calendarTimeZone = TimeZoneInfo.Local.Id,
                    trades = trades.Select(trade => new
                    {
                        tradeId = trade.Id,
                        trade.Asset,
                        trade.Side,
                        trade.EntryTime,
                        trade.ExitTime,
                        trade.CreatedAt,
                        trade.EntryPrice,
                        trade.ExitPrice,
                        trade.RiskPercentage,
                        trade.Outcome,
                        trade.ProfitLoss,
                        trade.PlannedOutcome,
                        trade.SeasonalityAligned,
                        trade.FollowedTradingPlan,
                        trade.Note,
                        trade.FeelingsBeforeEntry,
                        images = GetImages(trade).Select(image => new
                        {
                            image.Path,
                            image.OriginalFileName,
                            image.ContentType
                        })
                    })
                };

                await using (var entry = await zip.CreateEntry("obchody.json").OpenAsync(cancellationToken))
                {
                    await JsonSerializer.SerializeAsync(entry, manifest, JsonOptions, cancellationToken);
                }

                await using (var entry = await zip.CreateEntry("README.txt").OpenAsync(cancellationToken))
                await using (var writer = new StreamWriter(entry))
                {
                    await writer.WriteAsync("""
                        Export obchodní seance z TradingJournal

                        Soubor obchody.json obsahuje provedené obchody vybraného dne.
                        Každý obchod má tradeId a seznam images. Položka path ukazuje na
                        obrázek uvnitř ZIPu, ve složce screenshots/trade-<tradeId>/.
                        Prázdný seznam obrázků znamená, že k obchodu nejsou přiloženy.
                        Hodnota null označuje nezadaný údaj. ProfitLoss je v měně účtu.

                        Pokud vaše AI neumí otevřít ZIP, rozbalte jej a přiložte
                        obchody.json spolu s obrázky. Názvy obrázků obsahují ID obchodu.
                        Příklad zadání: Projdi provedené obchody a jejich screenshoty.
                        U každého uveď tradeId, zhodnoť dodržení plánu a popiš, jaké
                        údaje chybí pro závěr.
                        """.AsMemory(), cancellationToken);
                }

                foreach (var image in trades.SelectMany(GetImages))
                {
                    var entry = zip.CreateEntry(image.Path, CompressionLevel.NoCompression);
                    await using var destination = await entry.OpenAsync(cancellationToken);
                    await destination.WriteAsync(image.Data, cancellationToken);
                }
            }

            file.Position = 0;
            return file;
        }
        catch
        {
            await file.DisposeAsync();
            throw;
        }
    }

    private static IEnumerable<ExportImage> GetImages(JournalEntry trade)
    {
        var directory = $"screenshots/trade-{trade.Id}";
        if (trade.ImageData is { Length: > 0 } legacy)
        {
            yield return new($"{directory}/trade-{trade.Id}-legacy{Extension(trade.ImageContentType)}",
                trade.ImageFileName, trade.ImageContentType, legacy);
        }

        foreach (var image in trade.Images.OrderBy(i => i.Id))
        {
            if (image.ImageData.Length > 0)
            {
                yield return new($"{directory}/trade-{trade.Id}-image-{image.Id}{Extension(image.ImageContentType)}",
                    image.ImageFileName, image.ImageContentType, image.ImageData);
            }
        }
    }

    private static string Extension(string? contentType) => contentType?.ToLowerInvariant() switch
    {
        "image/png" => ".png",
        "image/jpeg" or "image/jpg" => ".jpg",
        "image/webp" => ".webp",
        "image/gif" => ".gif",
        "image/bmp" => ".bmp",
        "image/avif" => ".avif",
        "image/tiff" => ".tiff",
        "image/svg+xml" => ".svg",
        "image/heic" => ".heic",
        "image/heif" => ".heif",
        "image/x-icon" or "image/vnd.microsoft.icon" => ".ico",
        _ => ".bin"
    };

    private sealed record ExportImage(string Path, string? OriginalFileName, string? ContentType, byte[] Data);
}
