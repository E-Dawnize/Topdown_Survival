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

        [Header("构图")]
        [Tooltip("跟踪点相对角色根节点的偏移。角色根在脚底、模型中心更高时把 y 调到身体中心。")]
        [SerializeField] Vector3 _trackedObjectOffset = Vector3.zero;

        [Tooltip("角色在屏幕上的归一化偏移。0,0 是屏幕中心，0.1,0 是向右偏 10%。")]
        [SerializeField, Range(-0.5f, 0.5f)] Vector2 _screenOffset = Vector2.zero;

        [Tooltip("矩形死区占屏幕宽/高的比例。角色在框内时相机不移动；0.14 表示 14%。")]
        [SerializeField, Range(0f, 0.5f)] float _deadZone = 0.14f;

        [Header("跟随")]
        [Tooltip("跟随阻尼（秒，SmoothDamp 的 smoothTime）。越大，镜头越晚追上角色；建议 0.25~0.4。")]
        [SerializeField, Range(0f, 2f)] float _damping = 0.32f;

        [Header("边界")]
        [Tooltip("是否把相机目标夹在场地内。菜单/静帧相机通常关闭。")]
        [SerializeField] bool _clampToBounds = true;

        [Tooltip("相机目标点的移动范围半宽，米。要略大于场地半宽，留出视野余量")]
        [SerializeField] float _boundsHalfExtent = 80f;

        [Header("相机优先级")]
        [Tooltip("同一场景有多台虚拟相机时，优先级高的接管 Brain。")]
        [SerializeField, Range(0, 1000)] int _priority = 100;

        [Header("震动")]
        [Tooltip("本预设的震屏增益。菜单/静帧场景设为 0 可以完全关掉。")]
        [SerializeField, Range(0f, 2f)] float _shakeGain = 1f;

        public float OrthographicSize => _orthographicSize;
        public float TiltDegrees => _tiltDegrees;
        public float Distance => _distance;
        public float NearClip => _nearClip;
        public float FarClip => _farClip;
        public Vector3 TrackedObjectOffset => _trackedObjectOffset;
        public Vector2 ScreenOffset => _screenOffset;

        public float Damping => _damping;
        public float DeadZone => _deadZone;

        public bool ClampToBounds => _clampToBounds;
        public float BoundsHalfExtent => _boundsHalfExtent;
        public int Priority => _priority;
        public float ShakeGain => _shakeGain;
    }
}
