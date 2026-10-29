using UnityEngine;

namespace Game.Views
{
    /// <summary>
    /// 相机目标点的边界限制。Cinemachine 负责跟随，这里只负责把目标点夹在场地内，
    /// 避免正交视野露出场地边缘之外。不用 CinemachineConfiner3D：正交相机的
    /// bounding volume 贴合是按透视推的，边缘会穿帮。
    /// </summary>
    public sealed class CameraRig : MonoBehaviour
    {
        [SerializeField] Transform _target;
        [SerializeField] Camera _camera;
        [SerializeField] float _orthoSize = 7f;
        [SerializeField] float _tiltDegrees = 50f;
        [SerializeField] float _groundHalfExtent = 80f;

        public void Configure(Transform target, Camera cam, float orthoSize, float tiltDegrees, float groundHalfExtent)
        {
            _target = target;
            _camera = cam;
            _orthoSize = orthoSize;
            _tiltDegrees = tiltDegrees;
            _groundHalfExtent = groundHalfExtent;
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

        void Awake()
        {
            Core.AppRoot.Instance?.BindCameraRig(this);
        }
    }
}
