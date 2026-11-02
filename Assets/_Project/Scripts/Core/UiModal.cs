namespace Deadfall.Core
{
    /// <summary>
    /// Which full-attention menu is open (Supply Cache, Field Mod pick), if any. Pause-screen panels hide while
    /// one is open, since both use the free cursor.
    /// </summary>
    public static class UiModal
    {
        public static string Owner { get; private set; }
        public static bool Active => !string.IsNullOrEmpty(Owner);

        public static bool TryOpen(string owner)
        {
            if (Active && Owner != owner) return false;
            Owner = owner;
            return true;
        }

        public static void Close(string owner)
        {
            if (Owner == owner) Owner = null;
        }

        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset() => Owner = null;
    }
}
