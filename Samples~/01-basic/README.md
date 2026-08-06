# 01 - Basic

The full lifecycle in one prefab: construct the runtime, prepare a bank, play by enum, control
volume, mute, and demonstrate the per-cue polyphony gate.

## Layout

```
01-basic/
├── README.md
├── BasicAudioSample.prefab
├── Scripts/     Hlight.Audio.Samples.Basic.asmdef · BasicAudioSample.cs · SfxId.cs · SfxBank.cs · MusicId.cs · MusicBank.cs
├── Clips/       Click.wav · Coin.wav · Explosion.wav · MenuMusic.wav · BattleMusic.wav
└── Audio/       GameAudio.mixer
    ├── Channels/  Master.asset · Sfx.asset · Music.asset
    ├── Cues/      ClickCue.asset · CoinCue.asset · ExplosionCue.asset · MenuMusicCue.asset · BattleMusicCue.asset
    └── Banks/     SfxBank.asset · MusicBank.asset
```

This grouping is the template worth copying into your own project: scripts together
regardless of what they define, clips together regardless of SFX vs. music, and the
authored audio data (the mixer plus every asset that only exists to configure it) under one
`Audio/` folder, split by kind — routing (`Channels/`), individual sounds (`Cues/`), and
enum-keyed lookup tables (`Banks/`).

## Contents

| File | What it is |
|---|---|
| `Scripts/SfxId.cs` | The enum a game declares — `Click`, `Coin`, `Explosion` (contiguous from zero). |
| `Scripts/SfxBank.cs` | `public sealed class SfxBank : AudioBank<SfxId> { }` — the one-line binding. Kept as the primary type in a file named after it, exactly as the package README requires. |
| `Scripts/MusicId.cs` | The enum for the music demo — `Menu`, `Battle`. |
| `Scripts/MusicBank.cs` | `public sealed class MusicBank : AudioBank<MusicId> { }` — same one-line-binding rule as `SfxBank.cs`. `exclusive = true` on `MusicBank.asset`: one track plays at a time. |
| `Scripts/BasicAudioSample.cs` | `MonoBehaviour` driving the lifecycle — see below. |
| `Audio/GameAudio.mixer` | Three groups: `Master`, `Master/SFX`, `Master/Music`, each exposing a float volume parameter (`MasterVolume`, `SFXVolume`, `MusicVolume`). Named `GameAudio` rather than `Sfx` because it carries all three groups, not just SFX. |
| `Audio/Channels/Master.asset` / `Sfx.asset` / `Music.asset` | `AudioChannel` assets pointing at those groups/parameters. **Every group routes into `Master`**, so the `Master` channel's slider scales the whole game's audio at once, while `Sfx` and `Music` stay independent of each other. Unity always creates the `Master` group on a new mixer and it cannot be deleted — that is why this sample gives it its own `AudioChannel` alongside the two you'd expect. |
| `Clips/Click.wav` / `Coin.wav` / `Explosion.wav` | ~100 ms generated tones (1200 Hz / 880 Hz / 140 Hz sine, faded in/out to avoid clicks), marked Addressable in a `Sample-Audio` group. |
| `Clips/MenuMusic.wav` / `BattleMusic.wav` | ~2 s generated tone loops (220 Hz / 440 Hz sine — one octave apart, so a crossfade between them is unmistakable), 86 KB each. Each clip's length is an exact whole number of cycles (440 / 880) so the loop point has no click. |
| `Audio/Cues/ClickCue.asset` / `CoinCue.asset` / `ExplosionCue.asset` | `AudioCueDefinition` assets. `CoinCue` has `maxConcurrent = 2` — the point of the ten-coin loop below. |
| `Audio/Cues/MenuMusicCue.asset` / `BattleMusicCue.asset` | `AudioCueDefinition` assets with `loop = true`, `fadeIn = fadeOut = 0.5`, routed to the `Music` channel. |
| `Audio/Banks/SfxBank.asset` | The bank asset, all three `SfxId` values mapped. |
| `Audio/Banks/MusicBank.asset` | The music bank, `exclusive = true`, both `MusicId` values mapped. |
| `BasicAudioSample.prefab` | `config.voices = 12`, `config.mixer = GameAudio.mixer`, `config.channels = [Master, Sfx, Music]`. |

## One manual step before pressing Play

