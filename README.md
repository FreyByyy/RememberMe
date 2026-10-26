# Remember Me?

![Remember Me?](About/Preview.png)

A RimWorld 1.6 and 1.7 (Waylith) mod: colonists from your old games turn up in your new world.

The mod reads your old saves in the background and keeps an archive of every colonist who ever lived in them, alive or dead. With a small chance, a pawn the game is about to create becomes one of those people instead, with the same name, look, traits, skills, backstory, genes and prosthetics.

Where they end up depends on who they were:

- former ideology leaders and nobles tend to become faction leaders;
- veterans with many kills and bloodthirsty traits turn up in raids;
- merchants run trade caravans;
- tribal colonists join tribes, xenohumans join factions of their own kind;
- others come as visitors, refugees, wanderers who join, or slaves for sale;
- children from old colonies come back grown up;
- two people from the same old colony get an "old comrade" relation.

Pawns from the game you are playing (its own saves and autosaves) are never used, and neither is anyone who shares a name with a living pawn.

Colonists who died come back through their own rare event: as a wanderer, and with Anomaly as a creepjoiner or a shambler.

Settings: chance per event, chance per new faction leader, cooldown, letters, which roles are used, which old colonies count.

## Stories and scars

- **Stories.** The "familiar face" letter tells what the person went through since their old colony: wanderers and refugees get a longer story, visitors, traders, raiders and slaves a few sentences, the returned dead something left half unsaid. Stories are built with the game's own grammar engine from parts in `Defs/PastLives_Stories.xml` (Russian in `Languages/Russian (Русский)/DefInjected/RulePackDef`), and may name a faction of your world, a colonist of yours from the same old colony, or how many years have passed. On by default.
- **New scars and traits (experimental, off by default).** Old healed scars on random body parts (never lost limbs, eyes or ears) and sometimes a new trait that fits the role, on top of the old ones. Chances and the number of scars are configurable; the returned dead always get scars by default. When a story left a mark (a burning escape pod, a pirate cage), the story decides which scars and which trait.

## My list

Besides old saves, the mod keeps your own list of pawns:

- **Remember button** on every colonist adds them to the list (press again to forget). It can be hidden in the settings. Each pawn is a file in `SaveData/PastLives/MyList`; a pawn file in the game's own format dropped there works too.
- **Pawn Editor**: pawns saved with Pawn Editor are picked up from its save folder.
- **Character Editor (experimental)**: pawns from Character Editor's slots. Its slot format is undocumented, so this may break with a Character Editor update. Off by default.

Neither editor is a dependency; only their files are read. Imported pawns are kept by this mod, so they are not lost if the editor's own list is reset.

List settings: how much more often list pawns are picked (weight slider), "only my list" (ignore old saves), and repeat appearances (a list pawn may come back if their previous version in this world died; off by default). Pawns from the list without a known colony are introduced as "one of your saved colonists".

## Русский

Колонисты из ваших старых игр никуда не делись. Мод читает старые сохранения, собирает архив всех колонистов и иногда возвращает кого-то из них в новый мир: гостем, торговцем, рейдером, беженцем, рабом на продажу, лидером фракции или новым членом колонии. Кем станет человек, зависит от его прошлой жизни. Погибшие возвращаются отдельным событием, а с Anomaly приходят жутким странником или бродячим мертвецом.

Кроме сейвов есть «Мой список»: кнопка «Запомнить» на колонисте, папка с файлами пешек, импорт из Pawn Editor и (экспериментально) из Character Editor. Пешки из списка выбираются чаще (ползунок веса), можно включить режим «только мой список» и повторные появления.

## Requirements

- RimWorld 1.6 or 1.7
- [Harmony](https://steamcommunity.com/sharedfiles/filedetails/?id=2009463077)
- DLCs are optional; Royalty, Ideology, Biotech and Anomaly content is used when present.

## Installing

Copy `About`, `Defs`, `Languages`, `Textures`, `1.6` and `1.7` into `RimWorld/Mods/RememberMe` and enable the mod after Harmony.

## Building

Needs the .NET 10 SDK. One project builds both versions from the same source:

```bash
dotnet build Source/PastLives/PastLives.csproj -c Release            # both
dotnet build Source/PastLives/PastLives.csproj -c Release -f net10.0 # 1.7 only
dotnet build Source/PastLives/PastLives.csproj -c Release -f net472  # 1.6 only
```

- **1.7** (`net10.0`, output `1.7/Assemblies`) expects RimWorld 1.7 at the default Steam path and Harmony from the Workshop. Override with `-p:RimWorldDir=...` and `-p:HarmonyDir=...` if yours are elsewhere.
- **1.6** (`net472`, output `1.6/Assemblies`) takes the game and Harmony reference assemblies from NuGet (`Krafs.Rimworld.Ref`, `Lib.Harmony`), so no 1.6 install is needed.

## Dev mode

With dev mode on, the debug actions menu has a "Remember Me?" category: spawn a specific archived person (or a random one) as a wanderer, visitor, trader, raider, faction leader or returning dead; raids, visitors and caravans with several past colonists; story previews in the log; scars and thematic traits on a chosen pawn; reset who has already appeared; re-read saves and log archive stats. These ignore chances, cooldown and settings.

## Self-test

Launch the game with `-quicktest -pastlives-selftest` (plus `-mod=2009463077;frbyyy.rememberme` to load only Harmony and this mod). It forces every kind of appearance once, writes the results to `Player.log` and quits.

The archive is stored in `SaveData/PastLives/archive.xml`.
