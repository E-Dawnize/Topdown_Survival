using Game.Config;
using Game.Systems.Weapons;
using UnityEngine;

namespace Game.Views
{
    /// <summary>
    /// 一件武器 prefab 的运行时表现壳：负责瞄准、跟随锚点、攻击姿态和闪光。
    /// 模型不再由代码拼：prefab 里是导入的网格与材质，这个组件只操作根 Transform。
    ///
    /// 触发方式：每帧比较 WeaponModel.ShotsFired。近战在挥砍开始时 +1，远程在真正开火时 +1，
    /// 所以"射程内没目标就空挥/空放"不会播放动画。动画完全是表现层，不读伤害、不改判定。
    /// 弓另外带一个 NockedArrow：冷却末尾拉回弓上，ShotsFired 时收起，由 ProjectileView 接棒飞出去。
    /// </summary>
    public sealed class WeaponVisual : MonoBehaviour
    {
        const float FlashSeconds = 0.16f;

        /// <summary>弓在冷却最后这段时间里把搭箭拉回来；到 0 就是真正放箭。可在 Bow prefab 上按手感调。</summary>
        [SerializeField] float _bowDrawLeadSeconds = 0.28f;

        /// <summary>搭箭向后拉的距离（本地 Z）。可在 Bow prefab 上按手感调。</summary>
        [SerializeField] float _bowDrawPull = 0.20f;

        [SerializeField] Transform _flash;

        /// <summary>弓 prefab 上的搭箭子物体；其它武器为 null。</summary>
        [SerializeField] Transform _nockedArrow;

        /// <summary>开火后重新搭一支箭用的 prefab；Bow 指 Projectile_Arrow。</summary>
        [SerializeField] GameObject _nockedArrowPrefab;

        /// <summary>箭/弹道从哪个挂点接出去；Bow 用 NockedArrow，弩/灵球用 Flash。</summary>
        [SerializeField] Transform _muzzle;

        WeaponModel _model;
        WeaponAttackStyle _style;
        float _duration;
        int _phase;
        int _lastShots;
        int _ownerKey;

        float _attackTime;
        bool _attacking;

        Vector3 _basePosition;
        Quaternion _baseRotation;
        Vector3 _baseScale;
        Vector3 _flashBaseScale = Vector3.one;

        Vector3 _nockedBasePosition;
        Quaternion _nockedBaseRotation = Quaternion.identity;
        Vector3 _nockedBaseScale = Vector3.one;

        // 攻击范围显示由独立的 WeaponRangeIndicator 子类负责：近战扇形、远程目标锁定圈。
        // WeaponVisual 只创建它并在每帧把 attacking 状态转给它，不在这里写扇形的 if。
        WeaponRangeIndicator _rangeIndicator;

        public WeaponModel Model => _model;

        /// <summary>测试与调试用：这件武器的常驻范围显示。</summary>
        public WeaponRangeIndicator RangeIndicator => _rangeIndicator;

        public WeaponAttackStyle Style => _style;

        public float AttackDuration => _duration;

        public bool HasNockedArrow => _nockedArrow != null;

        public bool HasMuzzle => _nockedArrow != null || _muzzle != null;

        /// <summary>表现层枪口：弓优先用当前搭箭，弩/灵球用闪光前端。只用于视觉起点。</summary>
        public Vector3 MuzzleWorld => _nockedArrow != null
            ? _nockedArrow.position
            : _muzzle != null ? _muzzle.position : transform.position;

        /// <summary>
        /// 跟随层每帧写进来的新槽位（本地坐标）。攻击姿态仍然叠加在它上面，
        /// 所以武器可以一边拖尾一边挥砍。
        /// </summary>
        public void SetAnchor(Vector3 localPosition)
        {
            _basePosition = localPosition;
        }

        /// <summary>
        /// 从 catalog 的 prefab 实例化一件武器。prefab 必须自带 WeaponVisual 与闪光挂点：
        /// 缺资源时打错误并返回 null，不做"白模兜底"，否则美术资源断了也看不出来。
        /// </summary>
        public static WeaponVisual Attach(Transform parent, WeaponModel model, WeaponVisualCatalog.Entry entry,
            Vector3 localPosition, float scale, int phase, int ownerKey = 0)
        {
            if (parent == null || model == null) return null;
            if (entry == null || entry.Prefab == null)
            {
                Debug.LogError("[WeaponVisual] 武器 " + (model.Id ?? "?") +
                               " 没有视觉配置或 prefab；检查 Assets/Resources/WeaponVisualCatalog.asset");
                return null;
            }

            GameObject instance = Instantiate(entry.Prefab, parent, false);
            instance.name = "WeaponVisual_" + model.Id;
            instance.transform.localPosition = localPosition;
            instance.transform.localRotation = Quaternion.identity;
            instance.transform.localScale = Vector3.one * scale;

            WeaponVisual visual = instance.GetComponent<WeaponVisual>();
            if (visual == null)
            {
                Debug.LogError("[WeaponVisual] prefab " + entry.Prefab.name + " 上没有 WeaponVisual 组件");
                Destroy(instance);
                return null;
            }

            visual._ownerKey = ownerKey;
            visual.Initialize(model, entry, phase);
            return visual;
        }

