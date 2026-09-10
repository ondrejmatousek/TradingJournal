using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using TradingJournal.Data;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable
namespace TradingJournal.Data.PostgresMigrations;

[Migration("20260910082313_AddCompanyValuations")]
[DbContext(typeof(ApplicationDbContext))]
public partial class AddCompanyValuations : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "CompanyValuations",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false).Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                Symbol = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                ValuationDate = table.Column<DateOnly>(type: "date", nullable: false),
                CalculatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                CurrentPrice = table.Column<decimal>(type: "numeric(20,8)", nullable: true),
                FairValueLow = table.Column<decimal>(type: "numeric(20,8)", nullable: false),
                FairValueBase = table.Column<decimal>(type: "numeric(20,8)", nullable: false),
                FairValueHigh = table.Column<decimal>(type: "numeric(20,8)", nullable: false),
                UpsidePercent = table.Column<decimal>(type: "numeric(12,4)", nullable: true),
                MarginOfSafetyPercent = table.Column<decimal>(type: "numeric(12,4)", nullable: true),
                ValuationScore = table.Column<decimal>(type: "numeric(8,3)", nullable: true),
                Confidence = table.Column<decimal>(type: "numeric(8,3)", nullable: false),
                Status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                Model = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                MethodsJson = table.Column<string>(type: "text", nullable: false),
                AssumptionsJson = table.Column<string>(type: "text", nullable: false),
                DataQuality = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_CompanyValuations", x => x.Id));
        migrationBuilder.CreateIndex("IX_CompanyValuations_Symbol_ValuationDate", "CompanyValuations", new[] { "Symbol", "ValuationDate" }, unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropTable("CompanyValuations");
}
