using System.Diagnostics;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Game.EditorTools
{
    /// <summary>菜单入口：把 Excel 配置表导出成运行时 JSON。</summary>
    public static class ConfigMenu
    {
        [MenuItem("Tools/Game/导出配置表", priority = 30)]
        public static void ExportConfig()
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string scriptPath = Path.Combine(projectRoot, "tools", "export_config.py");
            if (!File.Exists(scriptPath))
            {
                Debug.LogError("[Config] 找不到 tools/export_config.py");
                return;
            }

            string[] candidates = { "python", "python3", "py" };
            foreach (string exe in candidates)
            {
                if (!TryRun(exe, "tools/export_config.py", projectRoot, out int exitCode, out string output))
                {
                    continue;
                }

                if (exitCode == 0)
                {
                    Debug.Log("[Config] 导表成功\n" + output);
                    AssetDatabase.Refresh();
                }
                else
                {
                    Debug.LogError("[Config] 导表失败\n" + output);
                }

                return;
            }

            Debug.LogError("[Config] 找不到 Python，请确认 python 在 PATH 里，或手动在项目根目录运行: python tools/export_config.py");
        }

        [MenuItem("Tools/Game/打开配置表", priority = 31)]
        public static void OpenExcel()
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string xlsx = Path.Combine(projectRoot, "Config", "GameConfig.xlsx");
            if (!File.Exists(xlsx))
            {
                Debug.LogError("[Config] 找不到 Config/GameConfig.xlsx");
                return;
            }

            EditorUtility.RevealInFinder(xlsx);
        }

        static bool TryRun(string exe, string arguments, string workDir, out int exitCode, out string output)
        {
            exitCode = -1;
            output = string.Empty;
            var sb = new StringBuilder();

            try
            {
                var psi = new ProcessStartInfo(exe, arguments)
                {
                    WorkingDirectory = workDir,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8,
                };

                using (Process p = Process.Start(psi))
                {
                    if (p == null) return false;
                    sb.Append(p.StandardOutput.ReadToEnd());
                    sb.Append(p.StandardError.ReadToEnd());
                    p.WaitForExit(30000);
                    exitCode = p.HasExited ? p.ExitCode : -1;
                }

                output = sb.Length > 8000 ? sb.ToString(0, 8000) + "\n…(输出被截断)" : sb.ToString();
                return true;
            }
            catch (System.ComponentModel.Win32Exception)
            {
                return false; // 这个 exe 不存在，换下一个
            }
            catch (System.Exception e)
            {
                output = e.Message;
                return true;
            }
        }
    }
}
