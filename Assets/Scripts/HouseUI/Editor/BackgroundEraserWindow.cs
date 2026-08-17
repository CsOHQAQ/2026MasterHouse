using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;
using Process = System.Diagnostics.Process;

namespace MasterHouse.EditorTools
{
    /// <summary>
    /// 通过项目本地 Python 环境调用 ZhengPeng7/BiRefNet，把美术原图导出为透明前景或白色 Alpha 遮罩。
    /// 模型及依赖不进入 Unity Player，也不会增大游戏包体。
    /// </summary>
    public sealed class BackgroundEraserWindow : EditorWindow
    {
        private enum OutputMode
        {
            TransparentCutout,
            WhiteAlphaMask,
        }

        private enum ComputeDevice
        {
            Auto,
            Cuda,
            Cpu,
        }

        private const string ToolRelativePath = "Tools/BiRefNet";
        private const int MaxLogCharacters = 24000;

        [SerializeField] private Texture2D sourceAsset;
        [SerializeField] private string externalSourcePath;
        [SerializeField] private OutputMode outputMode;
        [SerializeField] private ComputeDevice computeDevice;

        private readonly object logLock = new object();
        private readonly StringBuilder processLog = new StringBuilder();
        private Process process;
        private string operation;
        private string pendingOutputPath;
        private string sourcePath;
        private string status;
        private Texture2D preview;
        private Vector2 logScroll;

        private static string ProjectRoot => Path.GetFullPath(Directory.GetCurrentDirectory());
        private static string ToolRoot => Path.Combine(ProjectRoot, ToolRelativePath.Replace('/', Path.DirectorySeparatorChar));
        private static string VenvPython => Path.Combine(ToolRoot, ".venv", "Scripts", "python.exe");
        private static string SetupScript => Path.Combine(ToolRoot, "setup_environment.py");
        private static string InferenceScript => Path.Combine(ToolRoot, "background_erase.py");

        [MenuItem("MasterHouse/美术工具/BiRefNet 背景擦除")]
        private static void Open()
        {
            var window = GetWindow<BackgroundEraserWindow>("BiRefNet 背景擦除");
            window.minSize = new Vector2(500f, 620f);
            window.Show();
        }

        private void OnEnable()
        {
            SyncSourcePath();
        }

        private void OnDisable()
        {
            EditorApplication.update -= PollProcess;
            StopProcess();
            DestroyPreview();
        }

        private void OnGUI()
        {
            EditorGUILayout.Space(6f);
            EditorGUILayout.HelpBox(
                "本工具固定使用 ZhengPeng7/BiRefNet 官方模型。首次需创建项目本地 Python 环境，并下载约 444 MB 模型权重；" +
                "之后可离线复用缓存。RTX 显卡建议 Auto/CUDA，CPU 也能运行但会明显更慢。",
                MessageType.Info);

            DrawEnvironmentSection();
            EditorGUILayout.Space(8f);
            DrawInputSection();
            EditorGUILayout.Space(8f);
            DrawRunSection();
            DrawPreview();
            DrawLog();
        }

        private void DrawEnvironmentSection()
        {
            EditorGUILayout.LabelField("运行环境", EditorStyles.boldLabel);
            using (new EditorGUILayout.VerticalScope("HelpBox"))
            {
                EditorGUILayout.LabelField("模型", "ZhengPeng7/BiRefNet");
                EditorGUILayout.LabelField("本地环境", File.Exists(VenvPython) ? "已创建" : "未安装");
                computeDevice = (ComputeDevice)EditorGUILayout.EnumPopup("计算设备", computeDevice);

                using (new EditorGUI.DisabledScope(IsRunning))
                {
                    if (GUILayout.Button(File.Exists(VenvPython) ? "检查/更新 BiRefNet 环境" : "安装 BiRefNet 环境", GUILayout.Height(25f)))
                        StartSetup();
                }
            }
        }

        private void DrawInputSection()
        {
            EditorGUILayout.LabelField("图片", EditorStyles.boldLabel);
            using (new EditorGUILayout.VerticalScope("HelpBox"))
            {
                EditorGUI.BeginChangeCheck();
                sourceAsset = (Texture2D)EditorGUILayout.ObjectField("项目内源图", sourceAsset, typeof(Texture2D), false);
                if (EditorGUI.EndChangeCheck())
                {
                    externalSourcePath = string.Empty;
                    SyncSourcePath();
                    DestroyPreview();
                }

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.PrefixLabel("外部源图");
                EditorGUILayout.SelectableLabel(externalSourcePath ?? string.Empty, EditorStyles.textField, GUILayout.Height(19f));
                if (GUILayout.Button("选择…", GUILayout.Width(62f))) SelectExternalSource();
                EditorGUILayout.EndHorizontal();

                outputMode = (OutputMode)EditorGUILayout.EnumPopup("导出内容", outputMode);
                EditorGUILayout.LabelField(
                    outputMode == OutputMode.TransparentCutout
                        ? "保留原图 RGB，把 BiRefNet 结果写入 Alpha。"
                        : "RGB 固定为白色，把 BiRefNet 结果写入 Alpha，适合 UICycleBlend 的 _MaskTex。",
                    EditorStyles.wordWrappedMiniLabel);
            }
        }

