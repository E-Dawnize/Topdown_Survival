using Cinemachine;
using UnityEngine;

namespace Game.Views
{
    /// <summary>
    /// 相机装配与边界限制。运行时建 CameraTarget 与虚拟相机，
    /// 场景里只留主相机和 CinemachineBrain。取景、跟随与边界参数来自 CameraPreset。
    /// 相机朝向固定在 (Tilt, 0, 0)，Transposer 只按焦点定位；矩形死区与两轴阻尼在 CameraFocus 里。
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
        CameraFocus _follow;
        CinemachineImpulseSource _impulse;
        bool _built;

        void Awake()
        {
            BuildRig();
        }

        /// <summary>
        /// 显式初始化入口：装配根实例化/找到相机装置后把相机与预设注入进来。
        /// 这样 CameraRig 不需要回查 AppRoot.Instance 自注册——视图组件不知道装配根存在，
        /// 单独把它放进一个场景调试时也不会去碰一个不存在的单例。
        ///
        /// 场景里已经序列化好 _camera/_preset 时可以不调：Awake 会自己建。
        /// 重复调用无害（已经建好就直接返回）。
        /// </summary>
        public void Initialize(Camera camera, CameraPreset preset)
        {
            _camera = camera;
            _preset = preset;
            BuildRig();
        }

        /// <summary>
        /// 发一次脉冲。只在“值得强调”的命中上被调（触发判定在 RunLoop，不在视图层），
        /// 震屏是表现，不应该让相机自己读伤害数值去判断该不该震。
        /// </summary>
        public void Shake(float force)
        {
            float gain = _preset != null ? _preset.ShakeGain : 1f;
            _impulse?.GenerateImpulseWithForce(force * gain);
        }

        /// <summary>
        /// 只负责把目标点夹进边界，并推进矩形死区/阻尼；相机位姿由 Transposer 按焦点定位。
        /// 夹紧发生在焦点上，平滑后的相机不会跑到场地外。
        /// </summary>
        public void Tick(Vector2 focus)
        {
            Tick(focus, Time.deltaTime);
        }

        /// <summary>显式 dt 版，给确定性测试用；运行时走 Time.deltaTime 的重载。</summary>
        public void Tick(Vector2 focus, float deltaTime)
        {
            if (_target == null || _follow == null || _preset == null) return;

            Vector2 clamped = Clamp(focus);

            float aspect = _camera != null ? _camera.aspect : 16f / 9f;
            float halfWidth = _preset.OrthographicSize * aspect;
            float halfDepth = _preset.OrthographicSize / Mathf.Sin(_preset.TiltDegrees * Mathf.Deg2Rad);

            // 屏幕偏移：正 x 让角色出现在中心右侧，所以焦点要往左让。
            var screenOffset = new Vector2(
                _preset.ScreenOffset.x * 2f * halfWidth,
                _preset.ScreenOffset.y * 2f * halfDepth);

            var tracked = new Vector2(_preset.TrackedObjectOffset.x, _preset.TrackedObjectOffset.z);
            Vector2 point = clamped + tracked - screenOffset;
            Vector2 next = _follow.Tick(point, deltaTime, halfWidth, halfDepth);

            // 跟踪偏移的 y 给 Boss 这类身体中心高于脚底的取景用；角色本身仍在 y=0。
            _target.position = new Vector3(next.x, _preset.TrackedObjectOffset.y, next.y);
        }

        /// <summary>
        /// 边界是游戏规则，不是相机机制：Confiner3D 要一个 3D 包围体、且按相机位姿算，
        /// Confiner2D 只认 XY 平面正交相机。所以用一根锚点承载夹紧，相机只管跟着锚点走。
        /// </summary>
        Vector2 Clamp(Vector2 point)
        {
            if (_preset == null || !_preset.ClampToBounds) return point;

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
            if (_built) return;

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

            _built = true;

            float tilt = _preset.TiltDegrees * Mathf.Deg2Rad;
            var offset = new Vector3(
                0f,
                _preset.Distance * Mathf.Sin(tilt),
                -_preset.Distance * Mathf.Cos(tilt));

            _follow = new CameraFocus(_preset.DeadZone, _preset.Damping);

            var targetGo = new GameObject("CameraTarget");
            _target = targetGo.transform;

            var vcamGo = new GameObject("vcam_Follow");
            var vcam = vcamGo.AddComponent<CinemachineVirtualCamera>();
            vcam.Priority = _preset.Priority;
            vcam.Follow = _target;

            // 相机朝向固定，不再用 LookAt + Composer 去转镜头。
            // 死区与阻尼全在 CameraFocus 里按地面 XZ 两轴算，横向和纵向走同一套逻辑；
            // 这也让 50° 斜视下的屏幕矩形不会因为镜头俯仰而变化。
            vcamGo.transform.rotation = Quaternion.Euler(_preset.TiltDegrees, 0f, 0f);
            vcam.m_Lens.Orthographic = true;
            vcam.m_Lens.OrthographicSize = _preset.OrthographicSize;
            vcam.m_Lens.NearClipPlane = _preset.NearClip;
            vcam.m_Lens.FarClipPlane = _preset.FarClip;

            // Transposer 只负责“焦点 + 固定偏移”，阻尼为 0；
            // 位置平滑已经由 CameraFocus 做，两层阻尼叠起来会显得黏。
            var transposer = vcam.AddCinemachineComponent<CinemachineTransposer>();
            transposer.m_BindingMode = CinemachineTransposer.BindingMode.LockToTargetNoRoll;
            transposer.m_FollowOffset = offset;
            transposer.m_XDamping = 0f;
            transposer.m_YDamping = 0f;
            transposer.m_ZDamping = 0f;

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
        /// 脉冲源。曲线形状与时长是代码常量，震屏增益由 CameraPreset.ShakeGain 提供；
        /// 菜单/静帧预设可以把它设为 0。没指定 ImpulseSignal 资产，靠 Bump 曲线自己生成。
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
