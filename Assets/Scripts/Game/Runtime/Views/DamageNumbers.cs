using System.Collections.Generic;
using UnityEngine;

namespace Game.Views
{
    /// <summary>
    /// 世界空间的伤害飘字。敌人受击与玩家受击都走这里：数字从命中点向上飘、淡出；
    /// 暴击用更大的字与橙色，并带“暴击”前缀，玩家不用看血条也能分辨这一下是不是暴击。
    ///
    /// 用 TextMesh 而不是给每只怪建世界 Canvas：TextMesh 只是网格 + 字体材质，
    /// 同屏几十个飘字不会各自建 Canvas/EventSystem。数字内容只有 ASCII 数字与固定中文前缀，
    /// 字体来自 UiFont 的动态系统字体，和 HUD 用同一份字形图集。
    /// </summary>
    public static class DamageNumbers
    {
        const int MaxNumbers = 80;
        const float NormalLife = 0.75f;
        const float CritLife = 0.95f;

        static readonly List<Entry> _entries = new List<Entry>(MaxNumbers);
        static readonly Color NormalColor = new Color(1f, 0.93f, 0.72f, 1f);
        static readonly Color CritColor = new Color(1f, 0.58f, 0.16f, 1f);
        static readonly Color IncomingColor = new Color(1f, 0.35f, 0.30f, 1f);

        static Transform _root;
        static Font _font;
        static int _next;
        static int _serial;

        /// <summary>
        /// 显示一个伤害数字。incoming = true 表示玩家自己挨打，用红色负号；
        /// crit 只对打出去的伤害有意义，会放大并变成橙色。
        /// </summary>
        public static void Show(Vector3 worldPosition, float amount, bool crit, bool incoming = false)
        {
            int value = Mathf.Max(1, Mathf.CeilToInt(amount));
            EnsureRoot();
            if (_font == null) return;

            Entry entry = Rent();
            if (entry == null) return;

            string content;
            Color color;
            float characterSize;

            if (incoming)
            {
                content = "-" + value;
                color = IncomingColor;
                characterSize = 0.09f;
            }
            else if (crit)
            {
                content = "暴击 " + value;
                color = CritColor;
                characterSize = 0.115f;
            }
            else
            {
                content = value.ToString();
                color = NormalColor;
                characterSize = 0.075f;
            }

            // 同一帧多个目标同时挨打时错开一点，避免数字完全重叠。
            float phase = _serial++ * 2.399963f;
            Vector3 start = worldPosition + new Vector3(Mathf.Cos(phase) * 0.18f, 0f,
                Mathf.Sin(phase) * 0.18f);

            entry.Play(start, content, color, characterSize, crit || incoming ? CritLife : NormalLife);
        }

        /// <summary>场景卸载时由 AppRoot 调：销毁根节点并清掉静态引用。</summary>
        public static void Clear()
        {
            _entries.Clear();
            _next = 0;
            _serial = 0;

            if (_root != null)
            {
                Object.Destroy(_root.gameObject);
                _root = null;
            }
        }

        static void EnsureRoot()
        {
            if (_root != null) return;

            _root = new GameObject("[DamageNumbers]").transform;
            _font = UiFont.Resolve();
            _entries.Clear();
            _next = 0;
        }

        static Entry Rent()
        {
            for (int i = 0; i < _entries.Count; i++)
            {
                int index = (_next + i) % _entries.Count;
                Entry candidate = _entries[index];
                if (candidate == null || candidate.IsPlaying) continue;

                _next = (index + 1) % _entries.Count;
                return candidate;
            }

            if (_entries.Count < MaxNumbers)
            {
                Entry created = Create();
                if (created != null) _entries.Add(created);
                return created;
            }

            // 满了就抢最老的一条；混战时偶尔丢一条旧数字，比无限建对象便宜。
            Entry oldest = _entries[_next];
            _next = (_next + 1) % _entries.Count;
            return oldest;
        }

        static Entry Create()
        {
            var go = new GameObject("DamageNumber");
            go.transform.SetParent(_root, false);

            var text = go.AddComponent<TextMesh>();
            if (_font != null)
            {
                text.font = _font;
                text.fontSize = 64;
            }

            text.anchor = TextAnchor.MiddleCenter;
            text.alignment = TextAlignment.Center;
            text.characterSize = 0.075f;

            var renderer = go.GetComponent<MeshRenderer>();
            if (renderer == null) renderer = go.AddComponent<MeshRenderer>();
            if (_font != null && _font.material != null) renderer.sharedMaterial = _font.material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            var entry = go.AddComponent<Entry>();
            entry.Bind(text);
            go.SetActive(false);
            return entry;
        }

        sealed class Entry : MonoBehaviour
        {
            TextMesh _text;
            Color _color;
            Vector3 _start;
            float _age;
            float _life;
            bool _playing;

            public bool IsPlaying => _playing;

            public void Bind(TextMesh text)
            {
                _text = text;
            }

            public void Play(Vector3 start, string content, Color color, float characterSize, float life)
            {
                _start = start;
                _color = color;
                _age = 0f;
                _life = Mathf.Max(0.05f, life);
                _playing = true;

                transform.position = start;
                transform.localScale = Vector3.one;
                gameObject.SetActive(true);

                if (_text != null)
                {
                    _text.text = content;
                    _text.characterSize = characterSize;
                    _text.color = color;
                }

                FaceCamera();
            }

            void Update()
            {
                if (!_playing) return;

                _age += Time.deltaTime;
                float t = Mathf.Clamp01(_age / _life);
                if (t >= 1f)
                {
                    _playing = false;
                    gameObject.SetActive(false);
                    return;
                }

                transform.position = _start + Vector3.up * (1.10f * t);

                // 出现的第一瞬间弹一下，之后回到常速上飘。
                float pop = 1f + 0.28f * Mathf.Max(0f, 1f - t * 4f);
                transform.localScale = Vector3.one * pop;

                Color color = _color;
                color.a = t < 0.55f ? 1f : 1f - (t - 0.55f) / 0.45f;
                if (_text != null) _text.color = color;

                FaceCamera();
            }

            void FaceCamera()
            {
                Camera camera = Camera.main;
                if (camera == null) return;

                Vector3 toCamera = transform.position - camera.transform.position;
                if (toCamera.sqrMagnitude < 1e-6f) return;
                transform.rotation = Quaternion.LookRotation(toCamera);
            }
        }
    }
}
