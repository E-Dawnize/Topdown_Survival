using System.Collections;
using System.Reflection;
using Cinemachine;
using Game.Views;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Game.Tests
{
    /// <summary>
    /// 装配层验证：真实的 CameraRig + CinemachineBrain 下，屏幕中心是矩形死区，
    /// 横向和纵向出框后都会停到框边。纯数学部分见 CameraFocusTests。
    /// </summary>
    public class CameraRigPlayTests
    {
        static readonly FieldInfo CameraField = typeof(CameraRig).GetField("_camera", BindingFlags.Instance | BindingFlags.NonPublic);
        static readonly FieldInfo PresetField = typeof(CameraRig).GetField("_preset", BindingFlags.Instance | BindingFlags.NonPublic);

        [UnityTest]
        public IEnumerator RigUsesRectangularDeadZoneAndDampsBothAxes()
        {
            var preset = ScriptableObject.CreateInstance<CameraPreset>();
            float tilt = preset.TiltDegrees * Mathf.Deg2Rad;
            float deadX = preset.DeadZone * preset.OrthographicSize * (16f / 9f);
            float deadZ = preset.DeadZone * (preset.OrthographicSize / Mathf.Sin(tilt));

            var camGo = new GameObject("PlayTestCamera");
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = 7f;
            cam.aspect = 16f / 9f;
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = 300f;
            camGo.AddComponent<CinemachineBrain>();

            var rigGo = new GameObject("PlayTestRig");
            rigGo.SetActive(false);
            var rig = rigGo.AddComponent<CameraRig>();
            CameraField.SetValue(rig, cam);
            PresetField.SetValue(rig, preset);
            rigGo.SetActive(true);

            yield return null;
            rig.Tick(Vector2.zero, 1f / 60f);
            yield return null;
            yield return null;

            Vector3 atZero = cam.WorldToViewportPoint(Vector3.zero);
            Assert.AreEqual(0.5f, atZero.x, 0.01f, "initial focus should be centered");
            Assert.AreEqual(0.5f, atZero.y, 0.01f, "initial focus should be centered");

            // 1m right: inside the 0.14 dead zone (dead half = 1.74m); camera must not move.
            rig.Tick(new Vector2(1f, 0f), 1f / 60f);
            yield return null;
            yield return null;
            Assert.AreEqual(0f, cam.transform.position.x, 0.02f, "inside dead zone should not pan");

            // 3m right: after 2s the focus should sit at 3 - 1.74 = 1.26; target at the dead-zone edge.
            for (int i = 0; i < 130; i++)
            {
                rig.Tick(new Vector2(3f, 0f), 1f / 60f);
                yield return null;
            }

            Assert.AreEqual(3f - deadX, cam.transform.position.x, 0.05f,
                "horizontal focus should stop at the dead-zone edge");
            Vector3 atRight = cam.WorldToViewportPoint(new Vector3(3f, 0f, 0f));
            Assert.AreEqual(0.5f + preset.DeadZone * 0.5f, atRight.x, 0.01f,
                "target should be at the right edge of the 14% rectangle");

            // Now vertically: target z=2 is outside dead half (1.28m); focus z should settle to 0.72.
            for (int i = 0; i < 130; i++)
            {
                rig.Tick(new Vector2(3f, 2f), 1f / 60f);
                yield return null;
            }

            float expectedFocusZ = 2f - deadZ;
            Assert.AreEqual(expectedFocusZ - preset.Distance * Mathf.Cos(tilt), cam.transform.position.z, 0.05f,
                "vertical focus should stop at the dead-zone edge too");
            Vector3 atTop = cam.WorldToViewportPoint(new Vector3(3f, 0f, 2f));
            Assert.AreEqual(0.5f + preset.DeadZone * 0.5f, atTop.y, 0.01f,
                "target should be at the top edge of the 14% rectangle");

            Object.Destroy(rigGo);
            Object.Destroy(camGo);
            Object.Destroy(preset);
        }
    }
}
