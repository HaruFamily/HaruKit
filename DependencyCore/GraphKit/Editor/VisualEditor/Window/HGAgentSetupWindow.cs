namespace HaruFamily.DependencyCore.GraphKit.Editor
{
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

/// <summary>Optional bridge setup and project-local skill export; independent of MCP assemblies.</summary>
public sealed class HGAgentSetupWindow : EditorWindow
{
    private const string Bridge = "com.coplaydev.unity-mcp";
    private const string BridgeSource = "https://github.com/CoplayDev/unity-mcp.git?path=/MCPForUnity#81542a7e86292b5c4c21b1782c5af23b567f45ce";
    private AddRequest install;
    private int skillRoot;
    private string message;

    [MenuItem("Window/HaruGraph/AI and MCP Setup")]
    public static void Open() => GetWindow<HGAgentSetupWindow>("HaruGraph AI");

    private void OnDisable() => EditorApplication.update -= PollInstall;

    private void OnGUI()
    {
        var bridge = PackageInfo.GetAllRegisteredPackages().FirstOrDefault(package => package.name == Bridge);
        bool loaded = AppDomain.CurrentDomain.GetAssemblies().Any(assembly => assembly.GetName().Name == "HaruFamily.Tools.GraphMcp.Editor");
        EditorGUILayout.LabelField("Unity MCP", bridge == null ? "未安裝" : bridge.version);
        EditorGUILayout.LabelField("圖操作接入", loaded ? "已載入" : "未載入");
        if (!loaded)
            EditorGUILayout.HelpBox("MCP 接入需要 Unity 6000.3+、MCP for Unity 10.3.x 至 10.x，以及 Newtonsoft JSON 3.0.2 至 3.x。未啟用時仍可正常使用圖編輯器。", MessageType.Info);
        using (new EditorGUI.DisabledScope(bridge != null || install != null || EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode))
            if (GUILayout.Button("安裝 Unity MCP"))
            {
                install = Client.Add(BridgeSource);
                message = "正在安裝 Unity MCP…";
                EditorApplication.update += PollInstall;
            }
        EditorGUILayout.Space();
        skillRoot = EditorGUILayout.Popup("專案 skill 目錄", skillRoot, new[] { ".agents/skills", ".claude/skills" });
        if (GUILayout.Button("安裝操作 skill"))
        {
            try { message = "操作 skill：" + ExportSkill(Path.GetFullPath(Path.Combine(Application.dataPath, "..")), skillRoot == 0 ? ".agents/skills" : ".claude/skills"); }
            catch (Exception error) { message = error.Message; }
        }
        if (GUILayout.Button("開啟安裝與連線說明"))
            EditorUtility.RevealInFinder(Path.Combine(PackageRoot(), "Documentation~/Mcp/Installation.md"));
        EditorGUILayout.HelpBox("AI 客戶端連線由 AgentDock 或 MCP for Unity 管理；此視窗不改寫客戶端設定。", MessageType.None);
        if (!string.IsNullOrEmpty(message)) EditorGUILayout.HelpBox(message, MessageType.Info);
    }

    private void PollInstall()
    {
        if (install == null || !install.IsCompleted) return;
        message = install.Status == StatusCode.Success ? "Unity MCP 安裝完成；請依連線說明設定客戶端。" : "安裝失敗：" + install.Error?.message;
        install = null;
        EditorApplication.update -= PollInstall;
        Repaint();
    }

    private static string PackageRoot()
    {
        var package = PackageInfo.FindForAssembly(typeof(HGAgentSetupWindow).Assembly);
        if (package == null) throw new InvalidOperationException("找不到 GraphKit 套件位置。");
        return package.resolvedPath;
    }

    /// <summary>Exports the bundled skill without overwriting customized files or following linked directories.</summary>
    public static string ExportSkill(string projectPath, string skillDirectory)
    {
        if (skillDirectory != ".agents/skills" && skillDirectory != ".claude/skills")
            throw new ArgumentException("請選擇支援的專案 skill 目錄。", nameof(skillDirectory));
        string project = Path.GetFullPath(projectPath);
        if (!Directory.Exists(Path.Combine(project, "Assets")) || !Directory.Exists(Path.Combine(project, "ProjectSettings")))
            throw new ArgumentException("請選擇 Unity 專案根目錄。", nameof(projectPath));
        string directory = project;
        foreach (string part in (skillDirectory + "/harugraph-mcp").Split('/'))
        {
            directory = Path.Combine(directory, part);
            if ((Directory.Exists(directory) || File.Exists(directory)) && (File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Skill 目錄不可為連結：" + directory);
        }
        string target = Path.Combine(directory, "SKILL.md");
        byte[] content = File.ReadAllBytes(Path.Combine(PackageRoot(), "AgentSkill~/harugraph-mcp/SKILL.md"));
        if (File.Exists(target))
        {
            if ((File.GetAttributes(target) & FileAttributes.ReparsePoint) != 0 || !File.ReadAllBytes(target).SequenceEqual(content))
                throw new IOException("既有 skill 不同，未覆寫；請先檢查或合併：" + target);
            return target;
        }
        Directory.CreateDirectory(directory);
        using (var stream = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            stream.Write(content, 0, content.Length);
        return target;
    }
}
}
