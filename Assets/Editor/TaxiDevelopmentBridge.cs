using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEditor.TestTools.TestRunner.Api;

// Local, project-scoped development commands. No network listener or arbitrary code execution.
[InitializeOnLoad]
public static class TaxiDevelopmentBridge
{
    const string CommandPath = "Logs/TaxiCommand.txt";
    static double nextPoll;
    static TaxiDevelopmentBridge() { EditorApplication.update += Tick; }
    static void Tick()
    {
        if (EditorApplication.timeSinceStartup < nextPoll || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        nextPoll = EditorApplication.timeSinceStartup + 1;
        if (!File.Exists(CommandPath)) return;
        string command = File.ReadAllText(CommandPath).Trim();
        File.Delete(CommandPath);
        try
        {
            if (command == "inspect") Inspect();
            else if (command == "play") EditorApplication.isPlaying = true;
            else if (command == "stop") EditorApplication.isPlaying = false;
            else if (command == "save") EditorSceneManager.SaveOpenScenes();
            else if (command == "refresh") AssetDatabase.Refresh();
            else if (command == "test")
            {
                var runner = ScriptableObject.CreateInstance<TestRunnerApi>();
                runner.RegisterCallbacks(new Results());
                runner.Execute(new ExecutionSettings(new Filter { testMode = TestMode.EditMode, assemblyNames = new[] { "TaxiVR.Tests.EditMode" } }));
            }
            else if (command == "runtime-check")
            {
                var type = Type.GetType("TaxiVR.Playable.PlayableVerification, TaxiVR.Runtime", true);
                var root = GameObject.Find("Taxi VR - playable composition");
                if (!EditorApplication.isPlaying || root == null) throw new InvalidOperationException("Playable scene must be running.");
                root.AddComponent(type);
            }
            else if (command == "configure" || command == "build" || command == "verify")
            {
                if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play Mode first.");
                var type = Type.GetType("TaxiVR.Playable.Editor.PlayableBuilder, TaxiVR.Editor", true);
                type.GetMethod(command == "configure" ? "Configure" : command == "build" ? "Build" : "Verify").Invoke(null, null);
            }
            File.WriteAllText("Logs/TaxiCommandResult.txt", command + " OK " + DateTime.Now);
        }
        catch (Exception e) { File.WriteAllText("Logs/TaxiCommandResult.txt", command + " FAILED " + e); Debug.LogException(e); }
    }
    sealed class Results : ICallbacks
    {
        public void RunStarted(ITestAdaptor test) { }
        public void TestStarted(ITestAdaptor test) { }
        public void TestFinished(ITestResultAdaptor result) { }
        public void RunFinished(ITestResultAdaptor result)
        {
            TestRunnerApi.SaveResultToFile(result, "Logs/TaxiEditModeResults.xml");
            File.WriteAllText("Logs/TaxiEditModeSummary.txt", $"{result.ResultState}: {result.PassCount} passed; {result.FailCount} failed; {result.SkipCount} skipped\n{result.Message}");
        }
    }
    static void Inspect()
    {
        var text = new StringBuilder();
        text.AppendLine("PLAYING=" + EditorApplication.isPlaying);
        text.AppendLine($"RENDER drawCalls={UnityStats.drawCalls} triangles={UnityStats.triangles} vertices={UnityStats.vertices} renderers={UnityStats.visibleSkinnedMeshes}");
        foreach (var root in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
            text.AppendLine("ROOT " + root.name + " " + root.transform.position);
        foreach (var path in new[] {
            "Assets/ThirdParty/Vehicles/Models/Taxi_Full.fbx", "Assets/ThirdParty/Vehicles/Models/NormalCar1.fbx",
            "Assets/ThirdParty/DowntownCity/Models/Building_Small_1.fbx", "Assets/ThirdParty/DowntownCity/Models/Building_Medium_2_001.fbx",
            "Assets/ThirdParty/DowntownCity/Models/Building_Large_2.fbx", "Assets/ThirdParty/UniversalCharacters/Models/Superhero_Male_FullBody.fbx" })
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (asset == null) continue;
            var obj = UnityEngine.Object.Instantiate(asset);
            Bounds bounds = new Bounds(); bool first = true;
            foreach (var r in obj.GetComponentsInChildren<Renderer>()) { if (first) { bounds = r.bounds; first = false; } else bounds.Encapsulate(r.bounds); }
            text.AppendLine(path + " BOUNDS " + bounds);
            foreach (var t in obj.GetComponentsInChildren<Transform>().Take(160)) text.AppendLine("  " + t.name + " pos=" + t.position.ToString("F3") + " rot=" + t.eulerAngles.ToString("F1"));
            foreach (var r in obj.GetComponentsInChildren<Renderer>()) text.AppendLine("  MATERIALS " + string.Join(",", r.sharedMaterials.Select(m => m == null ? "null" : m.name + ":" + m.shader.name)));
            UnityEngine.Object.DestroyImmediate(obj);
        }
        var clips = AssetDatabase.LoadAllAssetsAtPath("Assets/ThirdParty/UniversalAnimations/Animations/UAL2_Standard.fbx").OfType<AnimationClip>();
        foreach (var clip in clips) text.AppendLine("CLIP " + clip.name + " " + clip.length);
        File.WriteAllText("Logs/TaxiAssetInspection.txt", text.ToString());
    }
}
