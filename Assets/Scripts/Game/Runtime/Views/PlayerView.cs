using Game.Core;
using Game.Core.Events;
using UnityEngine;

namespace Game.Views
{
    /// <summary>
    /// 把渲染快照写到 Transform，并在冲刺时闪一下颜色。
    /// 不读 Model：位置与朝向都是插值后的渲染值，由 RunLoop 供给。
    /// </summary>
    public sealed class PlayerView : MonoBehaviour
    {
        [SerializeField] Color _dashColor = new Color(0.35f, 0.75f, 1f);
        [SerializeField] float _flashDuration = 0.14f;

        Renderer _renderer;
        MaterialPropertyBlock _mpb;
        Color _baseColor = Color.white;
        bool _hasBaseColor;
        float _flash;
        IEventBus _bus;

        void Awake()
        {
            _renderer = GetComponent<Renderer>();
            _mpb = new MaterialPropertyBlock();

            if (_renderer != null && _renderer.sharedMaterial != null &&
                _renderer.sharedMaterial.HasProperty("_BaseColor"))
            {
                _baseColor = _renderer.sharedMaterial.GetColor("_BaseColor");
                _hasBaseColor = true;
            }
        }

        void OnEnable()
        {
            AppRoot root = AppRoot.Instance;
            if (root == null) return;
            _bus = root.Events;
            _bus?.Subscribe<PlayerDashed>(OnDashed);
            root.BindPlayerView(this);
        }

        void OnDisable()
        {
            _bus?.Unsubscribe<PlayerDashed>(OnDashed);
            _bus = null;
        }

        /// <summary>位置与朝向都来自插值后的渲染快照，不是模拟值。</summary>
        public void Sync(in ViewSnapshot snapshot)
        {
            Vector3 p = transform.position;
            p.x = snapshot.Position.x;
            p.z = snapshot.Position.y;
            transform.position = p;

            // 模型默认朝 +Z，LookRotation 把 +Z 转到 Forward 指向的世界方向
            if (snapshot.Forward.sqrMagnitude > 1e-6f)
            {
                transform.rotation = Quaternion.LookRotation(
                    new Vector3(snapshot.Forward.x, 0f, snapshot.Forward.y), Vector3.up);
            }

            UpdateFlash();
        }

        void OnDashed(PlayerDashed _)
        {
            _flash = _flashDuration;
        }

        void UpdateFlash()
        {
            if (_renderer == null || !_hasBaseColor) return;

            if (_flash > 0f)
            {
                _flash -= Time.deltaTime;
                float t = Mathf.Clamp01(_flash / _flashDuration);
                _mpb.SetColor("_BaseColor", Color.Lerp(_baseColor, _dashColor, t));
                _renderer.SetPropertyBlock(_mpb);
            }
            else if (_renderer.HasPropertyBlock())
            {
                _renderer.SetPropertyBlock(null);
            }
        }
    }
}
