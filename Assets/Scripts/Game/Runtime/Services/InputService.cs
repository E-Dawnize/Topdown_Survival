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

        public InputSnapshot Last { get; private set; }
        public Vector2 MoveAxis => Last.Move;
        public Vector2 AimScreenPosition => Last.Aim;
        public bool DashPressed => Last.DashPressed;
        public bool SkillPressed => Last.SkillPressed;
        public bool PausePressed => Last.PausePressed;
        public bool UiMode { get; private set; }

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

        /// <summary>进入 UI（商店/结算）时切 Map，避免战斗中按 E 触发界面。</summary>
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

            // I2 做商店时接 InputSystemUIInputModule，这里先把 Map 占住
            _uiMap = _asset.AddActionMap("UI");
            InputAction navigate = _uiMap.AddAction("Navigate", InputActionType.PassThrough, expectedControlLayout: "Vector2");
            navigate.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/upArrow")
                .With("Down", "<Keyboard>/downArrow")
                .With("Left", "<Keyboard>/leftArrow")
                .With("Right", "<Keyboard>/rightArrow");
            navigate.AddBinding("<Gamepad>/leftStick");
            _uiMap.AddAction("Submit", InputActionType.Button, "<Keyboard>/enter").AddBinding("<Gamepad>/buttonSouth");
            _uiMap.AddAction("Cancel", InputActionType.Button, "<Keyboard>/escape").AddBinding("<Gamepad>/buttonEast");
        }
    }
}
