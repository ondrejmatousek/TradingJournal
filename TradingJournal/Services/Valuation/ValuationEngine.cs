using Microsoft.Extensions.Options;

namespace TradingJournal.Services.Valuation;

public interface IValuationEngine
{
    ValuationReport Calculate(string symbol, string sector, FinancialDataset? data, decimal? price, DateOnly? priceDate,
        DateOnly asOf, Assumption? riskFree = null, ValuationForecast? forecast = null);
}
public interface IFinancialNormalizationService { decimal Median(IEnumerable<decimal> values); }
public sealed class FinancialNormalizationService : IFinancialNormalizationService
{
    public decimal Median(IEnumerable<decimal> values)
    {
        var a = values.Order().ToArray();
        return a.Length == 0 ? 0 : a.Length % 2 == 0 ? (a[a.Length/2-1]+a[a.Length/2])/2 : a[a.Length/2];
    }
}
public interface IWaccCalculator { decimal Calculate(decimal rf, decimal beta, decimal erp, decimal kd, decimal tax, decimal equity, decimal debt); }
public sealed class WaccCalculator : IWaccCalculator
{
    public decimal Calculate(decimal rf, decimal beta, decimal erp, decimal kd, decimal tax, decimal equity, decimal debt)
    {
        if (equity <= 0 || debt < 0 || tax is < 0 or >= 1 || rf < 0 || beta <= 0 || erp <= 0 || kd < 0) throw new ArgumentOutOfRangeException(nameof(equity));
        return equity/(equity+debt)*(rf+beta*erp)+debt/(equity+debt)*kd*(1-tax);
    }
}
public static class ValuationMath
{
    public static decimal Nopat(decimal ebit, decimal tax) => ebit*(1-tax);
    public static decimal Fcff(decimal nopat, decimal da, decimal capex, decimal changeNwc) => nopat+da-capex-changeNwc;
    public static decimal Pv(decimal cash, decimal rate, int year) => cash/(decimal)Math.Pow((double)(1+rate), year);
    public static decimal Terminal(decimal nopat, decimal growth, decimal roic, decimal wacc)
    {
        if (wacc <= growth || roic <= growth || growth < 0) throw new ArgumentOutOfRangeException(nameof(growth));
        return nopat*(1-growth/roic)/(wacc-growth);
    }
    public static decimal? Get(FinancialPeriod p, string key) => p.Facts.GetValueOrDefault(key)?.Value;
    public static decimal? Nwc(FinancialPeriod p) => Get(p,"Receivables") is {} ar && Get(p,"Payables") is {} ap
        ? ar+(Get(p,"Inventory") ?? 0)-ap : null;
    public static decimal? Debt(FinancialPeriod p)
    {
        // A provider's verified total debt already includes short-term borrowings.
        if (Get(p,"TotalDebt") is {} total) return total;
        // Aggregate term debt and current/noncurrent decomposition are alternatives, not additive.
        var term = Get(p,"DebtAggregate");
        if (term == null && Get(p,"LongDebt") is {} ld && Get(p,"CurrentDebt") is {} cd) term = ld+cd;
        if (term == null) return null; // absent is not zero
        // ShortTermBorrowings may already include commercial paper: do not add both.
        return term + (Get(p,"ShortDebt") ?? Get(p,"CommercialPaper") ?? 0);
    }
}
public sealed class ValuationEngine(IOptions<ValuationOptions> settings, IFinancialNormalizationService normalization,
    IWaccCalculator waccCalculator, IEnumerable<IValuationStrategy>? strategies = null) : IValuationEngine
{
    public ValuationReport Calculate(string symbol, string sector, FinancialDataset? data, decimal? price, DateOnly? priceDate,
        DateOnly asOf, Assumption? riskFree = null, ValuationForecast? forecast = null)
    {
        var o = settings.Value;
        var r = new ValuationReport { Symbol=symbol, AsOf=asOf, Price=price, PriceDate=priceDate, Policy=o,
            InputHash=ValuationJson.Hash(data == null ? null : data with { AsOf=default }), FinancialPeriodEnd=data?.Latest?.End, IsTtm=data?.Latest?.IsTtm ?? false };
        if (forecast != null) r.ModelVersion = "ValuationV2.1";
        r.Id=ValuationJson.Hash(new { r.ModelVersion, r.Symbol, r.InputHash, sector, asOf, price, priceDate, riskFree, o, forecast });
        ValuationReport Fail(string message, string status="LimitedData") { r.Status=status; r.Warnings.Add(message); return r; }
        if(o.MaximumForecastRoic<=0 || o.TerminalRoic<=0 || o.GrowthFloor>o.GrowthCeiling || o.TaxFloor<0 || o.TaxCeiling>=1 || o.TaxFloor>o.TaxCeiling
            || o.TerminalGrowth<0 || new[]{o.LowMos,o.MediumMos,o.HighMos,o.VeryHighMos}.Any(x=>x<0||x>=1))
            return Fail("Invalid valuation policy configuration.","InvalidData");
        if (forecast != null && (forecast.InitialGrowth is < -.10m or > .50m || forecast.TerminalMargin is < .01m or > .60m || forecast.Beta is < .3m or > 3m || forecast.Date > asOf)) return Fail("Invalid forward assumptions.", "InvalidData");
        if (!o.Enabled) return Fail("Valuation V2 is disabled; legacy values are not substituted.");
        var strategy = (strategies ?? new IValuationStrategy[] { new FinancialInstitutionStrategy(), new ReitStrategy(), new StandardNonFinancialStrategy() })
            .FirstOrDefault(x=>x.Matches(sector ?? ""));
        if(strategy == null) { r.Strategy="Unclassified"; return Fail("Verified company sector/classification required before selecting a valuation model.","UnsupportedCompanyType"); }
        r.Strategy=strategy.Name;
        if(strategy.UnsupportedReason is {} reason) return Fail(reason,"UnsupportedCompanyType");
        if (data?.Latest is not {} p) return Fail("SEC financial dataset missing. Refresh filings before publishing V2.");
        r.Warnings.AddRange(data.Warnings);
        if (data.Currency != "USD") return Fail("Currency unsupported; no currency conversion assumed.","UnsupportedCompanyType");
        if (p.End > asOf || asOf.DayNumber-p.End.DayNumber > 460 || p.Facts.Values.Any(f=>f.Filed>asOf || f.End>asOf || f.Start>f.End))
            return Fail("Invalid or stale financial period.","InvalidData");
        foreach(var f in p.Facts)
            if (f.Value.Unit != (f.Key.EndsWith("Shares") ? "shares" : "USD") || Math.Abs(f.Value.Value)>1_000_000_000_000_000m)
                return Fail($"Invalid units/range: {f.Key}.","InvalidData");
        var required=new[]{"Revenue","Ebit","Da","CapEx","Cash"};
        foreach(var key in required) if (ValuationMath.Get(p,key)==null) return Fail($"Missing {key} for the selected {(p.IsTtm?"TTM":"FY")} period.");
        var revenue=ValuationMath.Get(p,"Revenue")!.Value; var ebit=ValuationMath.Get(p,"Ebit")!.Value;
        if(revenue<=0 || ebit<=0 || ValuationMath.Get(p,"Da")<0 || ValuationMath.Get(p,"CapEx")<0 || ValuationMath.Get(p,"Cash")<0)
            return Fail("Nonpositive operating earnings or invalid expense signs; turnaround forecast requires analyst input.","InvalidData");
        var debt=ValuationMath.Debt(p);
        if(debt==null) return Fail("Debt completeness cannot be established; absent debt is not zero.");
        if(new[]{"TotalDebt","DebtAggregate","LongDebt","CurrentDebt","ShortDebt","CommercialPaper","ShortInvestments","LongInvestments"}.Any(k=>ValuationMath.Get(p,k)<0))
            return Fail("Negative debt or investment component.","InvalidData");
        if(data.History.Any(h=>h.End>asOf || h.Facts.Any(f=>f.Value.Filed>asOf || f.Value.End>asOf
            || f.Value.Unit!=(f.Key.EndsWith("Shares")?"shares":"USD"))))
            return Fail("Historical inputs contain future filings or inconsistent units.","InvalidData");
        var history=data.History.Where(x=>x.End<=p.End && ValuationMath.Get(x,"Revenue")>0 && ValuationMath.Get(x,"Ebit")!=null).OrderBy(x=>x.End).ToArray();
        if(history.Length<3) return Fail("At least three comparable annual revenue/EBIT periods are required.");
        var shares=data.ActualShares;
        if(shares!=null && shares.Unit!="shares") return Fail("Invalid actual share units.","InvalidData");
        var diluted=ValuationMath.Get(p,"DilutedShares"); var basic=ValuationMath.Get(p,"BasicShares");
        decimal shareCount;
        if(shares != null && shares.End<=asOf && shares.Filed<=asOf && asOf.DayNumber-shares.End.DayNumber<=180 && shares.Value>0)
        {
            shareCount=shares.Value;
            if(diluted>0 && basic>0) shareCount*=Math.Max(1,diluted.Value/basic.Value);
            r.ShareType="Actual outstanding × max(1, period diluted/basic); estimated dilution proxy"; r.SharesDate=shares.End;
        }
        else if(diluted>0) {shareCount=diluted.Value; r.ShareType="Period weighted diluted shares (fallback)"; r.SharesDate=p.End;}
        else return Fail("Neither dated actual shares nor period diluted shares available.");
        if(shareCount<=0) return Fail("Invalid shares.","InvalidData");
        if(shares!=null && basic>0 && (shares.Value/basic.Value is > 1.5m or < .65m))
            return Fail("Possible split/issuance or period mismatch in share basis; requires reconciliation.","InvalidData");
        r.Warnings.Add("Dilution is a proxy, not an option valuation; SBC remains an operating expense and is not added back to FCFF. Buybacks do not create extra value in the forecast.");
        if(price is not > 0 || priceDate==null || priceDate>asOf || asOf.DayNumber-priceDate.Value.DayNumber>7) return Fail("Fresh price is required for market equity weights in WACC.");
        decimal Median(IEnumerable<decimal> v)=>normalization.Median(v);
        void A(string name,decimal value,string unit,string source,bool estimated,string reason,DateOnly? date=null)
            =>r.Assumptions.Add(new(name,value,unit,source,date??p.End,estimated,reason));
        var taxHistory=history.Where(x=>ValuationMath.Get(x,"Pretax")>0 && ValuationMath.Get(x,"Tax")>=0)
            .Select(x=>ValuationMath.Get(x,"Tax")!.Value/ValuationMath.Get(x,"Pretax")!.Value).ToArray();
        var rawTax=taxHistory.Length>0?Median(taxHistory):o.FallbackTax;
        var tax=Math.Clamp(rawTax,o.TaxFloor,o.TaxCeiling);
        r.Normalizations.Add(new("Tax",rawTax,tax,"Median effective tax; bounded to avoid one-off tax distortions. Not a marginal statutory tax forecast."));
        A("Tax",tax,"ratio",taxHistory.Length>0?"SEC Tax/Pretax history":"ValuationOptions.FallbackTax",taxHistory.Length==0,"Normalized tax applies to NOPAT and debt tax shield.");
        decimal Cagr(int years)
        {
            var last=history[^1];var first=history.OrderBy(x=>Math.Abs((last.End.DayNumber-x.End.DayNumber)/365.25-years)).First();
            var n=(last.End.DayNumber-first.End.DayNumber)/365.25;
            return n>=1?(decimal)Math.Pow((double)(ValuationMath.Get(last,"Revenue")!.Value/ValuationMath.Get(first,"Revenue")!.Value),1/n)-1:0;
        }
        var yoy=history.Zip(history.Skip(1)).Select(x=>ValuationMath.Get(x.Second,"Revenue")!.Value/ValuationMath.Get(x.First,"Revenue")!.Value-1).ToArray();
        var recent=yoy[^1];
        var growth=Median(new[]{Cagr(3),history.Length>=6?Cagr(5):Cagr(3),recent});
        var spread=Median(yoy.Select(x=>Math.Abs(x-Median(yoy))));
        var initialGrowth=forecast?.InitialGrowth ?? Math.Clamp(growth,o.GrowthFloor,o.GrowthCeiling);
        A("RevenueCagr3Y",Cagr(3),"ratio","SEC annual history",false,"Calendar-day CAGR; shorter available history used when full window is unavailable.");
        if(history.Length>=6) A("RevenueCagr5Y",Cagr(5),"ratio","SEC annual history",false,"Calendar-day CAGR; shorter available history used when full window is unavailable.");
        if(history.Length>=11) A("RevenueCagr10Y",Cagr(10),"ratio","SEC annual history",false,"Calendar-day CAGR; shorter available history used when full window is unavailable.");
        r.Normalizations.Add(new("Growth",growth,initialGrowth,forecast?.InitialGrowth != null ? "Sourced user first-year growth, fading to terminal growth; not consensus." : "Median 3Y/5Y/latest FY growth, explicit bounds. Forecast extrapolation, not consensus."));
        var margins=history.TakeLast(5).Select(x=>ValuationMath.Get(x,"Ebit")!.Value/ValuationMath.Get(x,"Revenue")!.Value).ToArray();
        var margin=forecast?.TerminalMargin ?? Median(margins); var startMargin=ebit/revenue;
        r.Normalizations.Add(new("EbitMargin",startMargin,margin,forecast?.TerminalMargin != null ? "Forecast fades from latest margin to sourced user target." : "Forecast fades from latest margin to five-year median."));
        var daRatio=Median(history.Where(x=>ValuationMath.Get(x,"Da")!=null).TakeLast(5).Select(x=>ValuationMath.Get(x,"Da")!.Value/ValuationMath.Get(x,"Revenue")!.Value));
        var capexRatio=Median(history.Where(x=>ValuationMath.Get(x,"CapEx")!=null).TakeLast(5).Select(x=>ValuationMath.Get(x,"CapEx")!.Value/ValuationMath.Get(x,"Revenue")!.Value));
        var nwcPeriods=history.Where(x=>ValuationMath.Nwc(x)!=null).TakeLast(5).ToArray();
        if(nwcPeriods.Length<2 || ValuationMath.Nwc(p)==null) return Fail("Insufficient operating receivables/payables history for estimated ΔNWC.");
        var nwcRatio=Median(nwcPeriods.Select(x=>ValuationMath.Nwc(x)!.Value/ValuationMath.Get(x,"Revenue")!.Value));
        r.Warnings.Add("Operating NWC is estimated from receivables + inventory − payables. Other current items are excluded without verified operating classification.");
        A("OperatingNwcRevenue",nwcRatio,"ratio","SEC receivables/inventory/payables",true,"Partial operating NWC; no cash or financing items.");
        A("InitialRevenueGrowth",initialGrowth,"ratio",forecast?.InitialGrowth != null ? forecast.Source : "Historical model",true,"Ten-year projection is a scenario, not a prediction.",forecast?.InitialGrowth != null ? forecast.Date : null);
        A("TerminalMargin",margin,"ratio",forecast?.TerminalMargin != null ? forecast.Source : "SEC five-year median",true,"Year-ten operating margin target.",forecast?.TerminalMargin != null ? forecast.Date : null);
        A("DaRevenue",daRatio,"ratio","SEC five-year median",true,"Explicit period asset intensity.");
        A("CapExRevenue",capexRatio,"ratio","SEC five-year median",true,"Reinvestment checked against implied ROIC.");
        A("TerminalRoic",o.TerminalRoic,"ratio","ValuationOptions",true,"Stable reinvestment=g/ROIC; growth is not free.");
        A("MaximumForecastRoic",o.MaximumForecastRoic,"ratio","ValuationOptions",true,"Explicit-period reinvestment floor NOPAT × positive growth / maximum ROIC; estimated guardrail, not measured ROIC.");
        if(Math.Abs(startMargin-margin)>.10m)r.Warnings.Add("Latest EBIT margin differs by over 10 percentage points from historical median; exceptional items/cycle require review.");
        if(ValuationMath.Get(p,"Ocf") is {} ocf && ValuationMath.Get(p,"NetIncome") is > 0 && Math.Abs(ocf/ValuationMath.Get(p,"NetIncome")!.Value-1)>.50m)
            r.Warnings.Add("Operating cash flow and earnings differ materially; no unverified one-off adjustment was made.");
        var rf=riskFree is {Value:>=0 and <.20m} && riskFree.Date<=asOf && asOf.DayNumber-riskFree.Date.DayNumber<=10 ? riskFree : null;
        var riskFreeValue=rf?.Value??o.FallbackRiskFree;
        A("RiskFree",riskFreeValue,"ratio",rf?.Source??"ValuationOptions.FallbackRiskFree",rf==null,"USD nominal risk-free rate.",rf?.Date??asOf);
        var beta=forecast?.Beta ?? o.FallbackBeta;
        if (forecast != null)
        {
            if (forecast.InitialGrowth != null) A("ForwardGrowth", initialGrowth, "ratio", forecast.Source, true, "User scenario: first-year revenue growth fades to terminal growth over ten years.", forecast.Date);
            if (forecast.TerminalMargin != null) A("ForwardMargin", margin, "ratio", forecast.Source, true, "User scenario: EBIT margin reached in year ten.", forecast.Date);
            if (forecast.Beta != null) A("ForwardBeta", beta, "ratio", forecast.Source, true, "User-supplied company risk assumption.", forecast.Date);
            r.Warnings.Add("Custom forecast is an analyst scenario, not a verified consensus or a prediction.");
        }
        A("Beta",beta,"ratio",forecast?.Beta != null ? forecast.Source : "ValuationOptions.FallbackBeta",true,forecast?.Beta != null ? "Sourced user company beta; not independently verified." : "Estimated beta; a regression/bottom-up company beta is not available.",forecast?.Beta != null ? forecast.Date : null);
        A("ERP",o.Erp,"ratio","ValuationOptions.Erp",true,"Versioned equity risk premium policy.",asOf);
        var interest=ValuationMath.Get(p,"Interest");
        var kd=interest>0 && debt>0?Math.Clamp(interest.Value/debt.Value,.005m,.25m):riskFreeValue+o.CreditSpread;
        A("CostOfDebt",kd,"ratio",interest>0?"SEC interest/debt":"Risk-free + configured spread",true,"Accounting approximation; not current bond yield.");
        A("Debt",debt.Value,"USD","SEC balance sheet",false,"Term aggregate OR current+noncurrent; short borrowing OR commercial paper, never both.");
        if(ValuationMath.Get(p,"TotalDebt")==null && ValuationMath.Get(p,"ShortDebt")==null && ValuationMath.Get(p,"CommercialPaper")==null)
            r.Warnings.Add("Short-term borrowing disclosure missing; term debt used, completeness limited.");
        var cash=ValuationMath.Get(p,"Cash")!.Value; var excess=Math.Max(0,cash-revenue*o.OperatingCashRevenue);
        var investments=(ValuationMath.Get(p,"ShortInvestments")??0)+(ValuationMath.Get(p,"LongInvestments")??0);
        // Only explicitly labelled marketable securities qualify automatically, not broad strategic investments.
        investments=p.Facts.Where(x=>x.Key is "ShortInvestments" or "LongInvestments" && x.Value.Tag.StartsWith("MarketableSecurities")) .Sum(x=>x.Value.Value);
        A("OperatingCashReserve",revenue*o.OperatingCashRevenue,"USD","ValuationOptions.OperatingCashRevenue",true,"Cash reserve retained in operations; only excess enters bridge.");
        A("NonOperatingInvestments",investments,"USD","SEC marketable securities",true,"Assumed liquid non-operating securities, no cash-equivalent duplication; strategic investments excluded.");
        A("ShareCount",shareCount,"shares",shares?.Source??p.Facts["DilutedShares"].Source,true,r.ShareType,r.SharesDate);
        A("EquityWeight",price.Value*shareCount/(price.Value*shareCount+debt.Value),"ratio","Market price × share basis",true,"Market capitalization proxy; debt book value approximates market value.",priceDate);
        var wacc=waccCalculator.Calculate(riskFreeValue,beta,o.Erp,kd,tax,price.Value*shareCount,debt.Value);
        if(wacc<=o.TerminalGrowth+o.ScenarioTerminalShift+o.ScenarioWaccShift || o.TerminalRoic<=o.TerminalGrowth+o.ScenarioTerminalShift)
            return Fail("WACC/terminal growth/ROIC configuration has no valid stable-state spread.","InvalidData");
        A("WACC",wacc,"ratio","CAPM and market equity weights",true,"No hard 9% floor; estimated beta/debt cost.",asOf);
        foreach(var hp in history)
        {
            var prev=history.LastOrDefault(x=>x.End<hp.End); var rev=ValuationMath.Get(hp,"Revenue")!.Value;
            var change=prev!=null?ValuationMath.Nwc(hp)-ValuationMath.Nwc(prev):null;
            var he=ValuationMath.Get(hp,"Ebit"); var da=ValuationMath.Get(hp,"Da"); var cap=ValuationMath.Get(hp,"CapEx");
            r.History.Add(new(hp.End,rev,prev==null?null:rev/ValuationMath.Get(prev,"Revenue")-1,he,he/rev,da,cap,change,
                he*(1-tax)+da-cap-change,ValuationMath.Get(hp,"DilutedShares"),cap/rev,da/rev,ValuationMath.Nwc(hp)/rev));
        }
        ValuationScenario Scenario(string name,decimal shift,decimal rate,decimal tg)
        {
            var years=new List<ForecastYear>();var rev=revenue;var priorNwc=ValuationMath.Nwc(p)!.Value;
            var targetMargin=Math.Clamp(margin+shift*o.ScenarioMarginShift,.01m,.60m);
            var firstGrowth=Math.Clamp(initialGrowth+shift*Math.Max(o.ScenarioGrowthShift,spread),forecast?.InitialGrowth != null ? -.10m : o.GrowthFloor,forecast?.InitialGrowth != null ? .50m : o.GrowthCeiling);
            for(var y=1;y<=10;y++)
            {
                var progress=(y-1)/9m;var g=firstGrowth+(tg-firstGrowth)*progress;
                rev*=1+g;var em=startMargin+(targetMargin-startMargin)*y/10m;
                var op=rev*em;var np=ValuationMath.Nopat(op,tax);var da=rev*daRatio;
                var nwc=rev*nwcRatio;var change=nwc-priorNwc;priorNwc=nwc;
                var cap=rev*capexRatio*(1+shift*.10m);
                if(y<=5)cap=Math.Max(cap,da-change+np*Math.Max(0,g)/o.MaximumForecastRoic);
                // Transition reinvestment to stable g/ROIC, preserving component reconciliation.
                if(y>5) {var blend=(y-5)/5m;var target=np*tg/o.TerminalRoic;cap=(1-blend)*cap+blend*(da+target-change);}
                if(cap<0) cap=0;
                var fcff=ValuationMath.Fcff(np,da,cap,change);
                years.Add(new(y,g,rev,em,op,tax,np,da,cap,change,fcff,ValuationMath.Pv(1,rate,y),ValuationMath.Pv(fcff,rate,y)));
            }
            var terminalNopat=years[^1].Nopat*(1+tg);var reinvest=terminalNopat*tg/o.TerminalRoic;
            var tv=ValuationMath.Terminal(terminalNopat,tg,o.TerminalRoic,rate);var ptv=ValuationMath.Pv(tv,rate,10);
            var explicitPv=years.Sum(x=>x.PresentValueFcff);var ev=explicitPv+ptv;var eq=ev-debt.Value+excess+investments;
            return new(name,rate,tg,o.TerminalRoic,years,terminalNopat,reinvest,terminalNopat-reinvest,tv,ptv,explicitPv,ev,debt.Value,excess,investments,0,eq,shareCount,eq/shareCount);
        }
        r.Scenarios=[Scenario("Bear",-1,wacc+o.ScenarioWaccShift,Math.Max(0,o.TerminalGrowth-o.ScenarioTerminalShift)),Scenario("Base",0,wacc,o.TerminalGrowth),Scenario("Bull",1,wacc-o.ScenarioWaccShift,o.TerminalGrowth+o.ScenarioTerminalShift)];
        var b=r.Scenarios[1];
        if(r.Scenarios.Any(s=>s.FairValue<=0) || r.Scenarios[0].FairValue>b.FairValue || b.FairValue>r.Scenarios[2].FairValue)
        {r.Scenarios.Clear();return Fail("Scenario equity negative or scenarios economically inconsistent; manual review required.","InvalidData");}
        foreach(var dr in new[]{-.01m,-.005m,0,.005m,.01m}) foreach(var dg in new[]{-.01m,-.005m,0,.005m,.01m})
        {
            var rr=wacc+dr;var gg=o.TerminalGrowth+dg;
            // Recalculate transitional reinvestment as well as terminal numerator when changing g.
            r.Sensitivity.Add(new(rr,gg,rr>gg && gg>=0 && o.TerminalRoic>gg?Scenario("Sensitivity",0,rr,gg).FairValue:null));
        }
        var earnings=ValuationMath.Get(p,"NetIncome"); var eb=ebit+ValuationMath.Get(p,"Da")!.Value;
        r.CrossChecks=[new("P/E",earnings>0?price*shareCount/earnings:null,null,null,"Unavailable","Historical price/earnings series not sourced; no hardcoded replacement multiple."),
            new("EV/EBITDA",(price*shareCount+debt-excess-investments)/eb,null,null,"Unavailable","Peer/historical reference multiples unavailable."),
            new("P/FCF",ValuationMath.Get(p,"Ocf")-ValuationMath.Get(p,"CapEx")>0?price*shareCount/(ValuationMath.Get(p,"Ocf")-ValuationMath.Get(p,"CapEx")):null,null,null,"Unavailable","OCF-capex is only a descriptive cash yield cross-check, not FCFF DCF."),
            new("Forward P/E",null,null,null,"Unavailable","No sourced forward EPS.")];
        var quality=100;
        if(!p.IsTtm) quality-=10;if(history.Length<6) quality-=10;
        quality-=10; // partial NWC
        quality-=10; // beta, financing estimates
        quality-=5; // diluted share proxy
        if(rf==null)quality-=5;if(taxHistory.Length==0)quality-=10;
        if(!p.Facts.ContainsKey("TotalDebt")&&!p.Facts.ContainsKey("ShortDebt")&&!p.Facts.ContainsKey("CommercialPaper")) quality-=5;
        if(!p.Facts.ContainsKey("ShortInvestments")&&!p.Facts.ContainsKey("LongInvestments")) quality-=5;
        r.DataQuality=Math.Clamp(quality,0,100);
        var range=(r.Scenarios[2].FairValue-r.Scenarios[0].FairValue)/b.FairValue;
        r.Uncertainty=range>1 || r.DataQuality<60?"VeryHigh":range>.50m || spread>.08m || r.DataQuality<80?"High":"Medium";
        r.MarginOfSafety=r.Uncertainty switch {"Low"=>o.LowMos,"Medium"=>o.MediumMos,"High"=>o.HighMos,_=>o.VeryHighMos};
        r.BuyBelow=b.FairValue*(1-r.MarginOfSafety);r.Status="LimitedData";
        r.Warnings.Add("Model-derived forecast with partial NWC and estimated WACC. Data quality is completeness, not probability of fair value accuracy.");
        if(b.PvTerminal/b.EnterpriseValue>.75m)r.Warnings.Add("More than 75% of enterprise value comes from terminal value.");
        if(ValuationMath.Get(p,"Lease")>0)r.Warnings.Add("Operating leases remain operating expenses; operating lease liabilities are not deducted again. Finance lease classification requires review.");
        return r;
    }
}
