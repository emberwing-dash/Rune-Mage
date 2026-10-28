# Rune Mage — The Emberbound Grimoire

Mixed-reality spellcasting for Meta Quest 3 / 3S (Unity 6, Meta XR All-in-One SDK 207, passthrough, hand tracking, MRUK).
You draw glowing seals and runes in the air with a pinch, then release the spell with a palm push or a spoken word. No controllers, no typing, no weapons.

## First-time setup (once)

There is no setup step: the scenes in `Assets/Scenes` are already built, just open `MainMenu` and press Play. For a headless re-import:

```
Unity.exe -batchmode -nographics -projectPath <project> -executeMethod RuneMage.EditorTools.RuneMageSetup.RunAll -quit
```

It sets hand tracking (controllers + hands), passthrough and scene support to *Required* in the Oculus project config, IL2CPP/ARM64,
imports the art, builds `Assets/RuneMage/Resources/RuneMageAssets.asset`, and builds `Assets/Scenes/RuneMage.unity`
(OVRCameraRig + OVRManager passthrough + OVRPassthroughLayer + both OVRHand prefabs + MRUK + the `Game` root) as build scene 0.

## Architecture (all runtime code is in `RuneMage.Runtime`, namespace `RuneMage`)

| Folder | Responsibility |
|---|---|
| `Recognition/` | Pure C# (unit-testable): `$P` point-cloud rune recogniser, ring (seal) detector with circle fit, rune shape table |
| `Hands/HandInput` | OVRHand/OVRSkeleton → index tip, pinch ink, push-to-cast, palm-up/down grimoire, swipe. Editor mouse stand-in. Scripted hook for tests |
| `Magic/SpellCanvas` | Mid-air drawing plane, ink, seal ring → runes → armed spell. `GuideRenderer` = traceable dotted guide in the air |
| `Magic/SpellCaster`, `SpellEffects` | Casting rules (push / incantation / both), mana, mastery, hidden combos, per-element effects |
| `Voice/` | `SpeechService` (Android SpeechRecognizer via JNI), `TtsService`, `VoiceRouter` (fuzzy commands, `{x}` slots, choices, yes/no, "I said …" corrections), `TextMatch` |
| `Combat/` | Enemies from the Ember Court (weaknesses, shields, boss phases), slow telegraphed embers, Aegis ward, rifts, player ward/mana |
| `Story/` | `StoryDirector` (prologue, guided tutorial, 3 chapters, finale, Rift Trials), quests, NPCs (Mirel, Pip), rune-shard hunt |
| `UI/` | Head-following world-space HUD (subtitles, hints, choices, mic state), the Grimoire book |
| `Spatial/MrSpatial` | MRUK floor / room bounds / spawn points, with a no-room fallback |
| `Core/` | `Game` composition root, spell data, procedural audio (`AudioSynth`), save system, `Fx` material/line/particle helpers |

## Spells

Seal ring first, then a rune inside it:

| Rune | Element | Spell | Word |
|---|---|---|---|
| upward triangle | Fire | Ember Lance | ignis |
| downward triangle | Water | Tide Spear | aqua |
| two waves | Wind | Gale Cutter | ventus |
| lightning zig-zag | Lightning | Thunder Verdict (needs the spoken word) | fulgur |
| square | Earth | Stone Spike | terra |
| six-armed snowflake | Ice | Frost Shard | glacies |
| diamond | Barrier | Aegis Ward | murus |
| plus sign | Heal | Healing Light | sana |

Two runes in one seal discover hidden spells (fire+wind, water+lightning, ice+barrier) that need both words.

## Voice

"Begin / continue", "open my grimoire", "close the grimoire", "next page", "go to fire page", "learn fire magic", "practice water",
"cast fireball" (starts the tracing guide, never skips drawing), "show my quests", "ask Mirel about the rift", "what can I say?",
"cancel", "erase the rune", "hide the guide", "remind me", "I am ready", "try again", "mute the voice".
Dialogue choices are answered by number or keyword. Low-confidence matches are confirmed aloud ("Did you say …?") and can be corrected ("I said …").

Speech recognition uses `android.speech.SpeechRecognizer`. Whether that works on a given Quest OS build, and whether it is on-device or
online, is decided by the OS recognition service (offline is requested with `EXTRA_PREFER_OFFLINE`, not guaranteed). If it is unavailable the HUD says so
and every essential action still has a gesture path (palm-up to begin, push to cast, dwell-on-card for choices).
Meta's Voice SDK (online, Wit.ai) is installed in the project and could be added as a second backend behind `SpeechService.Heard`.

## Tests

`Window > General > Test Runner > PlayMode`, or

```
Unity.exe -batchmode -projectPath <project> -runTests -testPlatform PlayMode -testResults results.xml
```

`RuneMagePlayModeTests` drives the real scene with scripted hands and injected phrases (drawing, recognition, casting, combat, grimoire, voice, quests, persistence).
`RuneMageStoryTests` plays the tutorial using only voice phrases and strokes. `RuneMageVisualCapture` writes screenshots to `<project>/Screenshots`.

## Art provenance

Zombie / ZombieMage: Inferno-Overdrive (`Inferno.fbx`, `InfernoMage.fbx`, Mixamo animations). Chandelure: GraveSouls (`chandelier.fbx`).
The original per-frame flame textures (582 MB) were baked into 5×5 flipbook atlases (`Art/Textures/*_5x5.png`, UV-mapped, played by `RuneMage/GlowFlipbook`).
Environment props (dead trees, stone plate, candle) come from Pumpkin SoulDew; the ink trail texture is Eric VFX Studio's `trail_CPdr_Rm_01`.
