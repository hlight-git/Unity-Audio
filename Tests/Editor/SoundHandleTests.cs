using NUnit.Framework;

namespace Hlight.Audio.Tests
{
    public class SoundHandleTests
    {
        [Test]
        public void Default_IsNone()
        {
            Assert.IsFalse(default(SoundHandle).IsSome);
            Assert.AreEqual(SoundHandle.None, default(SoundHandle));
        }

        [Test]
        public void HandleWithGeneration_IsSome()
        {
            Assert.IsTrue(new SoundHandle(0, 1).IsSome);
        }

        [Test]
        public void SameSlotDifferentGeneration_AreNotEqual()
        {
            Assert.AreNotEqual(new SoundHandle(3, 1), new SoundHandle(3, 2));
        }

        [Test]
        public void SameSlotSameGeneration_AreEqual()
        {
            Assert.AreEqual(new SoundHandle(3, 7), new SoundHandle(3, 7));
            Assert.IsTrue(new SoundHandle(3, 7) == new SoundHandle(3, 7));
        }

        [Test]
        public void NonZeroSlotWithZeroGeneration_IsNone()
        {
            Assert.IsFalse(new SoundHandle(5, 0).IsSome);
        }
    }
}
