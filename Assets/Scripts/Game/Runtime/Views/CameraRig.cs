using Cinemachine;
using UnityEngine;

namespace Game.Views
{
    /// <summary>
    /// 相机装配与边界限制。运行时建 CameraTarget 与虚拟相机，
    /// 场景里只留主相机和 CinemachineBrain。取景、跟随与边界参数来自 CameraPreset。
    /// 边界不用 CinemachineConfiner3D：正交相机的 bounding volume 贴合是按透视推的，会穿帮。
    ///
    /// 震屏也在这里装配：脉冲源与监听器都是运行时加的，不靠场景里预先摆好的组件。
    /// 监听器挂在虚拟相机上而不是主相机上，所以脉冲发出去以后由 Brain 统一震，
    /// 相机目标点（边界夹紧用的锚点）不会被震偏。
    /// </summary>
    public sealed class CameraRig : MonoBehaviour
    {
        /// <summary>脉冲通道。源跟监听器必须同一条，否则发了没反应。</summary>
        const int ImpulseChannel = 1;

        [SerializeField] Camera _camera;
        [SerializeField] CameraPreset _preset;

        Transform _target;
        CinemachineImpulseSource _impulse;

        void Awake()
        {
            BuildRig();
            Core.AppRoot.Instance?.BindCameraRig(this);
        }

        /// <summary>
        /// 发一次脉冲。只在“值得强调”的命中上被调（触发判定在 RunLoop，不在视图层），
        /// 震屏是表现，不应该让相机自己读伤害数值去判断该不该震。
        /// </summary>
        public void Shake(float force)
        {
            _impulse?.GenerateImpulseWithForce(force);
        }

        /// <summary>
        /// 只负责把目标点夹进边界；跟随的平滑由 transposer 的阻尼做。
        /// 夹紧发生在目标上，平滑后的相机不会跑到场地外。
        /// </summary>
        public void Tick(Vector2 focus)
        {
            if (_target == null) return;

            Vector2 clamped = Clamp(focus);
            _target.position = new Vector3(clamped.x, 0f, clamped.y);
        }

        /// <summary>
        /// 边界是游戏规则，不是相机机制：Confiner3D 要一个 3D 包围体、且按相机位姿算，
        /// Confiner2D 只认 XY 平面正交相机。所以用一根锚点承载夹紧，相机只管跟着锚点走。
        /// </summary>
        Vector2 Clamp(Vector2 point)
        {
            if (_preset == null) return point;

            float aspect = _camera != null ? _camera.aspect : 16f / 9f;
            float halfWidth = _preset.OrthographicSize * aspect;
            float halfDepth = _preset.OrthographicSize / Mathf.Sin(_preset.TiltDegrees * Mathf.Deg2Rad);

            float maxX = Mathf.Max(0f, _preset.BoundsHalfExtent - halfWidth);
            float maxZ = Mathf.Max(0f, _preset.BoundsHalfExtent - halfDepth);

            return new Vector2(
                Mathf.Clamp(point.x, -maxX, maxX),
                Mathf.Clamp(point.y, -maxZ, maxZ));
        }

        void BuildRig()
        {
            if (_camera == null)
            {
                _camera = Camera.main;
            }

            if (_camera == null)
            {
                Debug.LogError("[CameraRig] 找不到主相机，相机不会跟随玩家");
                return;
            }

            if (_preset == null)
            {
                Debug.LogError("[CameraRig] 没有指定 CameraPreset，相机不会跟随玩家。" +
                               "先给 Main Camera 指定一份 Assets/Presets 下的预设");
                return;
            }

            float tilt = _preset.TiltDegrees * Mathf.Deg2Rad;
            var offset = new Vector3(
                0f,
                _preset.Distance * Mathf.Sin(tilt),
                -_preset.Distance * Mathf.Cos(tilt));

            var targetGo = new GameObject("CameraTarget");
            _target = targetGo.transform;

            var vcamGo = new GameObject("vcam_Follow");
            var vcam = vcamGo.AddComponent<CinemachineVirtualCamera>();
            vcam.Follow = _target;
            vcam.m_Lens.Orthographic = true;
            vcam.m_Lens.OrthographicSize = _preset.OrthographicSize;
            vcam.m_Lens.NearClipPlane = _preset.NearClip;
            vcam.m_Lens.FarClipPlane = _preset.FarClip;

            var transposer = vcam.AddCinemachineComponent<CinemachineTransposer>();
            transposer.m_BindingMode = CinemachineTransposer.BindingMode.LockToTargetNoRoll;
            transposer.m_FollowOffset = offset;
            transposer.m_XDamping = _preset.Damping;
            transposer.m_YDamping = 0f;
            transposer.m_ZDamping = _preset.Damping;

            // 监听器只接一条脉冲通道，增益默认为 1；在相机空间里震，
            // 所以斜视相机的“上下”就是屏幕上下，不会变成往场地深层推。
            var listener = vcamGo.AddComponent<CinemachineImpulseListener>();
            listener.m_ApplyAfter = CinemachineCore.Stage.Noise;
            listener.m_ChannelMask = ImpulseChannel;
            listener.m_Gain = 1f;
            listener.m_Use2DDistance = false;
            listener.m_UseCameraSpace = true;

            BuildImpulseSource();

            // Brain 接管前的那一帧也用预设里的取景，避免和场景里的旧值不一致
            _camera.orthographic = true;
            _camera.orthographicSize = _preset.OrthographicSize;
        }

        /// <summary>
        /// 脉冲源。幅度与时长写成代码里的固定值：参数在 CameraPreset 里的话，
        /// 要调手感就得改资产，而这两个数只在“卡一下”这个量级上有意义。
        /// 没指定 ImpulseSignal 资产，靠定义里的 Bump 曲线自己生成，不依赖项目里额外的资源。
        /// </summary>
        void BuildImpulseSource()
        {
            _impulse = gameObject.AddComponent<CinemachineImpulseSource>();
            _impulse.m_DefaultVelocity = Vector3.down;   // 屏幕空间上下震：竖着抖比横着抖更像挨了一下
            _impulse.m_ImpulseDefinition = new CinemachineImpulseDefinition
            {
                m_ImpulseChannel = ImpulseChannel,
                m_ImpulseShape = CinemachineImpulseDefinition.ImpulseShapes.Bump,
                m_ImpulseDuration = 0.14f,
                m_ImpulseType = CinemachineImpulseDefinition.ImpulseTypes.Uniform,
                m_DissipationDistance = 100f,
                m_DissipationRate = 0.25f,
                m_PropagationSpeed = 343f
            };
        }
    }
}
