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
        InputAction _skill;
        InputAction _aim;
        InputAction _pause;
        InputAction _uiNavigate;
        InputAction _uiSubmit;
        InputAction _uiCancel;
        InputAction _uiRefresh;

        public InputSnapshot Last { get; private set; }
        public Vector2 MoveAxis => Last.Move;
        public Vector2 AimScreenPosition => Last.Aim;
        public bool DashPressed => Last.DashPressed;
        public bool SkillPressed => Last.SkillPressed;
        public bool PausePressed => Last.PausePressed;
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
            _playerMap?.Enable();
        }

        void OnDisable()
        {
            _playerMap?.Disable();
            _uiMap?.Disable();
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

        /// <summary>RunLoop 每帧调一次，返回本帧快照。</summary>
        public InputSnapshot Sample()
        {
            if (_move == null) return Last;

            Last = new InputSnapshot
            {
                Move = _move.ReadValue<Vector2>(),
                Aim = _aim.ReadValue<Vector2>(),
                DashPressed = _dash.WasPressedThisFrame(),
                SkillPressed = _skill.WasPressedThisFrame(),
                PausePressed = _pause.WasPressedThisFrame(),
            };
            return Last;
        }

        /// <summary>
        /// 在 Player 与 UI 两套 Map 之间切。同一时刻只启用一套：
        /// 商店开着还让 Player Map 活着的话，玩家会在买入的同一帧按出一次冲刺。
        /// </summary>
        public void SetUiMode(bool uiMode)
        {
            if (UiMode == uiMode) return;
            UiMode = uiMode;
            if (uiMode)
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

            _skill = _playerMap.AddAction("Skill", InputActionType.Button, "<Keyboard>/e");
            _skill.AddBinding("<Gamepad>/rightShoulder");

            _aim = _playerMap.AddAction("Aim", InputActionType.Value, expectedControlLayout: "Vector2");
            _aim.AddBinding("<Mouse>/position");
            _aim.AddBinding("<Gamepad>/rightStick");

            _pause = _playerMap.AddAction("Pause", InputActionType.Button, "<Keyboard>/escape");
            _pause.AddBinding("<Gamepad>/start");

            // 商店 UI 是自建的键盘/手柄界面（见 ShopView），不接 EventSystem 与 InputSystemUIInputModule，
            // 所以这里只放商店真正用到的四个动作。导航同时绑方向键与 WASD：
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
