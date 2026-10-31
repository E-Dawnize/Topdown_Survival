using UnityEngine;

namespace Game.Views
{
    /// <summary>
    /// 相机取景与跟随的预设。参数集中在一份资产里，
    /// 改手感不用碰代码，换场景换一份预设即可。
    /// </summary>
    [CreateAssetMenu(menuName = "Game/Camera Preset", fileName = "CameraPreset")]
    public sealed class CameraPreset : ScriptableObject
    {
        [Header("取景")]
        [SerializeField, Range(2f, 30f)] float _orthographicSize = 7f;
        [SerializeField, Range(10f, 89f)] float _tiltDegrees = 50f;
        [SerializeField, Range(5f, 80f)] float _distance = 30f;
        [SerializeField] float _nearClip = 0.3f;
        [SerializeField] float _farClip = 300f;

        [Header("跟随")]
        [Tooltip("Cinemachine 阻尼：追上目标 99% 所需秒数。时间常数 = 值 / 4.605")]
        [SerializeField, Range(0f, 5f)] float _damping = 0.7f;

        [Header("边界")]
        [Tooltip("相机目标点的移动范围半宽，米。要略大于场地半宽，留出视野余量")]
        [SerializeField] float _boundsHalfExtent = 80f;

        public float OrthographicSize => _orthographicSize;
        public float TiltDegrees => _tiltDegrees;
        public float Distance => _distance;
        public float NearClip => _nearClip;
        public float FarClip => _farClip;
        public float Damping => _damping;
        public float BoundsHalfExtent => _boundsHalfExtent;
    }
}
