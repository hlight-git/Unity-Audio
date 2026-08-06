using UnityEngine;

namespace Hlight.Audio.Samples.Basic
{
    /// <summary>
    /// The whole binding: one line. TKey is fixed here, so Play only accepts SfxId.
    /// Must stay the primary type in a file named after it — see the package README's
    /// "Concrete bank subclass" note for why a saved .asset silently fails to reload otherwise.
    /// </summary>
    [CreateAssetMenu(menuName = "Hlight/Audio Samples/01 Sfx Bank")]
    public sealed class SfxBank : AudioBank<SfxId> { }
}
