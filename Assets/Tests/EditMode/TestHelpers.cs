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
    }
}
