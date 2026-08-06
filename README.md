# Audio

Casual-game audio: enum-keyed banks over Addressables, channel assets, per-cue polyphony
limiting. Type-safe without codegen — the game names its own sounds with its own enum, and
a wrong enum at a call site is a compile error, not a runtime surprise.

**Namespace:** `Hlight.Audio` (Runtime), `Hlight.Audio.Editor` (Editor tooling)
**Unity:** 6000.0
**Dependencies:** `com.unity.addressables`, `com.cysharp.unitask` (+ `UniTask.Addressables`)

## Quick start — the two files a game writes

```csharp
namespace MyGame.Audio
{
    public enum SfxId { Click, Coin, Explosion }   // your own enum — keep it contiguous from zero
}
```

```csharp
using UnityEngine;

namespace MyGame.Audio
{
    [CreateAssetMenu(menuName = "MyGame/Audio/Sfx Bank")]
    public sealed class SfxBank : AudioBank<SfxId> { }   // the whole binding: one line
}
```

That second file is not decorative — **the concrete subclass must be the primary type in a
file named after it.** Unity's MonoScript resolver ties a saved `.asset` back to a
ScriptableObject by matching the script's file name to its primary type; a `SfxBank` declared
as a second type inside someone else's file (e.g. nested in a test fixture) fails that match
silently. The `.asset` still exists on disk, `AssetDatabase.LoadAssetAtPath` still returns
something, but every field reads back empty and no error appears anywhere — this was caught by
a round-trip test (create asset → unload → reload → assert) during development, not by
inspection. Follow the `SfxBank.cs` shape above exactly.