        void Initialize(WeaponModel model, WeaponVisualCatalog.Entry entry, int phase)
        {
            _model = model;
            _style = entry.AttackStyle;
            _duration = entry.AttackDuration > 1e-4f ? entry.AttackDuration : 0.30f;
            _phase = phase;
            _basePosition = transform.localPosition;
            _baseRotation = transform.localRotation;
            _baseScale = transform.localScale;
            _lastShots = model.ShotsFired;

            // 升级期间刚拿到的武器可能在视觉壳挂上之前就出手了：只比较 ShotsFired 会吞掉第一刀。
            // 用模型里的 ShotAge 补播这一击；只在刚刚开火时补，不会让久未开火的武器凭空挥一下。
            if (model.ShotsFired > 0 && model.ShotAge <= 0.08f)
            {
                _attacking = true;
                _attackTime = Mathf.Min(model.ShotAge, _duration);
            }

            if (_flash != null)
            {
                _flashBaseScale = _flash.localScale;
                _flash.gameObject.SetActive(false);
            }

            if (_nockedArrow != null)
            {
                _nockedBasePosition = _nockedArrow.localPosition;
                _nockedBaseRotation = _nockedArrow.localRotation;
                _nockedBaseScale = _nockedArrow.localScale;
                _nockedArrow.gameObject.SetActive(false);
                TintNockedArrow();
            }

            _rangeIndicator = WeaponRangeIndicatorFactory.Attach(_model, transform, _ownerKey);
            UpdateRangeIndicator();
        }

        void Update()
        {
            if (_model == null) return;

            bool firedThisFrame = false;
            if (_model.ShotsFired != _lastShots)
            {
                _lastShots = _model.ShotsFired;
                _attacking = true;
                _attackTime = 0f;
                firedThisFrame = true;
            }

            if (_attacking)
            {
                _attackTime += Time.deltaTime;
                if (_attackTime >= _duration)
                {
                    _attacking = false;
                    _attackTime = 0f;
                }
            }

            ApplyPose();
            UpdateNockedArrow();
            UpdateFlash();
            UpdateRangeIndicator();

            if (firedThisFrame)
            {
                ProjectileMuzzleRegistry.Set(_ownerKey, MuzzleWorld);
                HandOffNockedArrow();
            }
        }

        /// <summary>
        /// 攻击窗口里的范围效果。近战只在伤害窗口 [MeleeWindowStart, MeleeWindowEnd + 0.06] 内显示，
        /// 与原来的亮弧同步；远程在冷却末尾拉弓窗口里显示目标身上的收拢圈。待机时线是关掉的。
        /// </summary>
        void UpdateRangeIndicator()
        {
            if (_rangeIndicator == null || _model == null) return;

            if (_model.Def.Kind == WeaponKind.Melee)
            {
                if (!_attacking)
                {
                    _rangeIndicator.Refresh(false, 0f);
                    return;
                }

                float start = _model.MeleeWindowStart;
                float end = _model.MeleeWindowEnd + 0.06f;
                bool visible = _attackTime >= start && _attackTime <= end;
                float t = Mathf.InverseLerp(start, end, _attackTime);
                float intensity = visible ? Mathf.Lerp(1f, 0.35f, t) : 0f;
                _rangeIndicator.Refresh(visible, intensity);
                return;
            }

            // 远程不是画射手周围的圆，而是画在锁定怪身上的收拢圈：
            // 冷却最后 _bowDrawLeadSeconds 秒从外圈收到目标身体边缘，开火那一帧消失。
            // 注意它由冷却驱动，不要求 _attacking：拉弓窗口本来就发生在出手之前。
            float lead = Mathf.Max(0.05f, _bowDrawLeadSeconds);
            float remaining = _model.CooldownRemaining;
            bool drawing = _model.HasTarget && remaining > 0f && remaining <= lead;
            float progress = drawing ? 1f - remaining / lead : 0f;
            _rangeIndicator.Refresh(drawing, progress);
        }

        void OnDestroy()
        {
            if (_rangeIndicator == null) return;

            if (Application.isPlaying) Destroy(_rangeIndicator.gameObject);
            else DestroyImmediate(_rangeIndicator.gameObject);
            _rangeIndicator = null;
        }

