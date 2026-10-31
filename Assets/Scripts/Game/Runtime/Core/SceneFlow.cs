using UnityEngine;
using UnityEngine.SceneManagement;

namespace Game.Core
{
    /// <summary>
    /// 切换场景的唯一入口。抽成接口是为了让"结算面板按下 Enter / Esc 之后发生什么"能被替身记录下来，
    /// 不必真的在编辑器里重载场景。
    /// </summary>
    public interface ISceneFlow
    {
        /// <summary>重开当前场景。</summary>
        void ReloadCurrent();

        /// <summary>回主菜单。</summary>
        void LoadMainMenu();
    }

    /// <summary>
    /// 用 SceneManager 实现的场景切换。
    ///
    /// 重开一局走场景重载而不是手工清场：玩家、敌人、掉落物、三套视图池、HUD、商店面板全都挂在场景上，
    /// 重载一次等于把它们连同事件总线一起丢掉重来，比"逐个 Reset 每个系统"少一整套可能漏项的代码。
    /// AppRoot 在 OnDestroy 里把事件总线 Clear 掉、把 Instance 置空，新场景的装配根因此不会被误判成重复。
    ///
    /// 两个可玩场景（Run / Test_Arena）都用当前场景名重载：写死其中任何一个，从另一个场景死掉就会跳到别的地图。
    /// </summary>
    public sealed class UnitySceneFlow : ISceneFlow
    {
        /// <summary>主菜单场景名。必须在 Build Settings 里（见 ProjectSettings/EditorBuildSettings.asset）。</summary>
        public const string MainMenuScene = "MainMenu";

        /// <summary>主菜单按开始之后进去的那一局。同样必须在 Build Settings 里。</summary>
        public const string RunScene = "Run";

        public void ReloadCurrent()
        {
            Scene active = SceneManager.GetActiveScene();

            // 空场景名说明这个场景是运行中临时建的、不在 Build Settings 里：此时 LoadScene 会抛异常，
            // 报一句能看懂的日志比让玩家卡在结算面板上强
            if (string.IsNullOrEmpty(active.name))
            {
                Debug.LogError("[SceneFlow] 当前场景没有名字（不在 Build Settings 里？），无法重开");
                return;
            }

            SceneManager.LoadScene(active.name);
        }

        public void LoadMainMenu()
        {
            SceneManager.LoadScene(MainMenuScene);
        }
    }
}
