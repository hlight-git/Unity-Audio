using NUnit.Framework;

namespace Hlight.Audio.Tests
{
    public class PolyphonyGateTests
    {
        private const float Never = float.NegativeInfinity;

        [Test]
        public void FirstPlay_IsAllowed()
        {
            Assert.IsTrue(PolyphonyGate.Allows(0, Never, 0f, 2, 0.03f));
        }

        [Test]
        public void AtConcurrencyLimit_IsRejected()
        {
            Assert.IsFalse(PolyphonyGate.Allows(2, Never, 10f, 2, 0f));
        }

        [Test]
        public void BelowConcurrencyLimit_IsAllowed()
        {
            Assert.IsTrue(PolyphonyGate.Allows(1, Never, 10f, 2, 0f));
        }

        [Test]
        public void ZeroMaxConcurrent_MeansUnlimited()
        {
            Assert.IsTrue(PolyphonyGate.Allows(99, Never, 10f, 0, 0f));
        }

        [Test]
        public void WithinMinInterval_IsRejected()
        {
            Assert.IsFalse(PolyphonyGate.Allows(0, 10f, 10.02f, 0, 0.03f));
        }

        [Test]
        public void AfterMinInterval_IsAllowed()
        {
            Assert.IsTrue(PolyphonyGate.Allows(0, 10f, 10.04f, 0, 0.03f));
        }

        [Test]
        public void ZeroMinInterval_NeverThrottlesByTime()
        {
            Assert.IsTrue(PolyphonyGate.Allows(0, 10f, 10f, 0, 0f));
        }

        // Both limits live and both breached. AtConcurrencyLimit_IsRejected and
        // WithinMinInterval_IsRejected are what actually pin the two rejections as
        // independent — each breaches one limit while the other is disabled, so an
        // implementation requiring both to be breached fails them. This case covers the
        // combination itself, which those two do not reach.
        [Test]
        public void BothLimitsViolated_IsRejected()
        {
            Assert.IsFalse(PolyphonyGate.Allows(2, 10f, 10.01f, 2, 0.03f));
        }

        // 0.125 rather than 0.03: it is a power of two and therefore exact in binary
        // float, so `now - lastStartTime` equals minInterval precisely and this really
        // does sit on the boundary that `<` versus `<=` decides.
        [Test]
        public void ExactlyAtMinInterval_IsAllowed()
        {
            Assert.IsTrue(PolyphonyGate.Allows(0, 10f, 10.125f, 0, 0.125f));
        }
    }
}