        /// <summary>
        /// 弓上的箭就是飞出去的那一支：开火时把当前 NockedArrow 从弓上摘下来交给投射物池，
        /// 再在弓上复制一支新的（下一发之前只需要拉弓，不需要重新生成飞行物）。
        /// </summary>
        void HandOffNockedArrow()
        {
            if (_nockedArrow == null) return;

            GameObject launched = _nockedArrow.gameObject;
            launched.transform.SetParent(null, true);
            launched.SetActive(false);
            ProjectileNockedHandoff.Set(_ownerKey, launched);
            _nockedArrow = null;

            CreateNockedArrow();
        }

        void CreateNockedArrow()
        {
            if (_nockedArrowPrefab == null) return;

            GameObject fresh = Instantiate(_nockedArrowPrefab, transform, false);
            fresh.name = "NockedArrow";
            fresh.transform.localPosition = _nockedBasePosition;
            fresh.transform.localRotation = _nockedBaseRotation;
            fresh.transform.localScale = _nockedBaseScale;
            fresh.SetActive(false);
            _nockedArrow = fresh.transform;
            TintNockedArrow();
        }

        /// <summary>搭箭和飞行箭共用 owner 的颜色，交接时不会出现同一支箭突然换色。</summary>
        void TintNockedArrow()
        {
            if (_nockedArrow == null) return;

            Color tint = _ownerKey == 0
                ? ProjectileViewSources.PlayerTint
                : ProjectileViewSources.CompanionTint;

            var block = new MaterialPropertyBlock();
            block.SetColor(Shader.PropertyToID("_BaseColor"), tint);
            Renderer[] renderers = _nockedArrow.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                Material material = renderers[i].sharedMaterial;
                if (material != null && material.HasProperty("_BaseColor"))
                    renderers[i].SetPropertyBlock(block);
            }
        }

        /// <summary>
        /// 武器模型指向最近一次真实瞄准/挥砍方向。父节点（玩家根节点或旅伴锚点）可能已经转了，
        /// 所以这里把世界朝向换算回父空间，避免武器被玩家移动方向带偏。
        /// 没有开过火时 AimDirection 是 zero，保持父节点朝向。
        /// </summary>
        Quaternion AimRotation()
        {
            Vector2 aim = _model != null ? _model.AimDirection : Vector2.zero;
            if (aim.sqrMagnitude < 1e-6f) return Quaternion.identity;

            float worldYaw = Mathf.Atan2(aim.x, aim.y) * Mathf.Rad2Deg;
            Transform parent = transform.parent;
            float parentYaw = parent != null ? parent.eulerAngles.y : 0f;
            return Quaternion.Euler(0f, worldYaw - parentYaw, 0f);
        }

