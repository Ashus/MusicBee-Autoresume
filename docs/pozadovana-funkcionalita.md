# MusicBee-Autoresume – požadovaná funkcionalita

## Cíl a výstup

C# plugin pro MusicBee automaticky obnovuje poslední přehrávání ve třech nezávisle volitelných situacích. Výstup je `MB_Autoresume.dll`, který se instaluje do složky `Plugins` instalace MusicBee. Cílový framework je .NET Framework 4.8 bez Client Profile, platforma x86 (také na 64bit Windows); uživatel tento cíl schválil.

Dodané SDK v `sdk/` ponechat beze změn. Jeho ukázkový projekt cílí na .NET Framework 4.8. Původní text o 4.0 Client Profile pochází z webu MusicBee, nejde o uživatelův požadavek na podporu historických runtime.

Verze 0.4 obsahuje nastavení v angličtině a češtině, autoplay při startu, volitelnou záložní obnovu po spánku a ověřování chybové zprávy podle překladu z veřejného API MusicBee. Uživatel potvrdil v cílovém MusicBee 3.7.9686 P odkliknutí chyby i střídání dvou streamů včetně návratu na začátek. Postup dalšího ověření je v [testovani.md](testovani.md); historický průzkum je v [pruzkum-musicbee.md](pruzkum-musicbee.md).

### Ověření cílového frameworku (2. 11. 2026)

