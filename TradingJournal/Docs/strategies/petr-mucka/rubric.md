# Petr Mucka: stabilni hodnotici rubrika

Tato rubrika je odvozena z 20 PDF v adresari `source-pdfs`. Pri pozdejsim review se ma pouzivat jako primarni zdroj pravidel; PDF jsou zdrojovy archiv a nemaji se znovu nacitat pri kazdem tradu.

## Jak rubriku pouzivat

1. Nejdriv oddel kvalitu setupu od vysledku tradu. `Stoploss`, `Breakeven` a `Take profit` nejsou samy o sobe dukazem kvality nebo nekvality setupu.
2. Zkontroluj obrazky a dostupne vstupy. Co neni videt nebo zadane, oznac jako chybejici dukaz. Nevymyslej si HTF, news, risk ani hodnoty z grafu.
3. Projdi vsechna criteria v `rubric.json`, aplikuj vazby a slovne uved, co sedelo, co nesedelo a co nelze overit.
4. Zkontroluj hard blockery. Hard blocker prebiji vysoke konfluencni score a vede k ratingu `No trade` a verdiktu `Nevalidni`.
5. Vypocitej score 0-100 % z pozorovatelnych kriterii. Neznama kriteria neodhaduj; pri nedostatku klicovych dukazu pouzij `Nelze vyhodnotit`.

## Stabilni interpretace strategie

- **Datova uplnost:** pred hodnocenim se kontroluje symbol/trh, datum, cas, smer, vysledek, pripadna poznamka a pritomnost HTF, 15M a 5M/1M screenshotu. Chybejici zasadni dukaz jde do `MissingInformation` a textu `Nelze určit z dodaných screenshotů.`.
- **Daily bias:** ranni analyza, predchozi daily/weekly/monthly close, HTF, price action, support/rezistence, predchozi VWAP a zpravy. Bias se nema behem dne nahodne menit.
- **HTF:** daily urcuje sentiment, 4H cistsi strednedobou strukturu a 15M aktualni situaci. 15M se nema hodnotit izolovane.
- **Structure:** zacina se BOS/I-BOS. Hlavni struktura ma vetsi vahu nez interni; interni vetsi nez CHOCH. BOS ma byt podporen momentum svici a objemem.
- **Liquidity:** pred vstupem musi byt vybrana nebo jasne identifikovana likvidita. Sleduji se highs/lows, knoty, konsolidace, FVG a dOPEN.
- **Supply & demand / OB:** kvalitni zona pred impulzem, idealne s BOS. OB je posledni opacna svicka pred impulzem; S/D a OB se maji pokud mozno kombinovat.
- **FVG:** imbalance, reakce priblizne na 50 % zony. Na 5M je FVG preferovana casteji, na 1M mene; vzdy je potreba dalsi konfluence.
- **Balanční levely:** protažený BOS, entry konfluence nebo target, hlavne spolu s liquidity a trendem.
- **Volume Profile:** pouziva se k upresneni zon a pravdepodobnostnich levelu. nPOC, VAH, VAL a fixed range jsou levely/targety, ne samostatny signal.
- **Sessions:** strategie pracuje s Asia 02:00-06:00, Frankfurt 08:00-09:00, London 09:00-17:00 a NY 14:00-22:00. Vstupni killzones jsou 09:00-11:00 a 13:30-17:00. U konkretni session strategie je rozsah nad 35 pipu nevalidni.
- **Entry model:** kvalitni zona je predpoklad. A+ muze pouzit 1M, A/B casteji 5M structure shift; limitni vstup je preferovany a muze byt lehce pred zonou kvuli spreadu.
- **SL/TP/RRR:** minimalni planovane RRR 1:2, prumer uvadeny kolem 1:2.5. SL ma smysluplne kryt zonu; target muze byt liquidity, S/D, dOPEN, Fibonacci, Asia H-L, VWAP close, nPOC nebo daily close.
- **Risk:** material uvadi 0.5 % nebo 1 % na obchod, 2 % denne a 4 % tydne. SL se nema posouvat na BE pred prolomenim duleziteho 15M levelu.

## Score a rating

Vahy a prahy jsou v `rubric.json`. PDF neobsahuji oficialni Petrovu ciselnu scorecard, proto jsou zde vazby oznacene jako provozni konvence pro opakovatelnost, ne jako doslovny citat Petrova bodovani.

Agregovane provozni skupiny maji vahy: datova uplnost 10 %, daily bias + HTF 15 %, market structure 15 %, likvidita 15 %, konfluence S/D/OB/FVG/balance/volume 15 %, entry model 10 %, killzone/session/news 10 % a RRR/risk management 10 %. Detailni criteria v JSON rozdeluji tyto skupiny.

- `A+`: 90-100 %
- `A`: 80-89.99 %
- `B`: 65-79.99 %
- `C`: 50-64.99 %
- `No trade`: 0-49.99 % nebo hard blocker

Mapovani verdiktu:

- `A+`, `A` -> `Validni`
- `B` -> `Částečně validní`
- `C`, `No trade`, hard blocker -> `Nevalidni`
- nedostatek klicovych dukazu -> `Nelze vyhodnotit`

## Poznamky k nejasnostem

- Presne vahy, ratingove hranice a pravidlo agregace nejsou v PDF specifikovany.
- CHOCH je v jednom materialu vysvetlen jako struktura, v entry modelu je ale uvedeno, ze se na nej nema spolehat; proto je pouze doplnkove potvrzeni.
- Slide s klicovymi castmi Volume Profile je obrazovy a textovy export nedal presnou definici vsech casti. NPOC/VAH/VAL se nesmi domyslet z nejasneho screenshotu.
- Casova zona screenshotu musi byt overena, stejne jako pip/tick jednotky u jinych instrumentu nez forex.
- 35 pipu patri ke konkretni session strategii, ne automaticky ke kazdemu setupu.
- Pri nejasnosti se ma uvést `Nelze vyhodnotit`, ne doplnit pravdepodobny fakt.
