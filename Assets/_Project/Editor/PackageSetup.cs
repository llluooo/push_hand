using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;

namespace PushHands.Editor
{
    public static class PackageSetup
    {
        private static AddRequest request;
        private static double deadline;
        private static bool exitOnFinish;

        public static void Install()
        {
            exitOnFinish = true;
            InstallInEditor();
        }

        public static void InstallInEditor()
        {
            request = Client.Add("com.unity.inputsystem@1.7.0");
            deadline = EditorApplication.timeSinceStartup + 300;
            EditorApplication.update += Poll;
        }

        private static void Poll()
        {
            if (!request.IsCompleted && EditorApplication.timeSinceStartup < deadline) return;
            EditorApplication.update -= Poll;
            if (!request.IsCompleted || request.Status != StatusCode.Success)
            {
                Debug.LogError("输入包安装失败：" + request.Error?.message);
                EditorJobs.Complete("install", "失败：" + request.Error?.message);
                if (exitOnFinish) EditorApplication.Exit(1);
                return;
            }
            Debug.Log("输入包已安装：" + request.Result.version);
            EditorJobs.Complete("install", "已安装 " + request.Result.version);
            if (exitOnFinish) EditorApplication.Exit(0);
        }

        public static void Configure()
        {
            var settings = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset")[0]);
            settings.FindProperty("activeInputHandler").intValue = 1;
            settings.ApplyModifiedPropertiesWithoutUndo();
            Time.fixedDeltaTime = 0.02f;
            AssetDatabase.SaveAssets();
            Debug.Log("已启用 Input System，固定模拟步长 0.02 秒；下次启动生效。");
        }
    }
}