Oficiální [stránka API](https://getmusicbee.com/help/api/) stále uvádí .NET 4.0 Client Profile. Proti tomu Steven na oficiálním fóru v odpovědi ze 7. 5. 2023 výslovně uvádí **4.8** jako použitelnou verzi pro pluginy: [Latest Dotnet version, and Plugin entry point](https://getmusicbee.com/forum/index.php?topic=39264.0). To odpovídá dodanému ukázkovému projektu SDK.

Podle [dokumentace Microsoftu](https://learn.microsoft.com/en-us/dotnet/framework/deployment/client-profile) byl Client Profile od verze 4.5 zrušen. Pro 4.8 se proto používá plný .NET Framework bez profilu Client; nejde o moderní .NET 8/10.

Schválený cíl nového pluginu je **.NET Framework 4.8, x86, bez Client Profile**. Údaj 4.0 na stránce API vyhodnocujeme jako historický pokyn, nikoli horní hranici podporované verze. Projekt byl na tento cíl přepnut. Funkčnost DLL je následně nutné ověřit v MusicBee.

### Sestavení bez systémového Developer Packu

Projekt používá NuGet balíček `Microsoft.NETFramework.ReferenceAssemblies` verze `1.0.3` s `PrivateAssets="All"`. Ten dodá referenční sestavení pro cílový framework pouze pro kompilaci. Balíčky se obnovují do ignorované složky `.packages/` v projektu; reference se k pluginu nedistribuují. Systémová instalace Developer Packu není potřeba, pro běh v MusicBee je nadále nutný .NET Framework 4.8.

Příkaz pro obnovení balíčků a sestavení:

```bat
dotnet msbuild MusicBee-Autoresume.csproj -restore -p:Configuration=Release
```

Výstup: `bin/Release/MB_Autoresume.dll`. První obnova balíčků vyžaduje dostupný NuGet zdroj. Pro sestavení i testovacího projektu použij místo `.csproj` soubor `MusicBee-Autoresume.sln`.

## Nastavení

Konfigurace se zobrazuje přímo v panelu MusicBee bez vlastního rámečku skupiny. Anglické názvy tří voleb jsou:

- `Autoplay on MusicBee launch`
- `Resume playing after sleep`
- `Resume playing after connection error`

Hodnoty se musí zachovat mezi restarty MusicBee. Konfiguraci ukládat do vlastní podsložky cesty poskytované `Setting_GetPersistentStoragePath()`, nikoli vedle DLL. Uložení musí odolat přerušenému zápisu; neznámé položky či chybějící hodnoty nesmí způsobit pád. Poškození nebo nemožnost zápisu oznámit srozumitelně bez opakovaných dialogů. Autoplay a obnova po chybě připojení jsou při nové instalaci zapnuté; obnova po spánku je standardně vypnutá. Při aktualizaci se zachovávají všechny výslovně uložené hodnoty včetně již zapnuté obnovy po spánku. Chybějící položka používá výchozí hodnotu dané volby, neplatná booleovská hodnota se vypne.

Výchozím jazykem UI je angličtina. Při českém UI MusicBee jsou checkboxy, popis pluginu, vysvětlení a běžná upozornění česky. Jazyk se čte z `SystemLanguage` v `MusicBee3Settings.ini` v cestě z `Setting_GetPersistentStoragePath()`, nikoli z Windows ani porovnáváním překladů. Při chybě čtení nebo neznámém jazyce zůstává angličtina. Oba `.resx` prostředky obsahují pouze vlastní texty pluginu a jsou vloženy přímo do hlavní DLL, bez dalších jazykových DLL. Logy a technické výjimky zůstávají anglicky.

## Autoplay on MusicBee launch

Po dokončeném startu MusicBee obnovit poslední položku / stream dostupný v přehrávači prostřednictvím veřejného API, obdobně jako stisk Play. Vyčkat na připravenost hostitele. Pokud již přehrává nebo načítá, nesmí další volání přehrávání pozastavit nebo spustit souběžný pokus. Pokud není co přehrát, nic nespouštět a neměnit frontu.

Autoplay znamená spuštění přehrávání při startu MusicBee, nikoli spouštění MusicBee při přihlášení Windows. Na předchozí stav Playing/Pause/Stop při zavření se nebere ohled: jde o spuštění aktuálně obnovené položky. Již běžící přehrávání se ale nesmí přepnout na Pause. Vlastní ukládání fronty či přesné pozice není požadováno; má se využít stav obnovený MusicBee. Nutnost vlastní persistence poslední položky prověřit v hostiteli.

## Resume playing after sleep

Reagovat na probuzení Windows ze spánku i hibernace. Obnovit přehrávání, které bylo aktivní před uspáním; ručně pozastavený nebo zastavený přehrávač nemá po probuzení sám začít hrát. Toto chování uživatel potvrdil; přesné načasování a zachycení stavu se může doladit při testování.

V uživatelově testu z 2. 11. 2026 obnovilo MusicBee přehrávání samo asi 1,7 sekundy po probuzení, před třísekundovým pokusem pluginu. Volba je zatím ponechána jako záloha; pro tento build nebyla doložena nutnost. Nevypíná ani neblokuje vlastní obnovu MusicBee, takže případné nechtěné spuštění hostitelem neřeší.

Zohlednit opožděnou dostupnost zvukového zařízení i sítě. Duplicitní události probuzení sloučit do jedné obnovy. Ruční zásah uživatele nebo změna položky během čekání zruší původní obnovu. Události napájení odregistrovat při vypnutí pluginu.

## Resume playing after connection error

Řešit dlouhý výpadek při přehrávání webového streamu. Krátký výpadek může zvládnout BASS; orientačních 10 sekund z popisu není pevný detekční limit. Po delším výpadku MusicBee zobrazí hlášku o nemožnosti přehrávání a běžně vyžaduje její potvrzení a nové Play.

Požadovaný výsledný stav: přehrávání nezůstane trvale zastavené na chybě nedostupné stanice. Chybový dialog se musí buď potlačit, nebo cíleně potvrdit bez ručního zásahu. Po chybě je dovoleno přejít na následující existující položku fronty; po konci se pokračuje od začátku bez ohledu na Repeat. Jediná položka se může znovu zkusit. Fronta se nemaže ani nepřepisuje; ruční Stop/Pause, výběr jiné položky, změna fronty a vypnutí volby ruší čekající obnovu.

Po neúspěšném průchodu seznamem se prodlužuje prodleva od 3 sekund až k 60 sekundám. Dokud není dostupná žádná stanice nebo síť, zvuk zajistit nelze; plugin může dál provádět odstupňované pokusy. Dostupný alternativní soubor lze přehrát i bez síťového rozhraní.

Pokusy provádět postupně, s prodlevou a omezením frekvence; po úspěchu je ukončit. Dostupné síťové rozhraní samo o sobě nepotvrzuje dostupnost streamu. Chování musí zvládnout i opakované výpadky, nedostupný server a čekání přes další uspání. Obnova nesmí generovat nekonečnou sérii chybových dialogů.

### Kritické ověření před implementací

V dodaném `NotificationType` není samostatná událost chyby přehrávání. `PlayStateChanged` samo nerozlišuje síťovou chybu od ručního Stop. Je třeba reprodukovat dlouhý výpadek v MusicBee a zaznamenat posloupnost událostí, aktuální položku a stavy přehrávače.

V dodaném veřejném API nebyl nalezen přepínač potlačení této chyby. `MB_GetLocalisation` poskytuje překlad; nezabraňuje zobrazení dialogu. Verze 0.3.3 byla uživatelem ověřena při dvou střídajících se stanicích.

Adaptér hledá viditelný modální dialog MusicBee a jediné aktivní výchozí `IButtonControl` s návratovou hodnotou OK nebo Yes. Nezávisle ověřuje celou zprávu proti šabloně z `MB_GetLocalisation("Main.msg.Rscnbo", ...)`; lišit se smí pouze neprázdný název stanice v jediném `{0}`. Pro neznámý překlad nebo nepodporované formátování se neklikne.

Uživatel schválil izolovaný čtecí fallback pro skinned dialog: pouze jedno jednoznačné deklarované instance pole typu string, bez závislosti na obfuskovaném názvu a bez zápisu nebo volání soukromých metod. Pokud je polí více, pole chybí nebo se zpráva neshoduje, dialog zůstane otevřený. To je výslovná výjimka z preference veřejného API; žádné obecné potvrzování chyb se nepoužívá.

Současná obnova je nezávislá na Repeat. Repeat řídí běžný konec přehrávání, nikoli obnovení po potvrzené chybě. Závislost na Repeat byla diskutována, nikoli schválena jako změna; dosavadní chování proto zůstává.

Uživatel dále schválil přesné rozpoznávání síťových zpráv `Main.msg.Ticto`, `Main.msg.Utpts` a `Main.msg.Trsinaatpi`. Jejich aktuální překlady se získávají výhradně přes API; porovnává se celý text bez proměnných částí. Chyby výstupního zařízení, formátu a místních souborů se nepotvrzují. Odpojení Bluetooth reproduktoru nezpůsobí přeskakování stanic ani automatické přepnutí zařízení pluginem; vlastní chování hostitele zatím není ověřeno.

## Odolnost a kompatibilita

- Veškerá volání MusicBee soustředit do malé adaptační vrstvy nad dodaným SDK; neměnit ABI, pořadí polí ani marshaling SDK.
- Ověřit podporovanou verzi rozhraní a dostupnost potřebných funkcí. Nepodporovanou funkci bezpečně vypnout s diagnostikou; nepokoušet se volat neplatné ukazatele.
- Před Play znovu ověřit stav a identitu položky. Pokud API nabízí pouze přepínač Play/Pause, nesmí volání při již běžícím přehrávání způsobit Pause.
- Použít společné řízení obnovy pro start, probuzení a síťovou chybu, aby se pokusy nepřekrývaly. Opožděné callbacky po vypnutí pluginu musí být neúčinné.
- Respektovat vláknové požadavky hostitele, neblokovat jeho UI čekáním na síť a nepřenášet výjimky pluginu do hostitele. Zachytávat očekávané chyby na hranicích integrace, neskrývat je bez diagnostiky.
- Při Close zrušit čekání, časovače a odběry událostí. Zakázání příslušné volby musí zrušit rozpracovanou obnovu.
- Diagnostiku udržovat omezenou velikostí a bez přihlašovacích údajů či citlivých parametrů URL. Zdrojový kód, komentáře, logy a technické výjimky jsou anglicky podle AGENTS.md. Běžné uživatelské texty mají i české prostředky; dokumentace a komunikace jsou česky. Podrobný strukturální debug, zásobníky a přímá závislost na Accessibility byly v 0.4 odstraněny. Provozní log má nejvýše dva soubory: aktuální a `.1`, rotace po přibližně 256 KiB, nikoli při každém spuštění.
- Preferovat veřejné API; jedinou schválenou neveřejnou závislostí je izolované čtení těla skinned chybového dialogu s přísným ověřením struktury a zprávy.

## Přijímací scénáře

1. MusicBee načte x86 DLL z `Plugins`, zobrazí plugin a všechny tři checkboxy.
2. Každá kombinace voleb přežije restart; vypnutá volba nespouští příslušnou obnovu.
3. Zapnutý autostart spustí poslední dostupnou položku, prázdná fronta nezpůsobí chybu a již běžící přehrávání se nepozastaví.
4. Spánek i hibernace při přehrávání lokálního souboru a streamu skončí obnovením; ruční Pause/Stop se respektuje.
5. Krátký výpadek sítě nevyvolá duplicitní Play. Dlouhý výpadek s rozpoznanou chybovou hláškou vede k automatickému potvrzení a pokusu o další položku, včetně přechodu z konce na začátek, bez zásahu uživatele.
6. Nedostupný server nevede k zatížení CPU, zahlcení logu ani sérii dialogů. Ruční Stop, změna stanice a vypnutí volby zruší čekající pokusy.
7. Souběh startu/probuzení/návratu sítě nevede k více obnovám. Ukončení a zakázání pluginu během čekání nezpůsobí pozdější volání API.
8. Chybějící, poškozená či nezapisovatelná konfigurace a nepodporované API nezpůsobí pád MusicBee.

## Zdroje a stav ověření

- https://getmusicbee.com/help/api/ – oficiální odkaz z požadavku; přímé načtení vrací HTTP 403, indexovaný obsah vyhledávače stále uvádí 4.0 Client Profile.
- https://musicbee.fandom.com/wiki/Tutorial:_Creating_A_Simple_Plugin – postup vytvoření pluginu, menu a základní práce se stavem přehrávače; jde o komunitní návod.
- `sdk/MusicBeeInterface.cs`, `sdk/TestCSharpDll.cs`, `sdk/CSharpDll.csproj` – primární lokální podklad pro konkrétní podpisy API, životní cyklus, konfiguraci a notifikace.

Otevřené otázky: prokazatelný přínos záložní obnovy po spánku v aktuálních buildech a ověření skutečných dialogů v dalších jazykových mutacích. Interní průběh obnovy v MusicBee/BASS neznáme; uživatelský README.md vznikne až po zpřesnění chování.
