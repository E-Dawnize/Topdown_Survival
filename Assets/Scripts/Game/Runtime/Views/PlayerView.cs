using Game.Core;
using Game.Core.Events;
using Game.Model;
using UnityEngine;

namespace Game.Views
{
    /// <summary>
    /// 把 PlayerModel 的位置与朝向写到 Transform，并在冲刺时闪一下颜色。
    /// 只读 Model，不写。
    /// </summary>
    public sealed class PlayerView : MonoBehaviour
    {
        [SerializeField] Color _dashColor = new Color(0.35f, 0.75f, 1f);
        [SerializeField] float _flashDuration = 0.14f;

        PlayerModel _model;
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

        public void Bind(PlayerModel model)
        {
            _model = model;
        }

        public void Sync()
        {
            if (_model == null) return;

            Vector3 p = transform.position;
            p.x = _model.Position.x;
            p.z = _model.Position.y;
            transform.position = p;

            // 模型默认朝 +Z，绕 Y 转 (90 - facing) 后 +Z 指向 facing 方向
            transform.rotation = Quaternion.Euler(0f, 90f - _model.Facing * Mathf.Rad2Deg, 0f);

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
