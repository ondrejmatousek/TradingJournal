"""Independent Decimal reconstruction of a saved V2 Base forecast. No .NET engine calls."""
import json
import sys
from decimal import Decimal as D
from pathlib import Path

payload = json.loads(Path(sys.argv[1]).read_text(encoding="utf-8"), parse_float=D, parse_int=D)
r, f = payload["report"], payload["financials"]
p = r["policy"]
a = {x["name"]: x["value"] for x in r["assumptions"]}
b = next(x for x in r["scenarios"] if x["name"] == "Base")
facts = {k: v["value"] for k, v in f["latest"]["facts"].items()}
rev = facts["Revenue"]
margin0 = facts["Ebit"] / rev
nwc0 = facts["Receivables"] + facts.get("Inventory", D(0)) - facts["Payables"]
pv = D(0)

def eq(x, y):
    assert abs(x-y) <= max(D("0.000001"), abs(y)*D("0.0000000001")), (x, y)

for y in range(1, 11):
    g = a["InitialRevenueGrowth"] + (b["terminalGrowth"]-a["InitialRevenueGrowth"])*D(y-1)/9
    rev *= 1+g
    margin = margin0 + (a["TerminalMargin"]-margin0)*D(y)/10
    nopat = rev*margin*(1-a["Tax"])
    da = rev*a["DaRevenue"]
    nwc = rev*a["OperatingNwcRevenue"]
    change = nwc-nwc0
    nwc0 = nwc
    capex = rev*a["CapExRevenue"]
    if y <= 5:
        capex = max(capex, da-change+nopat*max(D(0),g)/p["maximumForecastRoic"])
    else:
        blend = D(y-5)/5
        capex = (1-blend)*capex+blend*(da+nopat*b["terminalGrowth"]/b["terminalRoic"]-change)
    capex = max(D(0), capex)
    fcff = nopat+da-capex-change
    discounted = fcff/(1+b["wacc"])**y
    expected = b["years"][y-1]
    for key, value in {"revenue":rev,"nopat":nopat,"da":da,"capEx":capex,"changeNwc":change,"fcff":fcff,"presentValueFcff":discounted}.items():
        eq(value, expected[key])
    pv += discounted
tv = nopat*(1+b["terminalGrowth"])*(1-b["terminalGrowth"]/b["terminalRoic"])/(b["wacc"]-b["terminalGrowth"])
equity = pv+tv/(1+b["wacc"])**10-b["debt"]+b["excessCash"]+b["investments"]-b["otherClaims"]
fair = equity/b["shares"]
eq(fair, b["fairValue"])
eq(fair*(1-r["marginOfSafety"]), r["buyBelow"])
print(f"PASS: {r['symbol']} independent 10-year FCFF, terminal, equity bridge and MOS; Base {fair:.8f} USD")
