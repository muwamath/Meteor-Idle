using System.Reflection;
using UnityEngine;

namespace MeteorIdle.Tests.Editor
{
    // Unity does not reliably fire Awake on components added via `new GameObject(typeof(T))`
    // in EditMode tests. Reflection-invoke it so our components are fully initialized
    // before assertions run.
    internal static class TestHelpers
    {
        public static void InvokeAwake(MonoBehaviour mb)
        {
            var method = mb.GetType().GetMethod(
                "Awake",
                BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
            method?.Invoke(mb, null);
        }

        // EditMode tests never tick the engine, so we manually call Update when a
        // test needs to exercise position-driven logic (e.g. Meteor fade threshold).
        // Time.deltaTime is 0 in this context, so any deltaTime-scaled progression
        // (the fade timer) won't advance — that's fine; tests only need to verify
        // state transitions, not the fade animation itself.
        public static void InvokeUpdate(MonoBehaviour mb)
        {
            var method = mb.GetType().GetMethod(
                "Update",
                BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
            method?.Invoke(mb, null);
        }
    }
}
