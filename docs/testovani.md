# Ověření pluginu v MusicBee

Cílový build uživatelského testu: **MusicBee 3.7.9686 P**. Potvrzení chybového dialogu a střídání dvou streamů včetně návratu na začátek uživatel ověřil ve verzi 0.3.3. Verze 0.4 upravuje lokalizaci, vzhled, výchozí volbu spánku a rozsah logování.

## Instalace a nastavení

1. Ukonči MusicBee a nahraď `MB_Autoresume.dll` souborem z `bin/Release/` ve složce `Plugins`. Kopíruj jen DLL; oba jazykové prostředky jsou vložené uvnitř.
2. V předvolbách otevři konfiguraci pluginu. Checkboxy jsou přímo v panelu hostitele, bez vlastního rámečku.
3. V českém MusicBee se zobrazují české texty, v anglickém a ostatních jazycích anglické. Jazyk určuje hodnota `SystemLanguage` v `MusicBee3Settings.ini` v cestě z API `Setting_GetPersistentStoragePath()`. Soubor se pouze čte; při chybě nebo neznámém jazyce zůstává angličtina. Nastavení Windows se nepoužívá.
4. Nová konfigurace: autoplay a síťová obnova zapnuté, obnova po spánku vypnutá. Uložené hodnoty se zachovávají; nová výchozí hodnota nepřepne uživateli již uložené zapnutí spánku.
5. Změny potvrď přes Apply/Save. Ověř jejich zachování po restartu.
6. Se stále otevřenými předvolbami plugin vypni a znovu zapni. Při vypnutí se volby skryjí, při zapnutí se vrátí bez zavření předvoleb. Další plugin pod Autoresume musí po celou dobu zůstat viditelný. Ověř také uložení voleb a opakování celého postupu.

Konfigurace je v `<cesta z Setting_GetPersistentStoragePath()>\MusicBee-Autoresume\settings.xml`. Pro portable instalaci jde o `<MusicBee>\AppData\MusicBee-Autoresume\`. Cesta se v nastavení nezobrazuje.

## Autoplay při spuštění

Zapnutá volba spustí položku obnovenou MusicBee bez ohledu na to, zda před zavřením přehrávač hrál, byl pozastavený nebo zastavený. Pokud už hostitel přehrává, plugin nepošle Play/Pause. Při načítání nebo prázdné aktuální položce vyčkává, nejvýše 30 sekund od naplánování. Frontu ani pozici sám neukládá.

Ruční Stop/Pause nebo změna položky/fronty během čekání původní obnovu ruší. Vypnutí volby neovlivňuje vlastní nastavení spuštění přehrávání v MusicBee. Inicializační událost může přijít i při opětovném zapnutí pluginu za běhu.

## Obnova po spánku

Volba je nově standardně vypnutá a ponechaná jako volitelná záloha. V dosavadním uživatelském testu přehrávání obnovilo samo MusicBee před pokusem pluginu. Nevíme, jak přesně hostitel a BASS obnovu interně řídí.

Při zapnutí plugin použije poslední známý stav před uspáním a obnovuje pouze předchozí Playing. Po probuzení počká nejméně 3 sekundy a při nedostupnosti síťového rozhraní nebo načítání nejvýše 60 sekund. Již běžící přehrávání nepřerušuje. Ověř lokální soubor i stream, spánek i hibernaci a předchozí Pause/Stop.

Může sloužit jako záloha, když přehrávač zůstane zastavený, ale zatím není prokázáno, že vyřeší předčasný vlastní pokus hostitele před návratem sítě. Pokud hostitel zobrazí rozpoznanou síťovou chybu rádia, jde o samostatnou funkci obnovy po chybě připojení. Podrobnosti pro uživatelský README.md se doplní až po ověření těchto situací.

## Obnova po chybě rádia

Obnova zůstává nezávislá na Repeat. Ověř:

- dvě stanice: po potvrzené chybě další stanice, po konci znovu první, s Repeat vypnutým i zapnutým;
- jediný vzdálený stream: po každé další rozpoznané chybě znovu tentýž stream, bez limitu počtu pokusů;
- ruční Stop/Pause, jiný výběr, změna fronty nebo vypnutí volby: zrušení původního čekajícího pokusu;
- již obnovené přehrávání: žádné další přepnutí ani přeskok;
- neznámá nebo nesíťová chyba: žádné automatické potvrzení.

Plugin nemaže frontu ani nepřepíná nastavení Repeat. Používá jen již přítomné URL přes veřejné `NowPlayingList_PlayNow`; API při duplicitních URL vybírá první shodu. Pokud jsou všechny zdroje nebo síť nedostupné, nelze zajistit zvuk.

Zprávu porovnává s překladem šablony `Main.msg.Rscnbo` získaným přes `MB_GetLocalisation`. U této šablony se smí lišit pouze neprázdný název stanice v `{0}`. Dále přesně rozpoznává tři schválené síťové zprávy `Main.msg.Ticto` (timeout), `Main.msg.Utpts` (neodpovídající stream) a `Main.msg.Trsinaatpi` (momentálně nedostupné rádio). Překlady hostitele nejsou uloženy v našich prostředcích. Skinned tlačítko se ovládá přes veřejné `IButtonControl`. Tělo zprávy čte izolovaný schválený fallback z jediného jednoznačného string pole. Při změně struktury nebo neshodě se neklikne.

Po každém neúspěšném průchodu frontou se prodleva zdvojnásobí z 3 sekund až na 60 sekund. Opožděné notifikace hostitele nesmí historii neúspěchů vynulovat; úspěšné přehrávání ji resetuje. Chyby výstupního zařízení se nepotvrzují a plugin zařízení nepřepíná. Chování MusicBee při odpojení a opětovném připojení Bluetooth reproduktoru zatím není ověřeno.

Veřejné API potlačení této chyby před zobrazením jsme nenašli; lokalizační API pouze vrací text. Jde o cílené potvrzení zobrazeného dialogu, nikoli o obecné zavírání hlášek.

## Provozní log

Zůstává `autoresume.log` a nejvýše jedna předchozí kopie `autoresume.log.1`. Otáčení probíhá před dalším zápisem, pokud aktuální soubor přesáhl 256 KiB, nikoli při každém spuštění MusicBee. Limit může být překročen o jednu zprávu. Při další rotaci se stará `.1` nahradí.

Log obsahuje anglické provozní události, stavy a případné chyby. Nezaznamenává URL, názvy stanic ani tělo zprávy. Strukturální WinForms/Win32 debug, přístupnost a zásobníky volání byly odstraněny spolu s přímou závislostí na Accessibility. Staré řádky z předchozí verze mohou zůstat v existujících souborech do jejich otočení.

## Automatické ověření

```bat
dotnet msbuild MusicBee-Autoresume.sln -restore -p:Configuration=Release
tests\bin\Release\Autoresume.Tests.exe
```

Testy ověřují přesné lokalizované šablony, jiný název stanice, odmítnutí dalšího textu a neplatných placeholderů, izolovaný čtecí fallback, skinned tlačítko bez závislosti na nápisu, ochranu nesouvisejících dialogů, výchozí i uložené volby a anglické/české prostředky vložené do DLL. Skutečný vzhled panelu a jazyk UI je třeba ověřit v MusicBee.