Unity has **no public scripting API for authoring `AudioMixer` groups or exposed
parameters** — every group, snapshot, and exposed float on a `.mixer` asset is set up through
the Mixer window's UI (or Unity's own internal, non-public editor code). `Audio/GameAudio.mixer`
in this sample was authored by creating a real mixer asset in the Editor and adding its groups
and exposed parameters directly in the serialized asset data — it opens and resolves correctly
(`FindMatchingGroups`, `GetFloat` on all three exposed names all succeed), but it has **never
been opened in the Mixer window**. Before you rely on it beyond this sample, open
`Audio/GameAudio.mixer` once and confirm the `Master` → `SFX` / `Master` → `Music` hierarchy and
all three exposed parameters (`MasterVolume`, `SFXVolume`, `MusicVolume`) look the way you'd
expect from the window's own UI. See the package README's
[Setting up the mixer](../../README.md#setting-up-the-mixer) section for the click-by-click
version of this workflow for your own project.

## Setup

1. **Package Manager ▸ Audio ▸ Samples ▸ Import** on "01 - Basic".
2. Mark `Click.wav`, `Coin.wav`, `Explosion.wav`, `MenuMusic.wav`, `BattleMusic.wav`
   Addressable — package samples never carry Addressables assignments, so this step is always
   required, not a fallback. Addressable status lives in `Assets/AddressableAssetsData/`, which
   no package ships; skipping this step means `PrepareAsync` is never reached — the sample
   throws `InvalidKeyException` out of its first `GetDownloadSizeAsync` call, and the loading
   label never reaches 100%. The two music clips need this exactly as much as the three SFX
   clips do — nothing about looping content is exempt.
3. **Window ▸ Asset Management ▸ Addressables ▸ Groups ▸ Build ▸ New Build ▸ Default Build
   Script.**
4. Drag `BasicAudioSample.prefab` into any scene and press **Play**.

## Expected on Play (a human must confirm this — an agent cannot hear audio)

- A "Loading audio… N%" label appears and reaches 100%.
- One `Click` plays once, immediately after loading finishes.
- Ten `Coin` plays fire in the same frame; **only two are audible** — `CoinCue.maxConcurrent = 2`
  drops the other eight silently (no error, no log — that's the design; see the package
  README's "no-op" section).
- The on-screen slider changes SFX volume live; **Mute** silences it without losing the
  slider's value; stopping and restarting Play mode preserves both (`PlayerPrefs`).
- Clicking **Explosion** plays the low-pitched cue on demand.
- Console error list stays empty (aside from any pre-existing, unrelated project noise).

## What to look for (music)

- Pressing **Play menu music** then **Play battle music** (or vice versa) should **crossfade**:
  the first track fades out over 0.5 s while the second fades in, never both at full volume at
  once. This is `MusicBank.exclusive = true` doing its job — a second `Play` replaces the first
  instead of layering on top of it, and both clips are one octave apart in pitch so the swap is
  unmistakable even without careful listening.
- The **Music volume** slider and **Mute music** button must be visibly independent of the SFX
  ones above them: muting SFX must not affect the music track currently playing, and vice
  versa — they are two separate `AudioChannel` assets routed to two separate mixer groups.

## What this sample exists to make reachable

The package's Addressables lifecycle is only ever exercised by whoever runs this sample or the
project's own EditMode tests — nothing else touches real Addressables load/download. Reaching
each of these is on you; none of them were verified by whoever authored this sample file set
(an agent, without the ability to enter Play mode or hear sound):

1. **Remote group, size > 0** — move `Sample-Audio` to a remote-hosted Addressables group,
   rebuild, and confirm the loading percentage moves rather than jumping straight to 100%.
2. **All-local build (this sample's default)** — confirm `GetDownloadSizeAsync` returns 0 and
   loading still completes.
3. **Full cycle** — call `sfx.Release()` (e.g. bind a key to it, or destroy/re-instantiate the
   prefab), confirm `Play` returns nothing audible afterward, then prepare again and confirm
   playback resumes. Watch the Addressables Event Viewer or profiler to confirm the bundle
   actually unloads on `Release()`.
4. **Cancel mid-load, then `Release()`** — cancel the bootstrap's `CancellationToken` while
   `PrepareAsync` is still downloading/loading, then call `Release()`, and confirm no bundle
   stays resident.
5. **Two overlapping `PrepareAsync` calls** — call it twice back-to-back before the first
   completes and confirm both awaits observe `IsReady` once either resolves.
6. **A cue with an unassigned clip slot** — add a second, empty `AssetReferenceT<AudioClip>`
   slot to a cue's `clips` array, play it ~30 times, and confirm it never silently drops more
   often than the empty slot should account for.
7. **A clip removed from its Addressables group** — remove one clip's entry (or exclude its
   group from a build) and confirm the result is one silent cue, not a broken bank (the other
   two cues must keep playing normally).
