using Game.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Game.Services
{
    /// <summary>
    /// 唯一接触 InputSystem 的类。外部只读属性，不导入 UnityEngine.InputSystem。
    /// 输入在 RunLoop 的第 1 步采样，一帧一次。
    /// </summary>
    public sealed class InputService : MonoBehaviour
    {
        InputActionAsset _asset;
        InputActionMap _playerMap;
        InputActionMap _uiMap;
        InputAction _move;
        InputAction _dash;
        InputAction _collect;
        InputAction _pause;
        InputAction _uiNavigate;
        InputAction _uiSubmit;
        InputAction _uiCancel;
        InputAction _uiRefresh;

        readonly InputBuffer _buffer = new InputBuffer();

        public InputSnapshot Last { get; private set; }
        public Vector2 MoveAxis => Last.Move;
        public bool DashPressed => Last.DashPressed;
        public bool CollectPressed => Last.CollectPressed;

        /// <summary>游戏内暂停（Esc / 手柄 Start）。只用于战斗阶段的暂停入口，不进模拟快照。</summary>
        public bool PausePressed => _pause != null && _pause.WasPressedThisFrame();
        public bool UiMode { get; private set; }

        /// <summary>
        /// UI Map 的导航轴（方向键 / WASD / 左摇杆）。只在 UI Map 启用时（<see cref="SetUiMode"/>）有值。
        /// 不进 InputSnapshot：模拟层没有"UI 选了哪一格"这种概念，UI 面板每帧自己读一次就够，
        /// 塞进快照只会让模拟多一个永远用不上的字段。
        /// </summary>
        public Vector2 UiNavigate => _uiNavigate != null ? _uiNavigate.ReadValue<Vector2>() : Vector2.zero;

        public bool UiSubmitPressed => _uiSubmit != null && _uiSubmit.WasPressedThisFrame();
        public bool UiCancelPressed => _uiCancel != null && _uiCancel.WasPressedThisFrame();
        public bool UiRefreshPressed => _uiRefresh != null && _uiRefresh.WasPressedThisFrame();

        public InputActionAsset Asset => _asset;

        void Awake()
        {
            Build();
        }

        void OnEnable()
        {
            // 按当前模式恢复，而不是无条件开 Player Map。
            // 升级/结算面板打开时根对象被禁用再启用，UiMode 仍然是 true，
            // 这里必须恢复 UI Map，否则面板可见但按键没反应。
            ApplyMapState();
        }

        void OnDisable()
        {
            _playerMap?.Disable();
            _uiMap?.Disable();
            _buffer.Clear();
            Last = default;
        }

        void OnDestroy()
        {
            if (_asset != null)
            {
                _asset.Disable();
                Destroy(_asset);
                _asset = null;
            }
        }

        /// <summary>RunLoop 每帧调一次。边沿按钮进入缓冲，零步帧不会把它们清掉。</summary>
        public InputSnapshot Sample()
        {
            if (_move == null) return _buffer.Current;

            var edges = new InputSnapshot
            {
                Move = _move.ReadValue<Vector2>(),
                DashPressed = _dash.WasPressedThisFrame(),
                CollectPressed = _collect.WasPressedThisFrame(),
            };

            Last = _buffer.Capture(edges);
            return Last;
        }

        /// <summary>一个模拟步已经消费过按钮边沿，清掉锁存；零步帧不要调用。</summary>
        public void ConsumeSimulationStep()
        {
            _buffer.ConsumeSimulationStep();
            Last = _buffer.Current;
        }

        /// <summary>
        /// 在 Player 与 UI 两套 Map 之间切。同一时刻只启用一套：
        /// 升级面板开着还让 Player Map 活着的话，玩家会在选卡的同一帧按出一次冲刺。
        /// 切换时清掉旧模式下的按钮锁存，避免关面板后的下一步补触发一次冲刺。
        /// </summary>
        public void SetUiMode(bool uiMode)
        {
            if (UiMode == uiMode) return;
            UiMode = uiMode;
            _buffer.ConsumeSimulationStep();
            Last = _buffer.Current;
            ApplyMapState();
        }

        void ApplyMapState()
        {
            if (_playerMap == null || _uiMap == null) return;

            if (UiMode)
            {
                _playerMap.Disable();
                _uiMap.Enable();
            }
            else
            {
                _uiMap.Disable();
                _playerMap.Enable();
            }
        }

        void Build()
        {
            _asset = ScriptableObject.CreateInstance<InputActionAsset>();
            _asset.name = "GameInput";

            _playerMap = _asset.AddActionMap("Player");

            _move = _playerMap.AddAction("Move", InputActionType.Value, expectedControlLayout: "Vector2");
            _move.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/w")
                .With("Down", "<Keyboard>/s")
                .With("Left", "<Keyboard>/a")
                .With("Right", "<Keyboard>/d");
            _move.AddBinding("<Gamepad>/leftStick");

            _dash = _playerMap.AddAction("Dash", InputActionType.Button, "<Keyboard>/space");
            _dash.AddBinding("<Gamepad>/buttonSouth");

            // 全屏吸取经验：GDD 的 E/RB 主动技能位。只吸经验球，不吸心。
            _collect = _playerMap.AddAction("Collect", InputActionType.Button, "<Keyboard>/e");
            _collect.AddBinding("<Gamepad>/rightShoulder");

            // 暂停：Esc / 手柄 Start。暂停面板打开后 Player Map 会关闭，恢复走面板自己的输入。
            _pause = _playerMap.AddAction("Pause", InputActionType.Button, "<Keyboard>/escape");
            _pause.AddBinding("<Gamepad>/startButton");




            // 升级/结算 UI 是自建的键盘/手柄界面（见 UpgradeView / ResultView），不接 EventSystem。
            // 这里只放面板真正用到的四个动作。导航同时绑方向键与 WASD：
            // 键位与 Player Map 的移动键一致，玩家不用在两套键位之间换手指。
            _uiMap = _asset.AddActionMap("UI");

            _uiNavigate = _uiMap.AddAction("Navigate", InputActionType.PassThrough, expectedControlLayout: "Vector2");
            _uiNavigate.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/upArrow")
                .With("Down", "<Keyboard>/downArrow")
                .With("Left", "<Keyboard>/leftArrow")
                .With("Right", "<Keyboard>/rightArrow");
            _uiNavigate.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/w")
                .With("Down", "<Keyboard>/s")
                .With("Left", "<Keyboard>/a")
                .With("Right", "<Keyboard>/d");
            _uiNavigate.AddBinding("<Gamepad>/leftStick");

            _uiSubmit = _uiMap.AddAction("Submit", InputActionType.Button, "<Keyboard>/enter");
            _uiSubmit.AddBinding("<Keyboard>/space");
            _uiSubmit.AddBinding("<Gamepad>/buttonSouth");

            _uiCancel = _uiMap.AddAction("Cancel", InputActionType.Button, "<Keyboard>/escape");
            _uiCancel.AddBinding("<Gamepad>/buttonEast");

            _uiRefresh = _uiMap.AddAction("Refresh", InputActionType.Button, "<Keyboard>/r");
            _uiRefresh.AddBinding("<Gamepad>/buttonNorth");
        }
    }
}
