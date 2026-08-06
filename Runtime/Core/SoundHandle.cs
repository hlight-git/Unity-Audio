using System;

namespace Hlight.Audio
{
    /// <summary>
    /// Reference to one playing sound. Goes stale automatically when the pool slot is
    /// reused, so an old handle can never control a different sound.
    /// </summary>
    public readonly struct SoundHandle : IEquatable<SoundHandle>
    {
        public static readonly SoundHandle None = default;

        /// <summary>Index into the runtime's source array.</summary>
        public readonly int Slot;

        /// <summary>That slot's reuse counter at the time this handle was issued. Zero means none.</summary>
        public readonly int Generation;

        public SoundHandle(int slot, int generation)
        {
            Slot = slot;
            Generation = generation;
        }

        public bool IsSome => Generation != 0;

        public bool Equals(SoundHandle other) => Slot == other.Slot && Generation == other.Generation;
        public override bool Equals(object obj) => obj is SoundHandle o && Equals(o);
        public override int GetHashCode() => HashCode.Combine(Slot, Generation);
        public static bool operator ==(SoundHandle a, SoundHandle b) => a.Equals(b);
        public static bool operator !=(SoundHandle a, SoundHandle b) => !a.Equals(b);
        public override string ToString() => IsSome ? $"Sound({Slot}:{Generation})" : "Sound(none)";
    }
}
