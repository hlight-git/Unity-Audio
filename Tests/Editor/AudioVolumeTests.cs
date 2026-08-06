using NUnit.Framework;

namespace Hlight.Audio.Tests
{
    public class AudioVolumeTests
    {
        [Test]
        public void FullVolume_IsZeroDb()
        {
            Assert.AreEqual(0f, AudioVolume.ToDecibels(1f), 0.001f);
        }

        [Test]
        public void HalfVolume_IsAboutMinusSixDb()
        {
            Assert.AreEqual(-6.02f, AudioVolume.ToDecibels(0.5f), 0.01f);
        }

        [Test]
        public void Zero_IsSilence()
        {
            Assert.AreEqual(AudioVolume.SilenceDb, AudioVolume.ToDecibels(0f), 0.001f);
        }

        [Test]
        public void NegativeInput_ClampsToSilence()
        {
            Assert.AreEqual(AudioVolume.SilenceDb, AudioVolume.ToDecibels(-1f), 0.001f);
        }

        [Test]
        public void AboveOne_ClampsToZeroDb()
        {
            Assert.AreEqual(0f, AudioVolume.ToDecibels(5f), 0.001f);
        }
    }
}
