using Game.Model;
using Game.Systems.Weapons;
using UnityEngine;

namespace Game.Views
{
    /// <summary>
    /// 一个旅伴的渲染壳。由 CompanionViewPool 在创建实例时 AddComponent 上去，
    /// 与 UnitView（敌人）同一个做法：几何体是代码建的，不占预制体也不改场景。
    ///
    /// 位置一律走 PrevPosition → Position 的插值，不读 CompanionModel.Position 的最终值直接写 Transform：
    /// 模拟是 60Hz 阶梯，渲染帧率不是 60 的整数倍时直接写会看到一顿一顿。
    /// 不写朝向：身体是胶囊，绕 Y 转 180° 与转 0° 在画面上完全一样，每帧多写一次 Transform 只是白花钱。
    /// 旅伴的朝向在这一版里也没有可读的信息（远程只在重合时兜底，近战判定不看朝向的视觉表现）。
    ///
    /// 颜色按阵营上（见 CompanionViewStyle.TintOf），共享材质 + 每个实例一份 MaterialPropertyBlock：
    /// 材质是玩家预制体那一份资产，不克隆，所以几个旅伴不会各占一个批次。
    /// </summary>
    public sealed class CompanionView : MonoBehaviour, IPrimitiveView
    {
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        Renderer _renderer;
        MaterialPropertyBlock _mpb;
        bool _hasBaseColor;

        Color _tint = Color.white;
        Color _appliedColor;
        bool _applied;

        Material _sharedMaterial;
        WeaponVisualCatalog _catalog;
        WeaponModel _weaponModel;
        WeaponVisual _weaponVisual;
        Transform _weaponAnchor;

        /// <summary>当前绑定的旅伴。槽位被下一个旅伴复用时靠它判断是不是同一个。</summary>
        public CompanionModel Companion { get; private set; }

        /// <summary>
        /// 只调一次：把身体换成传入的共享材质，并准备好属性块。
        /// 材质不在这里 new：玩家预制体上那份资产在打包时一定被引用着，新建的材质可能因为
        /// shader 没被引用而被剔除（见 SharedViewMaterial 的说明）。
        /// </summary>
        public void Initialize(Material sharedMaterial)
        {
            Initialize(sharedMaterial, WeaponVisualCatalog.LoadDefault());
        }

        public void Initialize(Material sharedMaterial, WeaponVisualCatalog catalog)
        {
            _renderer = GetComponentInChildren<Renderer>();
            _mpb = new MaterialPropertyBlock();
            _catalog = catalog;

            BuildWeaponAnchor();

            if (_renderer == null || sharedMaterial == null) return;

            _sharedMaterial = sharedMaterial;
            _renderer.sharedMaterial = sharedMaterial;
            _hasBaseColor = sharedMaterial.HasProperty(BaseColorId);
        }

        /// <summary>
        /// 绑定一个旅伴（新招募，或同一槽位换成了另一个旅伴）。
        /// 直接吸附到它当前的位置，不从上一个旅伴的位置插值过来：否则会看到一块颜色横穿战场飞过来。
        /// </summary>
        public void Bind(CompanionModel companion)
        {
            Companion = companion;
            _tint = CompanionViewStyle.TintOf(companion.Faction);
            _applied = false;

            transform.position = new Vector3(companion.Position.x, CompanionViewStyle.BodyCenterHeight,
                companion.Position.y);
            ApplyColor(_tint);
        }

        /// <summary>
        /// 绑旅伴自己的那把武器。同一把武器（同一 WeaponModel）重复调用不会重建模型；
        /// CompanionViewPool 每帧对一次引用；槽位换人或以后换武器时旧模型随之销毁。
        /// </summary>
        public void SetWeapon(WeaponModel weapon)
        {
            if (ReferenceEquals(_weaponModel, weapon)) return;

            _weaponModel = weapon;
            if (_weaponVisual != null)
            {
                Destroy(_weaponVisual.gameObject);
                _weaponVisual = null;
            }

            if (weapon == null || _weaponAnchor == null) return;

            WeaponVisualCatalog.Entry entry = _catalog != null ? _catalog.Find(weapon.Id) : null;
            int ownerKey = Companion != null ? Companion.Slot + 1 : 0;
            _weaponVisual = WeaponVisual.Attach(_weaponAnchor, weapon, entry, Vector3.zero,
                WeaponVisualStyle.CompanionScale, Companion != null ? Companion.Slot : 0, ownerKey);
        }

        /// <summary>
        /// 旅伴胶囊根节点的缩放是 (0.7, 0.3, 0.7)（高度压扁了），直接把武器挂上去会被压成
        /// 一半高。这里放一个反向缩放的锚点，世界空间仍是 1:1，再在这上面挂 0.85 倍的武器。
        /// 锚点位置也按父级缩放换算，保证武器在身体右侧而不是被拉进胶囊。
        /// </summary>
        void BuildWeaponAnchor()
        {
            if (_weaponAnchor != null) return;

            Vector3 bodyScale = transform.localScale;
            float sx = Mathf.Abs(bodyScale.x) > 1e-4f ? bodyScale.x : 1f;
            float sy = Mathf.Abs(bodyScale.y) > 1e-4f ? bodyScale.y : 1f;
            float sz = Mathf.Abs(bodyScale.z) > 1e-4f ? bodyScale.z : 1f;

            Vector3 desired = WeaponVisualStyle.CompanionSlot;
            var go = new GameObject("WeaponAnchor");
            go.transform.SetParent(transform, false);
            go.transform.localScale = new Vector3(1f / sx, 1f / sy, 1f / sz);
            go.transform.localPosition = new Vector3(desired.x / sx, desired.y / sy, desired.z / sz);
            _weaponAnchor = go.transform;
        }

        /// <summary>写一个渲染帧的位置。alpha：0 = 本步起点，1 = 本步终点。</summary>
        public void Sync(float alpha)
        {
            if (Companion == null) return;

            Vector2 position = Vector2.Lerp(Companion.PrevPosition, Companion.Position, alpha);
            Vector3 p = transform.position;
            p.x = position.x;
            p.z = position.y;
            transform.position = p;
        }

        void ApplyColor(Color color)
        {
            if (_renderer == null || !_hasBaseColor) return;
            if (_applied && _appliedColor == color) return;

            _mpb.SetColor(BaseColorId, color);
            _renderer.SetPropertyBlock(_mpb);
            _appliedColor = color;
            _applied = true;
        }
    }
}
