using System.Collections.Generic;
using System.Runtime.CompilerServices;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace Hlight.Audio.Tests
{
    public class AudioBankTests
    {
        [TearDown]
        public void TearDown() => AudioRuntime.Current = null;

        private static AudioCueDefinition Cue() => ScriptableObject.CreateInstance<AudioCueDefinition>();

        private static TestBank BankWith(params (TestSfx key, AudioCueDefinition cue)[] pairs)
        {
            var bank = ScriptableObject.CreateInstance<TestBank>();
            var entries = new AudioBank<TestSfx>.Entry[pairs.Length];
            for (int i = 0; i < pairs.Length; i++)
                entries[i] = new AudioBank<TestSfx>.Entry { key = pairs[i].key, cue = pairs[i].cue };
            bank.SetEntriesForTests(entries);
            return bank;
        }

        [Test]
        public void Resolve_ReturnsCueForKey()
        {
            var click = Cue();
            var bank = BankWith((TestSfx.Click, click), (TestSfx.Coin, Cue()), (TestSfx.Boom, Cue()));
            Assert.AreSame(click, bank.Resolve(TestSfx.Click));
        }

        [Test]
        public void Resolve_ReturnsNullForUnmappedKey()
        {
            var bank = BankWith((TestSfx.Click, Cue()));
            Assert.IsNull(bank.Resolve(TestSfx.Boom));
        }

        [Test]
        public void MissingKeys_ListsEveryUnmappedEnumValue()
        {
            var bank = BankWith((TestSfx.Click, Cue()));
            CollectionAssert.AreEquivalent(new[] { TestSfx.Coin, TestSfx.Boom }, bank.MissingKeys());
        }

        [Test]
        public void MissingKeys_IsEmptyWhenComplete()
        {
            var bank = BankWith((TestSfx.Click, Cue()), (TestSfx.Coin, Cue()), (TestSfx.Boom, Cue()));
            CollectionAssert.IsEmpty(bank.MissingKeys());
        }

        [Test]
        public void MissingKeys_TreatsNullCueAsMissing()
        {
            var bank = BankWith((TestSfx.Click, null), (TestSfx.Coin, Cue()), (TestSfx.Boom, Cue()));
            CollectionAssert.AreEquivalent(new[] { TestSfx.Click }, bank.MissingKeys());
        }

        [Test]
        public void DuplicateKeys_ReportsRepeatedEntries()
        {
            var bank = BankWith((TestSfx.Click, Cue()), (TestSfx.Click, Cue()));
            CollectionAssert.AreEquivalent(new[] { TestSfx.Click }, bank.DuplicateKeys());
        }

        [Test]
        public void NewBank_IsNotReady()
        {
            Assert.IsFalse(BankWith((TestSfx.Click, Cue())).IsReady);
        }

        [Test]
        public void Play_BeforePrepare_ReturnsNone()
        {
            AudioRuntime.Current = null;
            var bank = BankWith((TestSfx.Click, Cue()));
            Assert.IsFalse(bank.Play(TestSfx.Click).IsSome);
        }

        [Test]
        public void Resolve_IgnoresEntryOrder()
        {
            var boom = Cue();
            var bank = BankWith((TestSfx.Boom, boom), (TestSfx.Click, Cue()));
            Assert.AreSame(boom, bank.Resolve(TestSfx.Boom));
        }

        [Test]
        public void DuplicateKeys_LastNonNullEntryWins()
        {
            var second = Cue();
            var bank = BankWith((TestSfx.Click, Cue()), (TestSfx.Click, second));
            Assert.AreSame(second, bank.Resolve(TestSfx.Click));

            var kept = Cue();
            var withNull = BankWith((TestSfx.Click, kept), (TestSfx.Click, null));
            Assert.AreSame(kept, withNull.Resolve(TestSfx.Click), "a null cue must not shadow a real one");
        }

        [Test]
        public void NoMethodTakesItsOwnTypeParameter()
        {
            // The whole design: TKey is bound on the asset, so a foreign enum cannot compile.
            // A method-level type parameter would silently reopen that hole — whether declared
            // directly on AudioBank<TKey>, or smuggled in as an extension method on the
            // non-generic AudioBank base. That second shape is exactly what an earlier package
            // in this repo did: putting the generic on the extension method instead of the
            // type, which accepts any enum and only fails at runtime.
            //
            // Public reflection only: BindingFlags.NonPublic is rejected by the tooling these
            // tests run through. GetMethods()/GetTypes() with default flags only sees public
            // members, so a non-public generic method (on either type) or a non-public
            // extension method is outside what this test can guarantee.
            var generic = System.Array.FindAll(
                typeof(AudioBank<TestSfx>).GetMethods(), m => m.IsGenericMethodDefinition);
            Assert.IsEmpty(generic, "AudioBank<TKey> must expose no public generic methods");

            var badExtensions = new List<string>();
            foreach (var type in typeof(AudioBank).Assembly.GetTypes())
            {
                if (!type.IsClass || !type.IsAbstract || !type.IsSealed) continue; // static class shape in IL
                foreach (var method in type.GetMethods())
                {
                    if (!method.IsDefined(typeof(ExtensionAttribute), false)) continue;
                    var parameters = method.GetParameters();
                    if (parameters.Length == 0) continue;

                    var first = parameters[0].ParameterType;
                    bool extendsBank = first == typeof(AudioBank) ||
                        (first.IsGenericType && first.GetGenericTypeDefinition() == typeof(AudioBank<>));
                    if (extendsBank) badExtensions.Add($"{type.FullName}.{method.Name}");
                }
            }
            CollectionAssert.IsEmpty(badExtensions,
                "an extension method on AudioBank/AudioBank<> reopens the hole TKey binding exists to close");
        }

        [Test]
        public void NonGenericBase_ExposesKeyType()
        {
            AudioBank asBase = BankWith((TestSfx.Click, Cue()));
            Assert.AreEqual(typeof(TestSfx), asBase.KeyType);
            Assert.IsFalse(asBase.IsReady);
        }

        [Test]
        public void Entries_SurviveAssetRoundTrip()
        {
            const string dir = "Assets/__audio_bank_roundtrip__";
            const string cuePath = dir + "/Cue.asset";
            const string bankPath = dir + "/Bank.asset";
            try
            {
                AssetDatabase.CreateFolder("Assets", "__audio_bank_roundtrip__");
                var cue = Cue();
                AssetDatabase.CreateAsset(cue, cuePath);

                var bank = ScriptableObject.CreateInstance<TestBank>();
                bank.SetEntriesForTests(new[] { new AudioBank<TestSfx>.Entry { key = TestSfx.Coin, cue = cue } });
                AssetDatabase.CreateAsset(bank, bankPath);
                AssetDatabase.SaveAssets();

                Resources.UnloadAsset(bank);
                var reloaded = AssetDatabase.LoadAssetAtPath<TestBank>(bankPath);
                Assert.IsNotNull(reloaded, "bank asset did not reload");
                Assert.IsNotNull(reloaded.Resolve(TestSfx.Coin),
                    "Entry.key did not survive serialization — every authored bank would be silently empty");
                Assert.IsNull(reloaded.Resolve(TestSfx.Click));
            }
            finally
            {
                AssetDatabase.DeleteAsset(dir);
            }
        }

        [Test]
        public void CollectKeys_IsEmptyWhenNoClipsAssigned()
        {
            var bank = BankWith((TestSfx.Click, Cue()));
            CollectionAssert.IsEmpty(bank.CollectKeys());
        }

        [Test]
        public void CollectKeys_SkipsNullCues()
        {
            var bank = BankWith((TestSfx.Click, null), (TestSfx.Coin, Cue()));
            Assert.DoesNotThrow(() => bank.CollectKeys());
        }

        [Test]
        public void Release_LeavesBankNotReady()
        {
            var bank = BankWith((TestSfx.Click, Cue()));
            bank.Release();
            Assert.IsFalse(bank.IsReady);
        }

        [Test]
        public void Release_IsSafeToCallTwice()
        {
            var bank = BankWith((TestSfx.Click, Cue()));
            bank.Release();
            Assert.DoesNotThrow(() => bank.Release());
        }

        [Test]
        public void CollectKeys_DedupsTheSameReferenceTwice()
        {
            // The bank being its own download unit is the whole premise of CollectKeys.
            const string guid = "0123456789abcdef0123456789abcdef";
            var a = Cue(); a.clips = new[] { new AssetReferenceT<AudioClip>(guid) };
            var b = Cue(); b.clips = new[] { new AssetReferenceT<AudioClip>(guid),
                                             new AssetReferenceT<AudioClip>("not-a-guid") };
            var bank = BankWith((TestSfx.Click, a), (TestSfx.Coin, b));

            Assert.AreEqual(1, bank.CollectKeys().Count);
        }

        [Test]
        public void PrepareAsync_WithNoClips_BecomesReadyThenReleases()
        {
            // AgentTestRunner has no [UnityTest] support: it invokes [Test] methods and does
            // not await a returned Task, so an `async Task` test here would have its
            // post-await assertions silently swallowed into an unobserved task instead of
            // failing the test. With zero clips PrepareAsync completes with no real
            // suspension, so blocking on it with GetAwaiter().GetResult() is safe and keeps
            // this a plain synchronous [Test].
            var bank = BankWith((TestSfx.Click, Cue()));
            bank.PrepareAsync().GetAwaiter().GetResult();
            Assert.IsTrue(bank.IsReady);

            bank.Release();
            Assert.IsFalse(bank.IsReady);
        }
    }
}
