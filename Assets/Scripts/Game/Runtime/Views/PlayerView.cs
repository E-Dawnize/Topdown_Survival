using Game.Core.Events;
using Game.Model;
using Game.Systems.Weapons;
using UnityEngine;

namespace Game.Views
{
    /// <summary>
    /// 把渲染快照写到 Transform，并在冲刺时闪一下颜色。
    /// 不读 Model 的位置与朝向：它们是插值后的渲染值，由 RunLoop 供给。
    ///
    /// 依赖（玩家模型、武器系统、事件总线）由装配根在实例化后显式 Bind 进来，
    /// 不再在 OnEnable 里回查 AppRoot.Instance：视图不应该知道装配根存在，
    /// 否则“先在空场景里放一个 PlayerView 试试”会安静地拿不到事件。
    ///
    /// 进场景时给自己挂一圈描边壳（见 PlayerOutline）：200 个敌人围上来时，
    /// 浅蓝的胶囊会被敌群淹掉，靠描边壳才能一眼找到玩家。壳是子节点，位置朝向自动跟随。
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
        bool _subscribed;
        PlayerModel _player;
        WeaponSystem _weapons;
        WeaponRackView _weaponRack;

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

            // 描边壳与冲刺闪色是两回事：壳有自己的材质与属性块，闪色只改身体那个渲染器
            PlayerOutline.Attach(transform, _renderer);

            // 武器架只建一次；武器集合在武器系统就绪后由 Sync 懒加载。
            // 模型来自 WeaponVisualCatalog 的 prefab，不再从身体材质现场拼几何体。
            _weaponRack = WeaponRackView.Attach(transform, WeaponVisualCatalog.LoadDefault());
        }

        /// <summary>
        /// 装配根在实例化后调用。武器与玩家模型可以晚到（比如调试场景里先放玩家再建系统），
        /// 每次调用都会把订阅切到新的总线上，不会重复订阅。
        /// </summary>
        public void Bind(PlayerModel player, WeaponSystem weapons, IEventBus bus)
        {
            _player = player;
            _weapons = weapons;

            if (ReferenceEquals(_bus, bus)) return;

            Unsubscribe();
            _bus = bus;
            Subscribe();
        }

        void OnEnable()
        {
            Subscribe();
        }

        void OnDisable()
        {
            Unsubscribe();
        }

        void Subscribe()
        {
            if (_subscribed || _bus == null) return;

            _bus.Subscribe<PlayerDashed>(OnDashed);
            _subscribed = true;
        }

        void Unsubscribe()
        {
            if (!_subscribed) return;

            _bus?.Unsubscribe<PlayerDashed>(OnDashed);
            _subscribed = false;
        }

        /// <summary>位置与朝向都来自插值后的渲染快照，不是模拟值。</summary>
        public void Sync(Vector2 position, Vector2 forward)
        {
            Vector3 p = transform.position;
            p.x = position.x;
            p.z = position.y;
            transform.position = p;

            // 模型默认朝 +Z，LookRotation 把 +Z 转到 Forward 指向的世界方向
            if (forward.sqrMagnitude > 1e-6f)
            {
                transform.rotation = Quaternion.LookRotation(
                    new Vector3(forward.x, 0f, forward.y), Vector3.up);
            }

            UpdateFlash();

            Vector2 velocity = _player != null ? _player.Velocity : Vector2.zero;
            _weaponRack?.Sync(_weapons, velocity);
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
