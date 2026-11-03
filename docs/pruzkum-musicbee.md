# Průzkum MusicBee – 2. 11. 2026

Cílový build pro tento vývoj a uživatelské ověřování: **3.7.9686 P**. Číslo je převzato od uživatele; netvrdíme, že jde o poslední patch všech větví v době čtení dokumentu.

## Obnova po spánku

- Uživatelův log: Windows se probudil ve 20:45:42.240, stav Playing přišel ve 20:45:43.915. Plugin měl minimální prodlevu 3 sekundy a nezapsal vlastní přehrávací příkaz. V tomto testu obnovu provedlo MusicBee.
- [Resume Playback does not work when originally playing Internet radio stream](https://getmusicbee.com/forum/index.php?topic=38753.0): hlášení z ledna 2023 popisuje chybu streamu po probuzení a opožděné zprovoznění sítě. Steven 29. 1. 2023 zmiňuje automatické opakování v BASS ve verzi 3.5. Autor poté uvádí, že již používá 3.5.8402. Nejde tedy o doklad, že byla každá situace opravena.
- [Musicbee Will not resume from Sleep Mode](https://getmusicbee.com/forum/index.php?topic=25579.0): původně MusicBee 3.1.6590, hlášení 2018 a diskuse 2021. Starší doklad potíží s obnovením streamu a zařízení; není důkazem chyby aktuálního buildu.
- Uživatelem dodané [MB sometimes starts to play by itself when Windows wakes from Sleep](https://getmusicbee.com/forum/index.php?topic=36277.msg198721#msg198721): březen 2022, opačný problém – MusicBee někdy začne hrát i po předchozím Pause/Stop. Přímé načtení odkazu vracelo HTTP 403; obsah dodal uživatel.
- Při hledání nebylo nalezeno přesvědčivé současné hlášení stejného selhání obnovy konkrétně pro 3.7.9686 P. Neexistence nalezeného hlášení není důkazem bezchybnosti.

Volba pro sleep zatím zůstává jako záložní funkce. O tom, zda ji z produktu odstranit, dosud nebylo rozhodnuto. Její vypnutí pouze vypne zásahy pluginu, ne vlastní obnovu přehrávače.

## Portable ukládání

[API diskuse](https://getmusicbee.com/forum/index.php?topic=24376.75) uvádí, že `Setting_GetPersistentStoragePath()` vrací v portable instalaci cestu pod adresářem MusicBee. Novější [diskuse z roku 2024](https://getmusicbee.com/forum/index.php?topic=41682.0) výslovně pracuje s `D:\Musicbee\AppData`.

V uživatelově instalaci byla čtením souborového systému ověřena konfigurace i log v `<MusicBee>\AppData\MusicBee-Autoresume\`. Odpovídající složka ve Windows Roaming profilu neexistovala. Uživatel potvrdil, že šlo o záměnu místa při hledání souborů. Původní portable ukládání bylo správné. Cesta se na jeho přání nezobrazuje v nastavení; nepřidává se vlastní detekce instalace, přesměrování ani migrace.

Soubor `MusicBee.exe.config` této instalace navíc přímo potvrzuje cílení hostitele na .NET Framework 4.8.

## Lokalizace

Dodané SDK poskytuje `MB_GetLocalisation(string id, string defaultText)`, ale nemá veřejný getter jazyka UI ani odpovídající `SettingId`. Verze 0.4 má vlastní anglické a české prostředky vložené v DLL. Uživatel schválil výběr podle `SystemLanguage` v `MusicBee3Settings.ini` v persistentní cestě hostitele; v jeho instalaci bylo ověřeno `Czech`. Soubor se pouze čte s omezením rozsahu, při chybě zůstává angličtina. Neparsuje se celý jako XML, protože obsahuje i jiná data.

Rozpoznávání chybového dialogu hostitele je samostatná věc: přesné aktuální překlady čtyř schválených síťových zpráv poskytuje API. Původní české signatury a dočasné překladové sondy byly odstraněny; vlastní prostředky neobsahují překlady hlášek hostitele.

## Skinned chybový dialog – ověření 3. 11. 2026

Log 0.3.1 z 01:47:39 a opakovaný snímek v 01:47:41 ukazují:

- modální okno ve stejném UI vlákně a procesu jako hlavní MusicBee;
- jediné viditelné tlačítko, které implementuje `IButtonControl`, ale není standardním `Button`;
- viditelné tlačítko má `DialogResult.Yes`, sdílí `AcceptButton` i `CancelButton`; další dvě tlačítka No a Cancel jsou skrytá;
- veřejné `Text` vlastnosti vypsaných prvků neobsahují rozpoznanou zprávu; kořen přístupnosti vrací `children=-1`.

To vysvětluje původní výsledek `visible buttons=0`. Zpráva může být kreslená přímo, ale dosavadní výpis neprokazuje, kde ji hostitel uchovává. Typ formuláře a obecná tlačítka samy o sobě nerozlišují chybu rádia od jiných hlášek. Verze 0.3.2 proto opravuje kontrakt tlačítka a přidává diagnostiku přístupnosti jednotlivých prvků a zásobníku; univerzální odklikávání různých chyb nezavádí.

## Zpráva mimo veřejný strom prvků – statické ověření 3. 11. 2026

Log 0.3.2 potvrdil `visible buttons=1`, ale zprávu nezpřístupnil ani strom přístupnosti jednotlivých prvků. Následovala pouze statická prohlídka metadat a IL uživatelova `MusicBee.exe`, assembly verze `3.7.9686.34861`. Assembly bylo načteno v reflection-only režimu; kód hostitele nebyl spouštěn, instalace nebyla měněna.

Bylo ověřeno:

- Třída dialogu je obecný zobrazovač zpráv, nikoli specifický typ chyby rádia. Samotný typ ani zásobník nejsou dostatečným pravidlem pro kliknutí.
- Tělo zprávy se v tomto buildu zapisuje do jediného deklarovaného privátního instance pole typu `System.String`; není dostupné přes vlastní veřejnou vlastnost. To vysvětluje chybějící text standardních kontrol.
- Chybová větev rádia používá lokalizační klíč `Main.msg.Rscnbo` a anglickou šablonu `Radio station '{0}' is not responding and may no longer be a valid link`.
- Tentýž handler obsahuje i jiné chyby. Síťové větve používají `Main.msg.Ticto` (`The internet connection timed out`), `Main.msg.Utpts` (`The requested stream is not responding, try later.`) a `Main.msg.Trsinaatpi` (`The radio station is not accessible at this point in time`). Jiné větve řeší například chybějící soubor nebo výstupní zařízení.

Uživatel schválil variantu získání přesných šablon přes veřejné `MB_GetLocalisation` a izolované přečtení jediného jednoznačného string pole bez závislosti na jeho obfuskovaném názvu. Při nesouladu struktury nebo zprávy se neklikne. Ve verzi 0.3.3 uživatel potvrdil automatické odkliknutí i střídání dvou streamů s návratem na začátek. Verze 0.4 rozšiřuje rozpoznávání na tři další schválené síťové zprávy uvedené výše. Strukturální debug a přímá reference Accessibility byly odstraněny; zůstává omezený provozní log.

## Přechod ve frontě – omezení API

[Diskuse API z května 2025](https://getmusicbee.com/forum/index.php?topic=29381.15) potvrzuje, že `NowPlayingList_PlayNow(url)` vybírá existující položku podle URL, nikoli podle indexu. Pokud stejná URL existuje vícekrát, vybere její první výskyt. Proto nelze touto funkcí slíbit přesné rozlišování duplicitních řádků. Plugin před voláním znovu ověřuje aktuální položku a celou frontu a vybírá jen URL, která v ní již je.
