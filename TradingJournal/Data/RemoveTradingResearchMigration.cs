using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

namespace TradingJournal.Data;

public static class RemoveTradingResearchMigration
{
    public static void Up(MigrationBuilder m)
    {
        m.DropTable("CotImportRuns");
        m.DropTable("CotReports");
        // Stock screening still needs the shared price history. Preserve it independently.
        m.RenameTable("SeasonalityPrices", newName: "MarketPrices");
        m.RenameIndex(name: "IX_SeasonalityPrices_AssetKey_Date", table: "MarketPrices", newName: "IX_MarketPrices_AssetKey_Date");
        m.DropPrimaryKey("PK_SeasonalityPrices", "MarketPrices");
        m.AddPrimaryKey("PK_MarketPrices", "MarketPrices", "Id");
    }

    public static void Down(MigrationBuilder m)
    {
        m.DropPrimaryKey("PK_MarketPrices", "MarketPrices");
        m.AddPrimaryKey("PK_SeasonalityPrices", "MarketPrices", "Id");
        m.RenameIndex(name: "IX_MarketPrices_AssetKey_Date", table: "MarketPrices", newName: "IX_SeasonalityPrices_AssetKey_Date");
        m.RenameTable("MarketPrices", newName: "SeasonalityPrices");
        // Recreate the historical schema, without fabricating deleted downloadable COT data.
        var legacy = new Migrations.AddCotData { ActiveProvider = m.ActiveProvider ?? string.Empty };
        foreach (var operation in legacy.UpOperations)
        {
            if (operation is CreateTableOperation table)
                foreach (var column in table.Columns)
                {
                    column.ColumnType = null;
                    if (column.Name == "Id")
                        column["Npgsql:ValueGenerationStrategy"] = NpgsqlValueGenerationStrategy.IdentityByDefaultColumn;
                }
            m.Operations.Add(operation);
        }
    }
}
