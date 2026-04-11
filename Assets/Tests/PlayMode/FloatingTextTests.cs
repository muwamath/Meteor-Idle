#if UNITY_EDITOR
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MeteorIdle.Tests.PlayMode
{
    // PlayMode tests for FloatingText — the "+$N" world-space tween that
    // appears on every missile kill. Pure visual element but the lifetime /
    // rise / alpha-fade curve is numeric state we can pin. Auto-destruction
    // at t >= 1 is also asserted because a leaked FloatingText would pile up
    // dead GameObjects under the scene root over a long play session.
    public class FloatingTextTests : PlayModeTestFixture
    {
        // Mirrors the serialized defaults on FloatingText.prefab. If either
        // drifts, update alongside — the test's job is to pin the curve.
        private const float Lifetime = 1f;
        private const float RiseDistance = 1.5f;

        // The TextMeshPro type lives in a precompiled reference we don't pull
        // into the PlayMode test asmdef (it overrides references to keep the
        // test assembly light). Reach the color via reflection on the private
        // `text` field so the test can assert alpha without an asmdef change.
        private static Color GetTextColor(FloatingText ft)
        {
            var field = typeof(FloatingText).GetField(
                "text",
                BindingFlags.NonPublic | BindingFlags.Instance);
            var comp = field.GetValue(ft);
            var colorProp = comp.GetType().GetProperty("color");
            return (Color)colorProp.GetValue(comp);
        }

        private static void SetTextColor(FloatingText ft, Color c)
        {
            var field = typeof(FloatingText).GetField(
                "text",
                BindingFlags.NonPublic | BindingFlags.Instance);
            var comp = field.GetValue(ft);
            var colorProp = comp.GetType().GetProperty("color");
            colorProp.SetValue(comp, c);
        }

        [UnityTest]
        public IEnumerator RisesAndFadesLinearly_ThenDestroys()
        {
            yield return SetupScene();

            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<FloatingText>(
                "Assets/Prefabs/FloatingText.prefab");
            Assert.IsNotNull(prefab, "FloatingText.prefab must load from Assets/Prefabs");

            var start = new Vector3(2f, 3f, 0f);
            var ft = Object.Instantiate(prefab, start, Quaternion.identity);
            ft.name = "TestFloatingText";
            ft.Show("+$5");

            // --- midlife: ~50% of lifetime ---------------------------------
            yield return new WaitForSeconds(Lifetime * 0.5f);
            // Timing in PlayMode isn't surgical — WaitForSeconds lands within
            // a frame or two of the target. Give each check a ~15% band.
            float halfY = start.y + RiseDistance * 0.5f;
            Assert.AreEqual(halfY, ft.transform.position.y, RiseDistance * 0.15f,
                "y position should track rise curve at midlife");
            Assert.AreEqual(0.5f, GetTextColor(ft).a, 0.15f,
                "alpha should fade to ~0.5 at midlife");
            // x must not drift — rise is purely vertical.
            Assert.AreEqual(start.x, ft.transform.position.x, 1e-4);

            // --- end of life: wait past lifetime, assert destroyed ---------
            yield return new WaitForSeconds(Lifetime * 0.7f);
            // Unity's null check: destroyed MonoBehaviours equal null via the
            // overloaded == operator even though the CLR object still exists.
            Assert.IsTrue(ft == null,
                "FloatingText should self-destroy once t >= 1");

            TeardownScene();
        }

        [UnityTest]
        public IEnumerator Show_ResetsAlphaToOne()
        {
            yield return SetupScene();

            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<FloatingText>(
                "Assets/Prefabs/FloatingText.prefab");
            Assert.IsNotNull(prefab);

            var ft = Object.Instantiate(prefab, new Vector3(0f, 0f, 0f), Quaternion.identity);
            ft.name = "TestFloatingTextAlpha";

            // Predirty the alpha so we can prove Show() resets it.
            var dimmed = GetTextColor(ft);
            dimmed.a = 0.1f;
            SetTextColor(ft, dimmed);

            ft.Show("+$1");
            // Read the alpha before the first Update tick can fade it —
            // Show() sets it to 1 synchronously.
            Assert.AreEqual(1f, GetTextColor(ft).a, 1e-4,
                "Show should reset alpha to 1 before the fade begins");

            // Clean up before the auto-destroy races the next test.
            Object.Destroy(ft.gameObject);
            yield return null;

            TeardownScene();
        }
    }
}
#endif