        void ApplyPose()
        {
            Quaternion aim = AimRotation();

            if (!_attacking)
            {
                float bob = Mathf.Sin(Time.time * 2.1f + _phase * 1.7f) * 0.018f;
                Vector3 idlePosition = _basePosition + new Vector3(0f, bob, 0f);

                if (_style == WeaponAttackStyle.BowDraw)
                {
                    // 拉弓时弓身稍微后收、前倾一点；箭本身在 UpdateNockedArrow 里后拉。
                    float draw = NockedDraw();
                    idlePosition += new Vector3(0f, 0f, -0.05f * draw);
                    float bowSway = Mathf.Sin(Time.time * 1.4f + _phase * 2.1f) * 2.5f;
                    transform.localPosition = idlePosition;
                    transform.localRotation = aim * _baseRotation * Quaternion.Euler(-5f * draw, 0f, bowSway);
                    transform.localScale = _baseScale;
                    return;
                }

                if (_style == WeaponAttackStyle.OrbPulse)
                {
                    float spin = Time.time * 70f + _phase * 31f;
                    float pulse = 1f + Mathf.Sin(Time.time * 2.6f + _phase) * 0.035f;
                    transform.localPosition = idlePosition;
                    transform.localRotation = aim * _baseRotation * Quaternion.Euler(0f, spin, 0f);
                    transform.localScale = _baseScale * pulse;
                    return;
                }

                float sway = Mathf.Sin(Time.time * 1.4f + _phase * 2.1f) * 2.5f;
                transform.localPosition = idlePosition;
                transform.localRotation = aim * _baseRotation * Quaternion.Euler(0f, 0f, sway);
                transform.localScale = _baseScale;
                return;
            }

            float t = _duration > 1e-5f ? Mathf.Clamp01(_attackTime / _duration) : 1f;
            float eased = t * t * (3f - 2f * t);
            float swing = Mathf.Sin(t * Mathf.PI);

            Vector3 position = _basePosition;
            Quaternion rotation = aim * _baseRotation;
            Vector3 scale = _baseScale;

            switch (_style)
            {
                case WeaponAttackStyle.Slash:
                {
                    // 绕 Z 扫：竖直刀身会真正扫出一条屏幕上的弧线，而不是原地自转。
                    // 起止都回到 0，否则待机姿态接攻击姿态会在第一帧/最后一帧跳 78°。
                    float angle = Mathf.Sin(Mathf.Sqrt(t) * Mathf.PI) * 78f;
                    rotation = aim * _baseRotation * Quaternion.Euler(0f, 0f, angle);
                    position += new Vector3(0f, 0f, swing * 0.16f);
                    break;
                }
                case WeaponAttackStyle.DoubleSlash:
                {
                    // 一次挥砍里左右各扫一下，双匕的"快"要看得出来
                    float angle = Mathf.Sin(t * Mathf.PI * 2f) * 72f;
                    rotation = aim * _baseRotation * Quaternion.Euler(0f, 0f, angle);
                    position += new Vector3(0f, 0f, swing * 0.18f);
                    break;
                }
                case WeaponAttackStyle.Slam:
                {
                    float pitch = -Mathf.Sin(Mathf.Sqrt(t) * Mathf.PI) * 78f;
                    rotation = aim * _baseRotation * Quaternion.Euler(pitch, 0f, 0f);
                    position += new Vector3(0f, -swing * 0.12f, swing * 0.20f);
                    break;
                }
                case WeaponAttackStyle.BowDraw:
                {
                    float pull = Mathf.Sin(t * Mathf.PI);
                    rotation = aim * _baseRotation * Quaternion.Euler(-14f * pull, 0f, 0f);
                    position += new Vector3(0f, -0.02f * pull, -0.15f * pull);
                    scale = _baseScale * (1f - 0.045f * pull);
                    break;
                }
                case WeaponAttackStyle.CrossbowRecoil:
                {
                    float recoil = Mathf.Sin(t * Mathf.PI);
                    rotation = aim * _baseRotation * Quaternion.Euler(10f * recoil, 0f, 0f);
                    position += new Vector3(0f, 0f, -0.14f * recoil);
                    break;
                }
                case WeaponAttackStyle.OrbPulse:
                {
                    float pulse = Mathf.Sin(t * Mathf.PI);
                    rotation = aim * _baseRotation * Quaternion.Euler(0f, 360f * eased, 0f);
                    scale = _baseScale * (1f + 0.30f * pulse);
                    break;
                }
            }

            transform.localPosition = position;
            transform.localRotation = rotation;
            transform.localScale = scale;
        }

        /// <summary>
        /// 弓的预拉进度：冷却最后 <see cref="_bowDrawLeadSeconds"/> 秒从 0 拉到 1。
        /// 没有瞄准方向（还没索敌）时不拉，避免对着空气一直搭着箭。
        /// </summary>
        float NockedDraw()
        {
            if (_style != WeaponAttackStyle.BowDraw || _model == null) return 0f;
            if (_model.AimDirection.sqrMagnitude < 1e-8f) return 0f;

            float lead = Mathf.Max(0.01f, _bowDrawLeadSeconds);
            float remaining = _model.CooldownRemaining;
            if (remaining >= lead) return 0f;
            return 1f - Mathf.Clamp01(remaining / lead);
        }

        /// <summary>
        /// 搭箭只在"准备放但还没放"时可见：冷却末尾拉回来，ShotsFired 一变化就收起，
        /// 同一帧由投射物箭从射手位置飞出去，观感上就是弓把箭射走了。
        /// </summary>
        void UpdateNockedArrow()
        {
            if (_nockedArrow == null) return;

            float draw = _attacking ? 0f : NockedDraw();
            bool show = draw > 0.001f;
            if (_nockedArrow.gameObject.activeSelf != show) _nockedArrow.gameObject.SetActive(show);
            if (!show) return;

            _nockedArrow.localPosition = _nockedBasePosition + new Vector3(0f, 0f, -_bowDrawPull * draw);
            _nockedArrow.localRotation = _nockedBaseRotation;
            _nockedArrow.localScale = _nockedBaseScale;
        }

        void UpdateFlash()
        {
            if (_flash == null) return;

            bool show = _attacking && _attackTime < FlashSeconds;
            if (_flash.gameObject.activeSelf != show) _flash.gameObject.SetActive(show);
            if (!show) return;

            float k = 1f - Mathf.Clamp01(_attackTime / FlashSeconds);
            _flash.localScale = _flashBaseScale * (0.55f + 0.45f * k);
            _flash.localRotation = Quaternion.Euler(0f, Time.time * 720f, 0f);
        }
    }
}
