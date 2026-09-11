using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace TradingJournal.Data.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260910120000_ValuationV2")]
public sealed class ValuationV2 : Migration
{
    protected override void Up(MigrationBuilder m)
    {
        m.CreateTable("ValuationInputs",t=>new {Id=t.Column<string>(maxLength:64,nullable:false),Json=t.Column<string>(nullable:false)},constraints:t=>t.PrimaryKey("PK_ValuationInputs",x=>x.Id));
        m.CreateTable("InvestmentListings",t=>new {Symbol=t.Column<string>(maxLength:32,nullable:false),Name=t.Column<string>(nullable:false),Sector=t.Column<string>(nullable:false),Enabled=t.Column<bool>(nullable:false)},constraints:t=>t.PrimaryKey("PK_InvestmentListings",x=>x.Symbol));
        m.CreateTable("ValuationRuns",t=>new {
            Id=t.Column<string>(maxLength:64,nullable:false),Symbol=t.Column<string>(maxLength:32,nullable:false),
            InputHash=t.Column<string>(maxLength:64,nullable:false),ModelVersion=t.Column<string>(nullable:false),
            CreatedAt=t.Column<DateTimeOffset>(nullable:false),BaseFairValue=t.Column<decimal>(precision:28,scale:10,nullable:true),
            Status=t.Column<string>(nullable:false),Json=t.Column<string>(nullable:false)
        },constraints:t=> {t.PrimaryKey("PK_ValuationRuns",x=>x.Id);t.ForeignKey("FK_ValuationRuns_ValuationInputs_InputHash",x=>x.InputHash,"ValuationInputs","Id",onDelete:ReferentialAction.Restrict);});
        m.CreateIndex("IX_ValuationRuns_InputHash","ValuationRuns","InputHash");
        m.CreateIndex("IX_ValuationRuns_Symbol_CreatedAt","ValuationRuns",new[]{"Symbol","CreatedAt"});
    }
    protected override void Down(MigrationBuilder m) {m.DropTable("ValuationRuns");m.DropTable("ValuationInputs");m.DropTable("InvestmentListings");}
}

