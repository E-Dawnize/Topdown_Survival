using Game.Services;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Game.Views
{
    /// <summary>
    /// 菜单/面板的输入读取。InputService 的 UI Map 负责正常路径；主菜单没有 InputService，
    /// 而测试或热重载时 Map 也可能还没切过来，所以同一套动作再补一份直接读设备的兜底。
    ///
    /// 只收"面板真正需要的四个动作"：确认、取消、重抽、水平导航。
    /// 面板可见时才调用，不会把边沿漏给模拟层。
    /// </summary>
    public static class MenuInput
    {
        public static bool Submit(InputService input)
        {
            if (input != null && input.UiSubmitPressed) return true;

            Keyboard keyboard = Keyboard.current;
            if (keyboard != null &&
                (keyboard.enterKey.wasPressedThisFrame || keyboard.spaceKey.wasPressedThisFrame ||
                 keyboard.numpadEnterKey.wasPressedThisFrame))
            {
                return true;
            }

            Gamepad gamepad = Gamepad.current;
            return gamepad != null &&
                   (gamepad.buttonSouth.wasPressedThisFrame || gamepad.startButton.wasPressedThisFrame);
        }

        public static bool Cancel(InputService input)
        {
            if (input != null && input.UiCancelPressed) return true;

            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame) return true;

            Gamepad gamepad = Gamepad.current;
            return gamepad != null && gamepad.buttonEast.wasPressedThisFrame;
        }

        public static bool Refresh(InputService input)
        {
            if (input != null && input.UiRefreshPressed) return true;

            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.rKey.wasPressedThisFrame) return true;

            Gamepad gamepad = Gamepad.current;
            return gamepad != null && gamepad.buttonNorth.wasPressedThisFrame;
        }

        /// <summary>水平导航：-1 左 / +1 右 / 0 无。优先读 InputService 的 UI Map，读不到再看设备。</summary>
        public static float NavigateX(InputService input)
        {
            if (input != null)
            {
                float axis = input.UiNavigate.x;
                if (Mathf.Abs(axis) >= 0.5f) return Mathf.Sign(axis);
            }

            Keyboard keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (keyboard.leftArrowKey.wasPressedThisFrame || keyboard.aKey.wasPressedThisFrame) return -1f;
                if (keyboard.rightArrowKey.wasPressedThisFrame || keyboard.dKey.wasPressedThisFrame) return 1f;
            }

            Gamepad gamepad = Gamepad.current;
            if (gamepad != null)
            {
                float stick = gamepad.leftStick.x.ReadValue();
                if (Mathf.Abs(stick) >= 0.5f) return Mathf.Sign(stick);
                if (gamepad.dpad.left.wasPressedThisFrame) return -1f;
                if (gamepad.dpad.right.wasPressedThisFrame) return 1f;
            }

            return 0f;
        }
    }
}
