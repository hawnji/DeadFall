using Deadfall.Core;
using NUnit.Framework;

namespace Deadfall.Tests.EditMode.Rules
{
    public class KeyBindingTests
    {
        [Test]
        public void DefaultsUntilRebound()
        {
            var s = new GameSettings();
            Assert.AreEqual("<Keyboard>/r", KeyBindings.Path(s, "Reload"));
            Assert.IsTrue(KeyBindings.IsDefault(s, "Reload"));
            KeyBindings.Set(s, "Reload", "<Keyboard>/t");
            Assert.AreEqual("<Keyboard>/t", KeyBindings.Path(s, "Reload"));
            Assert.IsFalse(KeyBindings.IsDefault(s, "Reload"));
            KeyBindings.Set(s, "Reload", "<Keyboard>/r");
            Assert.IsEmpty(s.KeyBindings); // back to default leaves nothing stored
        }

        [Test]
        public void TakingAUsedKeySwaps()
        {
            var s = new GameSettings();
            KeyBindings.Set(s, "Reload", "<Keyboard>/f"); // F was Interact
            Assert.AreEqual("<Keyboard>/f", KeyBindings.Path(s, "Reload"));
            Assert.AreEqual("<Keyboard>/r", KeyBindings.Path(s, "Interact"));
            foreach (KeyBindings.Entry a in KeyBindings.All)
                foreach (KeyBindings.Entry b in KeyBindings.All)
                    if (a.Id != b.Id) Assert.AreNotEqual(KeyBindings.Path(s, a.Id), KeyBindings.Path(s, b.Id));
        }

        [Test]
        public void KeyNamesAndPaths()
        {
            // (FromKeyCode isn't covered here: KeyCode lives in a Unity module the offline test runner doesn't load.)
            Assert.AreEqual("<Mouse>/backButton", KeyBindings.FromMouseButton(3));
            Assert.AreEqual("Left Shift", KeyBindings.Display("<Keyboard>/leftShift"));
            Assert.AreEqual("Q", KeyBindings.Display("<Keyboard>/q"));
            Assert.AreEqual("Mouse 4", KeyBindings.Display("<Mouse>/backButton"));
        }
    }
}
