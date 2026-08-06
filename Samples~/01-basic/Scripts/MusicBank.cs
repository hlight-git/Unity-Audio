using UnityEngine;

namespace Hlight.Audio.Samples.Basic
{
    /// <summary>
    /// The whole binding: one line. TKey is fixed here, so Play only accepts MusicId.
    /// exclusive = true on the asset — one music track plays at a time, crossfading the old
    /// one out instead of layering. Must stay the primary type in a file named after it — see
    /// the package README's "Concrete bank subclass" note for why a saved .asset silently
    /// fails to reload otherwise.
    /// </summary>
    [CreateAssetMenu(menuName = "Hlight/Audio Samples/01 Music Bank")]
    public sealed class MusicBank : AudioBank<MusicId> { }
}