        private void DrawRunSection()
        {
            var canRun = !IsRunning && File.Exists(VenvPython) && File.Exists(InferenceScript) && !string.IsNullOrEmpty(sourcePath);
            using (new EditorGUI.DisabledScope(!canRun))
            {
                if (GUILayout.Button("用 BiRefNet 擦除背景并导出…", GUILayout.Height(32f)))
                    SelectOutputAndRun();
            }

            if (IsRunning)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("正在" + operation + "…", EditorStyles.boldLabel);
                if (GUILayout.Button("取消", GUILayout.Width(72f)))
                {
                    StopProcess();
                    status = "已取消。";
                }
                EditorGUILayout.EndHorizontal();
            }

            if (!string.IsNullOrEmpty(status))
                EditorGUILayout.HelpBox(status, status.StartsWith("失败", StringComparison.Ordinal) ? MessageType.Error : MessageType.None);
        }

        private void SelectExternalSource()
        {
            var initialDirectory = string.IsNullOrEmpty(externalSourcePath)
                ? ProjectRoot
                : Path.GetDirectoryName(externalSourcePath);
            var selected = EditorUtility.OpenFilePanelWithFilters(
                "选择要擦除背景的图片",
                initialDirectory,
                new[] { "图片", "png,jpg,jpeg,webp", "所有文件", "*" });
            if (string.IsNullOrEmpty(selected)) return;
            sourceAsset = null;
            externalSourcePath = selected;
            SyncSourcePath();
            DestroyPreview();
        }

        private void SelectOutputAndRun()
        {
            SyncSourcePath();
            if (string.IsNullOrEmpty(sourcePath)) return;
            var suffix = outputMode == OutputMode.WhiteAlphaMask ? "_mask" : "_cutout";
            var defaultName = Path.GetFileNameWithoutExtension(sourcePath) + suffix + ".png";
            var output = EditorUtility.SaveFilePanel("导出 BiRefNet 结果", Path.GetDirectoryName(sourcePath), defaultName, "png");
            if (string.IsNullOrEmpty(output)) return;

            DestroyPreview();
            pendingOutputPath = output;
            var arguments = JoinArguments(
                InferenceScript,
                "--input", sourcePath,
                "--output", output,
                "--mode", outputMode == OutputMode.WhiteAlphaMask ? "mask" : "cutout",
                "--device", DeviceArgument());
            StartProcess(VenvPython, arguments, "执行背景擦除");
        }

        private void StartSetup()
        {
            if (!File.Exists(SetupScript))
            {
                status = "失败：找不到环境安装脚本 " + SetupScript;
                return;
            }

            var arguments = JoinArguments(SetupScript, "--device", DeviceArgument());
            StartProcess("python", arguments, "安装环境");
        }

        private void StartProcess(string executable, string arguments, string operationName)
        {
            if (IsRunning) return;
            lock (logLock) processLog.Length = 0;
            status = string.Empty;
            operation = operationName;

            var startInfo = new ProcessStartInfo
            {
                FileName = executable,
                Arguments = arguments,
                WorkingDirectory = ProjectRoot,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };
            startInfo.EnvironmentVariables["PYTHONUTF8"] = "1";
            startInfo.EnvironmentVariables["PYTHONUNBUFFERED"] = "1";

            process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
            process.OutputDataReceived += (_, args) => AppendLog(args.Data);
            process.ErrorDataReceived += (_, args) => AppendLog(args.Data);
            try
            {
                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                EditorApplication.update -= PollProcess;
                EditorApplication.update += PollProcess;
            }
            catch (Exception exception)
            {
                process.Dispose();
                process = null;
                status = "失败：无法启动进程。" + exception.Message;
                Debug.LogException(exception);
            }
        }

        private void PollProcess()
        {
            Repaint();
            if (!IsRunning || !process.HasExited) return;

            EditorApplication.update -= PollProcess;
            process.WaitForExit();
            var exitCode = process.ExitCode;
            process.Dispose();
            process = null;

            if (exitCode != 0)
            {
                status = "失败：" + operation + "返回退出码 " + exitCode + "，详情见下方日志。";
                pendingOutputPath = string.Empty;
                return;
            }

            if (operation == "执行背景擦除" && !string.IsNullOrEmpty(pendingOutputPath) && File.Exists(pendingOutputPath))
            {
                TryImportAsset(pendingOutputPath);
                preview = LoadTexture(pendingOutputPath);
                status = "已完成并导出：" + pendingOutputPath;
                pendingOutputPath = string.Empty;
            }
            else
            {
                status = "BiRefNet 环境已就绪。首次推理时会下载模型权重。";
            }
        }

