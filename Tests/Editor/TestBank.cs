namespace Hlight.Audio.Tests
{
    /// <summary>
    /// Deliberately a primary type in a file named after it: Unity cannot resolve a
    /// ScriptableObject asset back to a class declared as a secondary type in another
    /// file, so a bank declared that way fails to reload. Real games must follow the
    /// same rule for their own banks.
    /// </summary>
    public sealed class TestBank : AudioBank<TestSfx> { }

    public enum TestSfx { Click, Coin, Boom }
}
