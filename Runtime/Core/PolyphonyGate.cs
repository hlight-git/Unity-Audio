namespace Hlight.Audio
{
    /// <summary>
    /// Per-cue instance limiting — what middleware calls event polyphony. Unity's own
    /// virtualization caps total voices but happily plays twenty copies of one sound,
    /// which sums into clipping and phasing. Over the limit the new instance is simply
    /// dropped; for pickups and UI clicks that is correct and needs no stealing policy.
    /// </summary>
    public static class PolyphonyGate
    {
        /// <param name="activeCount">Instances of this cue currently playing.</param>
        /// <param name="lastStartTime">When this cue last started. Use float.NegativeInfinity for never.</param>
        /// <param name="now">Current unscaled time.</param>
        /// <param name="maxConcurrent">Instance limit; 0 means unlimited.</param>
        /// <param name="minInterval">Minimum seconds between starts; 0 disables.</param>
        public static bool Allows(int activeCount, float lastStartTime, float now,
                                  int maxConcurrent, float minInterval)
        {
            if (maxConcurrent > 0 && activeCount >= maxConcurrent) return false;
            if (minInterval > 0f && now - lastStartTime < minInterval) return false;
            return true;
        }
    }
}