`TKey` is bound at the **asset** level, not per-method — `SfxBank.Play` only accepts `SfxId`;
passing any other enum is `CS1503` at compile time. The type parameter also carries a real
runtime constraint: **`TKey` must be an int-backed enum with non-negative, densely-packed
values starting at zero.** The cue lookup table is a plain array indexed by
`UnsafeUtility.EnumToInt(key)` — a non-int-backed enum (`enum Foo : byte`) logs
`Debug.LogError` on first use, and a value like `SfxId.Boss = 100` allocates (and wastes) a
101-slot array. See [Deferred](#deferred) for the sparse-enum escape hatch if you need one.

## Clips are Addressable references, not direct fields

`AudioCueDefinition.clips` is `AssetReferenceT<AudioClip>[]`, never a direct `AudioClip`
field. The package only **names** the assets it needs (`CollectKeys()` walks every cue in a
bank and returns the distinct Addressables runtime keys); it never touches group, label, or
bundle configuration — **how those clips get packed into bundles is entirely the game's
Addressables setup.** Mark each clip Addressable in its importer (or via a build script) and
put it in whatever group your project's Addressables profile expects. A bank with clips split
across a remote group and a local group works exactly the same as one bank, one group — the
bank doesn't know or care.

## Lifecycle

```csharp
_runtime = new AudioRuntime(config);      // plain C#, not a singleton — you own the instance
AudioRuntime.Current = _runtime;          // banks resolve playback through this static locator

await bank.PrepareAsync(progress, ct);    // download (if needed) then load every clip

bank.Play(SfxId.Click);                   // synchronous — never blocks or awaits

bank.Release();                           // drop every Addressables handle this bank holds
```

`PrepareAsync` belongs in an `ABootstrapTask` (`Hlight.Foundation`) — its
`Execute(scope, progress, cancellationToken)` hands you exactly the `IProgress<float>` and
`CancellationToken` shapes `PrepareAsync` takes. Call `bank.Release()` when leaving the content
that bank belongs to (a level, an event), not at application quit — a bank left prepared for
the lifetime of the app just means its clips are never unloaded.

**`PrepareAsync` is idempotent, not naive.** Calling it while a load is already in flight hands
back the *same* in-progress task instead of starting a second one — two callers awaiting
`bank.PrepareAsync()` concurrently both observe `IsReady` when their await returns, and only
the *first* caller's `progress` and `ct` are honored; a second caller's are silently ignored,
so a joining caller cannot cancel a load the first caller is still relying on. If `Release()`
lands while a prepare is in flight, that in-flight `PrepareAsync` throws
`OperationCanceledException` — a caller awaiting "the bank is ready" must not treat that as
success. Releasing and immediately re-preparing in the same frame hands back the dying task;
wait a frame and retry.

## `Play` before `PrepareAsync` completes

`Play` is **silent** when the bank isn't ready — it returns `SoundHandle.None` with **no
console warning**. Loading is a phase your bootstrap owns, not something a call site awaits or
gets told off for skipping; `IsReady` is there for UI (`OnGUI` in the sample shows a loading
label) if you need to react to it. The same silent-`SoundHandle.None` behavior covers an
unmapped key, a cue whose only clip reference is missing from its Addressables group, and a
cue blocked by its own polyphony gate (`maxConcurrent` / `minInterval`) — `Play`'s return value
is always the only signal; nothing here logs.

## Project settings

- **`voices`** (`AudioRuntimeConfig.voices`, range 4–24) must stay at or below
  **Project Settings ▸ Audio ▸ Max Real Voices** (default 32) — Unity will not create sources
  beyond what it can play, but exceeding it wastes `GameObject`s that never get used. On
  **mobile, keep it nearer 12–16**: many Android devices expose only 15–32 hardware audio
  tracks system-wide, shared with the rest of the OS.
- **DSP Buffer Size** (Project Settings ▸ Audio) starts at **Best latency (256)**; move to
  **Good latency (512)** if a target device reports audio underruns/crackle. This is a
  device-behavior tradeoff the package has no visibility into — it's your call to make per
  target hardware.

## Import settings

| Content | Load Type | Format |
|---|---|---|
| Short SFX | Decompress on Load | Vorbis |
| Many mid-length SFX | Compressed in Memory | ADPCM |
| Music | Streaming | PCM |

Enable **Force to Mono** on the *importer* of any clip whose cue has `spatial = true` — a
stereo clip is twice the on-disk/in-memory size, and 3D playback (`AudioSource.spatialBlend`)
discards the stereo image anyway, so shipping stereo there is a pure cost with no audible
benefit.

## Setting up the mixer

`AudioChannel.exposedParam` is a plain string you type by hand, and nothing checks it until
runtime — get it wrong and `AudioRuntime` logs a `[Audio] Channel '<name>' looks for exposed
parameter '<param>' but the mixer has none` error the first time that channel's volume is
applied (once per channel, not once per call). Here is the actual click path, the first time
you set up a channel:

1. **Window ▸ Audio ▸ Audio Mixer**, then create a mixer asset (or open the sample's
   `Audio/GameAudio.mixer` to see a finished example).
2. Add a group per channel — e.g. a `Master` group with an `SFX` child group underneath it.
3. Select a group. In the Inspector, right-click its **Volume** slider and choose
   *Expose 'Volume (of SFX)' to script*.
4. Open the **Exposed Parameters** dropdown at the top-right of the Audio Mixer window and
   rename the entry Unity just added from its default name to something meaningful, e.g.
   `SFXVolume`. **This renamed string is the value that goes into `AudioChannel.exposedParam`.**
   This step is the one people skip — Unity's default exposed name looks plausible enough that
   it's easy to assume it's already usable and move on.
5. Create the `AudioChannel` asset (`Hlight/Audio/Channel`), set `group` to that mixer group,
   and `exposedParam` to that exact renamed string. Once `group` is assigned, the channel
   inspector reads that group's mixer and offers its exposed parameters as a dropdown, so the
   renamed string never has to be typed a second time. You still do steps 3–4 by hand in the
   Audio Mixer window — Unity has no scripting API for creating an exposed parameter, only for
   listing the ones that already exist.

**Why this field exists at all:** Unity gives you no way to set a mixer group's volume from
code — there is no `group.volume` setter. An exposed parameter is the only writable handle a
script has, and `AudioMixer.SetFloat` takes it by name, not by group reference. That's also why
`group` and `exposedParam` are a pair rather than either one alone: `group` decides where a
cue's sound is *routed*, `exposedParam` is the separate, named handle through which that
routed group's *volume* is changed.

The value you write is in **decibels**, not the 0–1 range a volume slider shows — that's why
`AudioRuntime` never writes a slider value straight to the mixer; it goes through
`AudioVolume.ToDecibels` first.

## Core types

| Type | What it is |
|---|---|
| `AudioBank` | Non-generic base — `KeyType`, `IsReady`, `MissingKeyNames()` / `DuplicateKeyNames()`, `Invalidate()`, `GetDownloadSizeAsync`, `PrepareAsync`, `Release`. What tooling and bootstrap code use when they don't know the concrete key enum. |
| `AudioBank<TKey>` | What you subclass. `Play` / `PlayAt` / `Stop` / `StopAll` / `Resolve`. `exclusive = true` crossfades: a new `Play` stops the bank's previous sound instead of layering — use it for music/voice, not SFX. An exclusive bank also bypasses its cues' per-cue polyphony gate (`maxConcurrent`/`minInterval`) entirely — it already permits only one sound at a time, so the gate would otherwise refuse the very sound meant to replace the old one. Caveat: bypassing the gate also bypasses `minInterval`, so calling `Play` every frame on an exclusive bank whose cue has a long `fadeOut` accumulates fading voices until no slot is free. **`StopAll` stops by cue, not by bank instance**: if two banks share a cue (the supported pattern), `musicBank.StopAll()` also stops another bank's currently-playing instances of that same cue. |
| `AudioChannel` | One volume-controllable group: `AudioMixerGroup group`, `string exposedParam` (must match the mixer's exposed float parameter name exactly — see [Setting up the mixer](#setting-up-the-mixer) for how that name gets created), `defaultVolume`. One asset per channel. |
| `AudioCueDefinition` | One sound: `AssetReferenceT<AudioClip>[] clips` (more than one picks at random on each play), `channel`, `volume`/`pitch`/`pitchVariation`, `priority` (0 = never virtualized, 255 = first virtualized — leave most cues at 128), `loop`, `fadeIn`/`fadeOut`, `maxConcurrent`/`minInterval` (polyphony gate — not applied on an exclusive bank, see `AudioBank<TKey>` below), `spatial` + `minDistance`/`maxDistance`. |
| `AudioRuntime` | Owns the fixed `SoundSource` pool, per-channel volume/mute (batched `PlayerPrefs` writes, flushed on pause/focus-loss/dispose), and mixer parameter application. Plain C# `IDisposable` — construct one at bootstrap, assign it to the static `AudioRuntime.Current`, `Dispose()` it when torn down. Call `SetPaused` from `OnApplicationPause`/`OnApplicationFocus` so a phone call or app-switch silences the game. |
| `SoundHandle` | Readonly struct (`Slot` + `Generation`). Goes stale automatically when its pool slot is reused, so an old handle can never accidentally control a different, newer sound. `SoundHandle.None.IsSome == false`. |

## Editor tooling

- **Bank inspector** — reports enum values with no cue (`MissingKeyNames`) and keys that
  appear more than once (`DuplicateKeyNames`, last one wins), plus a **Sync with enum** button
  that rewrites `entries` to exactly match the enum's current values (in declaration order),
  keeping any cues already assigned to a value that still exists.
- **Cue inspector** — a **Preview** button plays the cue's clip (random pick, with pitch
  variation applied) without entering Play mode, and **Stop preview** which also fires
  automatically before a domain reload so a preview `AudioSource` never survives one orphaned.
- **Channel inspector** — `exposedParam` is a dropdown of the assigned group's mixer's exposed
  parameter names instead of free text, so it can't be mistyped; it falls back to a plain text
  field (with setup instructions) until a group is assigned or the mixer has no exposed
  parameters yet, and warns rather than silently overwriting the field if the saved value no
  longer matches anything on the mixer. Also warns if `group` itself is unassigned, since that
  routes the channel's volume/mute past the mixer entirely.

**Odin Inspector support is automatic.** When a project has Odin installed (`ODIN_INSPECTOR`
defined), both inspectors draw their serialized fields through Odin instead of Unity's default
`DrawDefaultInspector`, so Odin attributes on your own `AudioBank`/`AudioCueDefinition` subclass
render correctly — nothing to configure. Without Odin, both inspectors behave exactly as
described above. Either way the package has **no hard dependency on Odin**: the Odin-aware
editors live in their own assembly (`Editor/Odin/`), gated by an asmdef `defineConstraints` on
`ODIN_INSPECTOR`, so it is simply excluded from compilation when Odin is absent.

## Sample

`Samples~/01-basic` — import via **Package Manager ▸ Audio ▸ Samples ▸ 01 - Basic**. Covers SFX
(enum-keyed bank, polyphony gate) and background music (an `exclusive` bank whose second `Play`
crossfades out the first instead of layering, via `loop`/`fadeIn`/`fadeOut` on the cue). See
that folder's own `README.md` for what it demonstrates and the manual mixer step it needs (Unity
has no public scripting API for authoring `AudioMixer` groups/exposed parameters, so one
one-time setup step is manual).

