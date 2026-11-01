// Compile-check stubs ONLY (not in the repo). Mirrors the subset of com.unity.inputsystem 1.x API used by Deadfall.
using System;
namespace UnityEngine.InputSystem
{
    public enum InputActionType { Value, Button, PassThrough }
    public sealed class InputAction : IDisposable
    {
        public InputAction(string name = null, InputActionType type = default, string binding = null, string interactions = null, string processors = null, string expectedControlType = null) { }
        public string name => null;
        public TValue ReadValue<TValue>() where TValue : struct => default;
        public bool IsPressed() => false;
        public bool WasPressedThisFrame() => false;
        public bool WasReleasedThisFrame() => false;
        public void Enable() { }
        public void Disable() { }
        public void Dispose() { }
        public bool enabled => false;
    }
    public sealed class InputActionMap : IDisposable
    {
        public InputActionMap(string name = null) { }
        public void Enable() { }
        public void Disable() { }
        public void Dispose() { }
        public bool enabled => false;
    }
    public static class InputActionSetupExtensions
    {
        public static InputAction AddAction(this InputActionMap map, string name, InputActionType type = default, string binding = null, string interactions = null, string processors = null, string groups = null, string expectedControlLayout = null) => null;
        public static BindingSyntax AddBinding(this InputAction action, string path, string interactions = null, string processors = null, string groups = null) => default;
        public static CompositeSyntax AddCompositeBinding(this InputAction action, string composite, string interactions = null, string processors = null) => default;
        public struct BindingSyntax { public BindingSyntax WithProcessor(string processor) => this; }
        public struct CompositeSyntax { public CompositeSyntax With(string name, string binding, string groups = null, string processors = null) => this; }
    }
}