        private bool IsRunning => process != null;

        private void StopProcess()
        {
            EditorApplication.update -= PollProcess;
            if (process == null) return;
            try
            {
                if (!process.HasExited) process.Kill();
            }
            catch (Exception)
            {
                // 进程可能恰好自行退出。
            }
            process.Dispose();
            process = null;
            pendingOutputPath = string.Empty;
        }

        private void AppendLog(string line)
        {
            if (string.IsNullOrEmpty(line)) return;
            lock (logLock)
            {
                processLog.AppendLine(line);
                if (processLog.Length > MaxLogCharacters)
                    processLog.Remove(0, processLog.Length - MaxLogCharacters);
            }
        }

        private void DrawLog()
        {
            string log;
            lock (logLock) log = processLog.ToString();
            if (string.IsNullOrEmpty(log)) return;
            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("运行日志", EditorStyles.boldLabel);
            logScroll = EditorGUILayout.BeginScrollView(logScroll, GUILayout.MinHeight(90f), GUILayout.MaxHeight(180f));
            EditorGUILayout.SelectableLabel(log, EditorStyles.textArea, GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();
        }

        private void DrawPreview()
        {
            var texture = preview != null ? preview : sourceAsset;
            if (texture == null) return;
            EditorGUILayout.Space(8f);
            var available = Mathf.Max(120f, position.width - 24f);
            var aspect = texture.width / (float)Mathf.Max(1, texture.height);
            var height = Mathf.Min(280f, available / Mathf.Max(.01f, aspect));
            var rect = GUILayoutUtility.GetRect(available, height, GUILayout.ExpandWidth(true));
            DrawCheckerboard(rect);
            GUI.DrawTexture(rect, texture, ScaleMode.ScaleToFit, true);
        }

        private static void DrawCheckerboard(Rect rect)
        {
            EditorGUI.DrawRect(rect, new Color(.14f, .14f, .14f));
            const float cell = 12f;
            for (var y = rect.y; y < rect.yMax; y += cell)
            for (var x = rect.x; x < rect.xMax; x += cell)
                if ((((int)((x - rect.x) / cell)) + ((int)((y - rect.y) / cell))) % 2 == 0)
                    EditorGUI.DrawRect(new Rect(x, y, Mathf.Min(cell, rect.xMax - x), Mathf.Min(cell, rect.yMax - y)), new Color(.24f, .24f, .24f));
        }

        private void SyncSourcePath()
        {
            if (sourceAsset != null)
                sourcePath = AssetPathToAbsolute(AssetDatabase.GetAssetPath(sourceAsset));
            else
                sourcePath = File.Exists(externalSourcePath) ? externalSourcePath : string.Empty;
        }

        private string DeviceArgument()
        {
            switch (computeDevice)
            {
                case ComputeDevice.Cuda: return "cuda";
                case ComputeDevice.Cpu: return "cpu";
                default: return "auto";
            }
        }

        private static string JoinArguments(params string[] arguments)
        {
            var quoted = new List<string>(arguments.Length);
            foreach (var argument in arguments) quoted.Add(QuoteArgument(argument));
            return string.Join(" ", quoted);
        }

        private static string QuoteArgument(string argument)
        {
            if (string.IsNullOrEmpty(argument)) return "\"\"";
            // 当前传入的都是文件路径/固定枚举，不会以反斜杠结尾；保留 Windows 路径中的单个反斜杠。
            return "\"" + argument.Replace("\"", "\\\"") + "\"";
        }

        private static string AssetPathToAbsolute(string assetPath)
        {
            return string.IsNullOrEmpty(assetPath) ? string.Empty : Path.GetFullPath(Path.Combine(ProjectRoot, assetPath));
        }

        private static Texture2D LoadTexture(string path)
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false, false);
            if (texture.LoadImage(File.ReadAllBytes(path), false)) return texture;
            DestroyImmediate(texture);
            return null;
        }

        private void DestroyPreview()
        {
            if (preview == null) return;
            DestroyImmediate(preview);
            preview = null;
        }

        private static void TryImportAsset(string absolutePath)
        {
            var project = ProjectRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var full = Path.GetFullPath(absolutePath);
            if (!full.StartsWith(project + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return;
            var relative = full.Substring(project.Length + 1).Replace('\\', '/');
            if (!relative.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)) return;
            AssetDatabase.ImportAsset(relative, ImportAssetOptions.ForceUpdate);
            var imported = AssetDatabase.LoadAssetAtPath<Texture2D>(relative);
            Selection.activeObject = imported;
            EditorGUIUtility.PingObject(imported);
        }
    }
}
