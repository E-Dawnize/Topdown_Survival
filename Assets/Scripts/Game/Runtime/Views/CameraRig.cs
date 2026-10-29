using Cinemachine;
using UnityEngine;

namespace Game.Views
{
    /// <summary>
    /// 相机装配与边界限制。运行时建 CameraTarget 与虚拟相机，
    /// 场景里只留主相机和 CinemachineBrain。
    /// 边界不用 CinemachineConfiner3D：正交相机的 bounding volume 贴合是按透视推的，会穿帮。
    /// </summary>
    public sealed class CameraRig : MonoBehaviour
    {
        [SerializeField] Camera _camera;
        [SerializeField] float _orthoSize = 7f;
        [SerializeField] float _tiltDegrees = 50f;
        [SerializeField] float _groundHalfExtent = 80f;
        [SerializeField] float _distance = 30f;
        [SerializeField] float _damping = 0.35f;

        Transform _target;
        CinemachineVirtualCamera _vcam;

        void Awake()
        {
            BuildRig();
            Core.AppRoot.Instance?.BindCameraRig(this);
        }

        public void Tick(Vector2 focus)
        {
            if (_target == null) return;
            Vector2 clamped = Clamp(focus);
            _target.position = new Vector3(clamped.x, 0f, clamped.y);
        }

        public Vector2 Clamp(Vector2 point)
        {
            float aspect = _camera != null ? _camera.aspect : 16f / 9f;
            float halfWidth = _orthoSize * aspect;
            float halfDepth = _orthoSize / Mathf.Sin(_tiltDegrees * Mathf.Deg2Rad);

            float maxX = Mathf.Max(0f, _groundHalfExtent - halfWidth);
            float maxZ = Mathf.Max(0f, _groundHalfExtent - halfDepth);

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

            float tilt = _tiltDegrees * Mathf.Deg2Rad;
            var offset = new Vector3(0f, _distance * Mathf.Sin(tilt), -_distance * Mathf.Cos(tilt));

            var targetGo = new GameObject("CameraTarget");
            _target = targetGo.transform;

            var vcamGo = new GameObject("vcam_Follow");
            vcamGo.transform.position = offset;
            vcamGo.transform.rotation = Quaternion.Euler(_tiltDegrees, 0f, 0f);

            _vcam = vcamGo.AddComponent<CinemachineVirtualCamera>();
            _vcam.Priority = 10;
            _vcam.Follow = _target;
            _vcam.m_Lens.Orthographic = true;
            _vcam.m_Lens.OrthographicSize = _orthoSize;
            _vcam.m_Lens.NearClipPlane = 0.3f;
            _vcam.m_Lens.FarClipPlane = 300f;

            CinemachineTransposer transposer = _vcam.AddCinemachineComponent<CinemachineTransposer>();
            transposer.m_BindingMode = CinemachineTransposer.BindingMode.LockToTargetNoRoll;
            transposer.m_FollowOffset = offset;
            transposer.m_XDamping = _damping;
            transposer.m_YDamping = 0f;
            transposer.m_ZDamping = _damping;
        }
    }
}
