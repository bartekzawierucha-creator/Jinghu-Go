using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace JianghuGo.Editor
{
    /// <summary>
    /// Headless Android build (CI: game-ci/unity-builder with buildMethod JianghuGo.Editor.BuildAndroid.Build).
    /// Creates the empty bootstrap scene (the game builds its UI in code, see AutoStart), applies player settings and
    /// writes a debug-signed DEVELOPMENT APK: development builds may talk to a LAN test server over http://.
    /// </summary>
    public static class BuildAndroid
    {
        private const string ScenePath = "Assets/Scenes/Bootstrap.unity";

        public static void Build()
        {
            Directory.CreateDirectory("Assets/Scenes");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };

            PlayerSettings.companyName = "BZ";
            PlayerSettings.productName = "Jianghu Go";
            PlayerSettings.bundleVersion = Environment.GetEnvironmentVariable("JIANGHU_VERSION") ?? "0.8.0";
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "pl.bz.jianghugo");
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel26;
            PlayerSettings.Android.forceInternetPermission = true;
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;
            // Cleartext HTTP is allowed at the OS level; UnityWebRequestTransport still refuses http:// in release builds.
            PlayerSettings.insecureHttpOption = InsecureHttpOption.AlwaysAllowed;
            EditorUserBuildSettings.buildAppBundle = false;

            var output = Argument("-customBuildPath") ?? "Builds/Android/JianghuGo.apk";
            if (!output.EndsWith(".apk", StringComparison.OrdinalIgnoreCase)) output = Path.Combine(output, "JianghuGo.apk");
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output)));

            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = output,
                target = BuildTarget.Android,
                options = BuildOptions.Development,
            });
            Debug.Log($"Android build {report.summary.result}: {output} ({report.summary.totalSize} bytes, {report.summary.totalErrors} errors)");
            if (report.summary.result != BuildResult.Succeeded) EditorApplication.Exit(1);
        }

        private static string Argument(string name)
        {
            var args = Environment.GetCommandLineArgs();
            var i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }
    }
}