## Deferred

Not built now, with the trigger that would justify each — see the implementation plan
(`docs/superpowers/plans/2026-08-04-com-hlight-audio.md`) for the full list. The one worth
knowing up front: **sparse enum support.** The lookup table is sized by the largest enum value,
so a game needing `SfxId.Boss = 100` should swap the array in `AudioBank<TKey>.BuildTable` for
a `Dictionary<TKey, AudioCueDefinition>` — nothing outside `Resolve` would need to change.

## File layout

```
Packages/com.hlight.audio/
├── package.json
├── README.md (this file)
├── Runtime/
│   └── Core/            AudioChannel, AudioCueDefinition, SoundHandle, PolyphonyGate,
│                         SoundSource, AudioRuntime, AudioBank(+Loading partial)
├── Editor/               AudioBankEditor (Sync + validation), AudioCueDefinitionEditor (Preview),
│                         AudioChannelEditor (exposed-param dropdown)
│   └── Odin/             Odin-drawn equivalents, compiled only when ODIN_INSPECTOR is defined
├── Tests/Editor/         EditMode tests (run via Window ▸ General ▸ Test Runner ▸ EditMode)
└── Samples~/01-basic/    SfxId · SfxBank · MusicId · MusicBank · BasicAudioSample · channels · cues · clips
```

## License / status

Internal Hlight package.
