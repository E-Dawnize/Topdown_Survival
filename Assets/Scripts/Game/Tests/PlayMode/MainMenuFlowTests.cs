using System.Collections;
using Game.Core;
using Game.Views;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Game.Tests
{
    /// <summary>
    /// 从主菜单进 Run、死亡结算、回主菜单/重开的整条场景流程。
    /// 这些用例比模拟层测试慢，但它们验的是 AppRoot/MainMenuBootstrap/ResultView 的真实生命周期：
    /// 菜单是不是运行时真的搭出来了、LoadScene 后 AppRoot 有没有重新装配、结算面板能不能切回主菜单。
    /// </summary>
    public class MainMenuFlowTests
    {
        [UnityTest]
        public IEnumerator MainMenuBuildsAndStartsRun()
        {
            yield return LoadScene(UnitySceneFlow.MainMenuScene);

            MainMenuView menu = Object.FindObjectOfType<MainMenuView>();
            Assert.IsNotNull(menu, "MainMenu 场景加载后应自动搭出菜单");
            Assert.IsNotNull(menu.GetComponent<Canvas>(), "菜单应挂 Overlay Canvas");

            menu.StartRun();
            yield return WaitForScene(UnitySceneFlow.RunScene);

            AppRoot root = Object.FindObjectOfType<AppRoot>();
            Assert.IsNotNull(root, "进入 Run 后应存在 AppRoot");
            Assert.IsNotNull(Object.FindObjectOfType<RunLoop>(), "Run 场景应存在 RunLoop");
            Assert.IsNotNull(AppRoot.Instance, "AppRoot.Instance 应已装配");
            Assert.AreEqual(RunPhase.Battle, root.RunController.Phase, "进入 Run 后应立即开始战斗");
            Assert.IsNotNull(root.Player, "玩家模型应已创建");
            Assert.IsNotNull(root.Hud, "HUD 应已创建");
        }

        [UnityTest]
        public IEnumerator LevelUpPanelPausesAndResumesTheRun()
        {
            yield return LoadScene(UnitySceneFlow.MainMenuScene);
            MainMenuView menu = Object.FindObjectOfType<MainMenuView>();
            Assert.IsNotNull(menu);
            menu.StartRun();
            yield return WaitForScene(UnitySceneFlow.RunScene);

            AppRoot root = Object.FindObjectOfType<AppRoot>();
            Assert.IsNotNull(root);

            // 直接补一级经验：不依赖真实刷怪节奏，只验证暂停/选卡/恢复这条流程。
            root.Run.AddExperience(root.Run.XpToNext);
            yield return WaitUntil(() => root.RunController.Phase == RunPhase.LevelUp, 120);

            UpgradeView upgrade = Object.FindObjectOfType<UpgradeView>();
            Assert.IsNotNull(upgrade, "升级后应存在选卡面板");
            Assert.IsTrue(upgrade.IsVisible, "升级暂停时选卡面板应显示");
            Assert.Greater(root.Build.Options.Count, 0, "选卡面板应有候选卡");

            Assert.IsTrue(root.RunController.ChooseUpgrade(0), "应能选中第一张卡");
            yield return WaitUntil(() => root.RunController.Phase == RunPhase.Battle, 120);
            yield return WaitUntil(() => !upgrade.IsVisible, 60);
            Assert.IsFalse(upgrade.IsVisible, "选卡后应关闭面板并回到战斗");
        }

        [UnityTest]
        public IEnumerator PauseFreezesRunAndCanReturnToMainMenu()
        {
            yield return LoadScene(UnitySceneFlow.MainMenuScene);
            MainMenuView menu = Object.FindObjectOfType<MainMenuView>();
            Assert.IsNotNull(menu);
            menu.StartRun();
            yield return WaitForScene(UnitySceneFlow.RunScene);

            AppRoot root = Object.FindObjectOfType<AppRoot>();
            Assert.IsNotNull(root);
            Assert.IsNotNull(root.PauseUi, "Run 场景应创建暂停面板");

            root.RunController.Pause();
            yield return WaitUntil(() => root.PauseUi.IsVisible, 120);
            Assert.AreEqual(RunPhase.Paused, root.RunController.Phase);
            Assert.IsTrue(root.Input.UiMode, "暂停面板应切换到 UI 输入");

            float frozenElapsed = root.RunController.RunElapsed;
            yield return null;
            yield return null;
            Assert.AreEqual(frozenElapsed, root.RunController.RunElapsed, 1e-5f, "暂停期间游戏时间不应推进");

            root.PauseUi.ResumeGame();
            yield return WaitUntil(() => root.RunController.Phase == RunPhase.Battle, 120);
            Assert.IsFalse(root.PauseUi.IsVisible, "继续游戏后暂停面板应关闭");
            Assert.IsFalse(root.Input.UiMode, "继续游戏后应切回 Player 输入");

            root.RunController.Pause();
            yield return WaitUntil(() => root.PauseUi.IsVisible, 120);
            root.PauseUi.ReturnToMainMenu();
            yield return WaitForScene(UnitySceneFlow.MainMenuScene);
            Assert.IsNotNull(Object.FindObjectOfType<MainMenuView>(), "暂停菜单应能返回主菜单");
        }

        [UnityTest]
        public IEnumerator DeathShowsResultAndEscapeReturnsToMainMenu()
        {
            yield return LoadScene(UnitySceneFlow.MainMenuScene);
            MainMenuView menu = Object.FindObjectOfType<MainMenuView>();
            Assert.IsNotNull(menu);
            menu.StartRun();
            yield return WaitForScene(UnitySceneFlow.RunScene);

            AppRoot root = Object.FindObjectOfType<AppRoot>();
            Assert.IsNotNull(root);

            root.Player.HP = 0f;
            yield return WaitUntil(() => root.RunController.Phase == RunPhase.GameOver, 300);

            ResultView result = Object.FindObjectOfType<ResultView>();
            Assert.IsNotNull(result, "死亡后应存在结算面板");
            Assert.IsTrue(result.IsVisible, "死亡后结算面板应显示");

            result.ReturnToMainMenu();
            yield return WaitForScene(UnitySceneFlow.MainMenuScene);

            Assert.IsNotNull(Object.FindObjectOfType<MainMenuView>(), "Esc 应回到主菜单");
            Assert.IsNull(AppRoot.Instance, "回主菜单后旧 AppRoot 应已销毁");
        }

        [UnityTest]
        public IEnumerator ResultEnterRestartsARun()
        {
            yield return LoadScene(UnitySceneFlow.MainMenuScene);
            MainMenuView menu = Object.FindObjectOfType<MainMenuView>();
            Assert.IsNotNull(menu);
            menu.StartRun();
            yield return WaitForScene(UnitySceneFlow.RunScene);

            AppRoot first = Object.FindObjectOfType<AppRoot>();
            Assert.IsNotNull(first);
            first.Player.HP = 0f;
            yield return WaitUntil(() => first.RunController.Phase == RunPhase.GameOver, 300);

            ResultView result = Object.FindObjectOfType<ResultView>();
            Assert.IsNotNull(result);
            Assert.IsTrue(result.IsVisible);

            result.RestartRun();
            yield return WaitUntil(() => Object.FindObjectOfType<AppRoot>() != first, 300);

            AppRoot second = Object.FindObjectOfType<AppRoot>();
            Assert.IsNotNull(second, "Enter 应重载 Run 场景");
            Assert.AreNotSame(first, second, "重开应重新装配 AppRoot，而不是复用旧对象");
            Assert.AreEqual(RunPhase.Battle, second.RunController.Phase);
        }

        static IEnumerator LoadScene(string name)
        {
            SceneManager.LoadScene(name, LoadSceneMode.Single);
            yield return WaitForScene(name);
        }

        static IEnumerator WaitForScene(string name)
        {
            for (int i = 0; i < 300; i++)
            {
                if (SceneManager.GetActiveScene().name == name)
                {
                    yield return null;   // 让新场景的 Awake/OnEnable 与 FirstFrame 跑完
                    yield break;
                }
                yield return null;
            }

            Assert.Fail("场景没有在限定帧内加载完成：" + name);
        }

        static IEnumerator WaitUntil(System.Func<bool> condition, int maxFrames)
        {
            for (int i = 0; i < maxFrames; i++)
            {
                if (condition()) yield break;
                yield return null;
            }

            Assert.Fail("条件没有在限定帧内满足");
        }

    }
}
