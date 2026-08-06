using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace Hlight.Audio.Tests
{
    /// <summary>
    /// Covers AudioRuntime.Play directly — AudioBankTests only ever exercises it through a
    /// bank, and nothing anywhere constructs an AudioRuntime, so the whole playback half was
    /// previously untested.
    /// </summary>
    public class AudioRuntimeTests
    {
        // Every ScriptableObject/AudioClip a test creates via CreateInstance/AudioClip.Create,
        // so TearDown can destroy them. Without this, every run leaks assets — and, worse,
        // `new AudioRuntime(...)` creates an [Audio] GameObject that (outside Dispose) would
        // land in whatever scene the developer has open.
        private readonly List<Object> _created = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var obj in _created)
                if (obj != null) Object.DestroyImmediate(obj);
            _created.Clear();
            AudioRuntime.Current = null;
        }

        private T Track<T>(T obj) where T : Object
        {
            _created.Add(obj);
            return obj;
        }

        private AudioCueDefinition Cue()
        {
            var cue = Track(ScriptableObject.CreateInstance<AudioCueDefinition>());
            cue.channel = Track(ScriptableObject.CreateInstance<AudioChannel>());
            return cue;
        }

        [Test]
        public void Play_HonoursPerCuePolyphony()
        {
            // PolyphonyGateTests proves the predicate; nothing proved AudioRuntime calls it.
            var runtime = new AudioRuntime(new AudioRuntimeConfig { voices = 8 });
            try
            {
                var cue = Cue();
                cue.maxConcurrent = 2;
                cue.minInterval = 0f;
                var clip = Track(AudioClip.Create("t", 128, 1, 8000, false));

                Assert.IsTrue(runtime.Play(cue, clip).IsSome);
                Assert.IsTrue(runtime.Play(cue, clip).IsSome);
                Assert.IsFalse(runtime.Play(cue, clip).IsSome, "third instance must be refused");
            }
            finally { runtime.Dispose(); }
        }

        [Test]
        public void Play_DoesNotMutateTheCueAsset()
        {
            // Two banks may share a cue; the old package this replaces wrote to them at play time.
            var runtime = new AudioRuntime(new AudioRuntimeConfig { voices = 4 });
            try
            {
                var cue = Cue();
                float volume = cue.volume, pitch = cue.pitch;
                int priority = cue.priority, maxConcurrent = cue.maxConcurrent;
                bool loop = cue.loop;

                var handle = runtime.Play(cue, Track(AudioClip.Create("t", 128, 1, 8000, false)));

                Assert.IsTrue(handle.IsSome, "Play must actually start a sound, or this test proves nothing");
                Assert.AreEqual(volume, cue.volume);
                Assert.AreEqual(pitch, cue.pitch);
                Assert.AreEqual(priority, cue.priority);
                Assert.AreEqual(maxConcurrent, cue.maxConcurrent);
                Assert.AreEqual(loop, cue.loop);
            }
            finally { runtime.Dispose(); }
        }

        [Test]
        public void StaleHandle_CannotControlARecycledSlot()
        {
            // SoundHandleTests only proves struct equality; this proves the generation field
            // actually does its job once a pool slot is reused for a different sound.
            var runtime = new AudioRuntime(new AudioRuntimeConfig { voices = 1 });
            try
            {
                var cue = Cue();
                // No real time elapses between these two synchronous Play calls, so the cue's
                // default minInterval (0.03s) would otherwise refuse the second one outright —
                // that gate is not what this test is about.
                cue.minInterval = 0f;
                var clip = Track(AudioClip.Create("t", 128, 1, 8000, false));

                var first = runtime.Play(cue, clip);
                Assert.IsTrue(first.IsSome);

                runtime.Stop(first, 0f); // immediate: frees the only slot synchronously
                var second = runtime.Play(cue, clip); // same (only) slot, new generation

                Assert.AreNotEqual(first, second, "a recycled slot must issue a new generation");
                Assert.IsTrue(runtime.IsPlaying(second));
                Assert.IsFalse(runtime.IsPlaying(first), "a stale handle must not report the new sound as playing");

                runtime.Stop(first);
                Assert.IsTrue(runtime.IsPlaying(second), "stopping through a stale handle must not stop the recycled slot's sound");
            }
            finally { runtime.Dispose(); }
        }

        [Test]
        public void SetMuted_PreservesTheChosenVolume()
        {
            // The exact defect the older package in this repo had — mute is stored separately
            // from volume for exactly this reason.
            var runtime = new AudioRuntime(new AudioRuntimeConfig());
            try
            {
                var channel = Track(ScriptableObject.CreateInstance<AudioChannel>());
                channel.name = "MuteTestChannel";

                runtime.SetVolume(channel, 0.42f);
                runtime.SetMuted(channel, true);
                Assert.AreEqual(0.42f, runtime.GetVolume(channel), 0.0001f, "muting must not overwrite the chosen volume");

                runtime.SetMuted(channel, false);
                Assert.AreEqual(0.42f, runtime.GetVolume(channel), 0.0001f, "unmuting must restore what the player chose, not default to 1");
            }
            finally { runtime.Dispose(); }
        }

        [Test]
        public void ExclusiveBank_ReplacesItsOwnSoundInsteadOfSilencingItself()
        {
            // This is the case that was permanently broken until the polyphony bypass landed:
            // an exclusive bank's replacement Play, issued immediately after the first (no time
            // elapses between the two calls in this test), used to be refused by the cue's own
            // minInterval, leaving the bank silent instead of crossfading.
            //
            // AudioCueDefinition.clips is an Addressables reference, so a bank only ever resolves
            // a playable clip after a real Addressables load succeeds — PrepareAsync with zero
            // keys (as used elsewhere in this suite) leaves every slot unresolvable, which would
            // make this test vacuously pass by never reaching the exclusive branch at all.
            // SetClipsForTests bypasses Addressables to inject an already-"loaded" clip so the
            // exclusive-replacement code path is actually exercised.
            AudioRuntime.Current = new AudioRuntime(new AudioRuntimeConfig { voices = 4 });
            try
            {
                var cue = Cue();
                var bank = Track(ScriptableObject.CreateInstance<TestBank>());
                bank.SetEntriesForTests(new[] { new AudioBank<TestSfx>.Entry { key = TestSfx.Click, cue = cue } });
                bank.SetExclusiveForTests(true);
                bank.SetClipsForTests(new[] { new[] { Track(AudioClip.Create("t", 128, 1, 8000, false)) } });

                var first = bank.Play(TestSfx.Click);
                var second = bank.Play(TestSfx.Click);

                Assert.IsTrue(first.IsSome, "first play must return a valid handle");
                Assert.IsTrue(second.IsSome,
                    "an exclusive bank's replacement play must return a valid handle instead of being silenced by its own polyphony gate");
                Assert.IsFalse(AudioRuntime.Current.IsPlaying(first),
                    "the previous sound must be stopped once the exclusive bank replaces it, not left playing alongside the new one");
            }
            finally { AudioRuntime.Current.Dispose(); }
        }
    }
}
