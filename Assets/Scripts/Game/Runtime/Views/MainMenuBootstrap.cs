using Game.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Game.Views
{
    /// <summary>
    /// 主菜单场景的入口。MainMenu 场景里只有一个相机和一个空的 MenuRoot，没有挂任何脚本——
    /// 而场景文件不手工改（场景由编辑器菜单生成，手改会在下次重建时被覆盖），
    /// 所以菜单改为运行时搭：挂一个"场景加载完成"的钩子，落到的场景是 MainMenu 就搭一块菜单。
    ///
    /// 用 sceneLoaded 而不是只在 RuntimeInitializeOnLoadMethod 里建一次：
    /// 从结算面板按 Esc 回主菜单会再 LoadScene("MainMenu") 一次，那时 RuntimeInitializeOnLoadMethod
    /// 已经不会再跑了（它只在进程启动时跑一次），菜单会缺一块。钩子在启动时挂一次，之后每次加载都走到。
    /// 启动时手动再调一次 OnSceneLoaded，是因为 AfterSceneLoad 阶段第一个场景的 sceneLoaded 已经发过了。
    ///
    /// 菜单不 DontDestroyOnLoad：它属于 MainMenu 场景，进 Run 时跟着场景一起销毁，
    /// 不需要"离开主菜单时记得关掉"这种额外约定。
    /// </summary>
    public static class MainMenuBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            // 先退订再订阅：编辑器里关掉 Domain Reload 时静态字段会活过两次 Play，
            // 不去重的话同一个场景会挂上两份回调、搭出两块菜单
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;

            OnSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.name != UnitySceneFlow.MainMenuScene) return;

            // 已经有一块就什么都不做：上面手动调的那一次与事件各来一遍时不会搭出两份
            if (Object.FindObjectOfType<MainMenuView>() != null) return;

            // 带 RectTransform 建：Overlay Canvas 的根节点就是一张全屏矩形，
            // 先建普通 Transform 再挂 Canvas 会被 Unity 换成 RectTransform，不如一开始就建对。
            // 不挂到场景里那个空的 MenuRoot 下面：Overlay Canvas 的大小由屏幕决定、与父节点无关，
            // 挂上去除了把菜单藏进一个看不见的空节点底下，没有任何好处。
            var go = new GameObject("MainMenuCanvas", typeof(RectTransform));
            go.AddComponent<MainMenuView>();
        }
    }
}
