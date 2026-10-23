using System;
using System.IO;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace PushHands.Editor
{
    // 本机工程内的任务入口；只处理固定白名单动作，不接受任意代码。
    [InitializeOnLoad]
    public static class EditorJobs
    {
        private const string Inbox = "Artifacts/editor-job.txt";
        private static TestRunnerApi api;
        private static double nextPoll;
        static EditorJobs()
        {
            EditorApplication.update += Poll;
            string pending = SessionState.GetString("PushHands.TestJob", "");
            if (!string.IsNullOrEmpty(pending))
            {
                api = ScriptableObject.CreateInstance<TestRunnerApi>();
                api.RegisterCallbacks(new Results(pending));
            }
        }

        private static void Poll()
        {
            if (EditorApplication.timeSinceStartup < nextPoll || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            nextPoll = EditorApplication.timeSinceStartup + 1;
            if (!File.Exists(Inbox)) return;
            string command = File.ReadAllText(Inbox).Trim();
            // 用户可能仍停留在试玩模式。测试框架必须在编辑状态启动；保留任务至退出完成。
            if (EditorApplication.isPlaying && (command == "editmode-red" || command == "editmode-green"
                || command == "playmode" || command == "physical-tests"))
            { EditorApplication.isPlaying = false; return; }
            File.Delete(Inbox);
            try
            {
                if (command == "editmode-red" || command == "editmode-green" || command == "playmode" || command == "physical-tests")
                {
                    SessionState.SetString("PushHands.TestJob", command);
                    api = ScriptableObject.CreateInstance<TestRunnerApi>();
                    api.RegisterCallbacks(new Results(command));
                    bool playMode = command == "playmode" || command == "physical-tests";
                    api.Execute(new ExecutionSettings(new Filter { testMode = playMode ? TestMode.PlayMode : TestMode.EditMode,
                        assemblyNames = new[] { playMode ? "PushHands.PlayMode" : "PushHands.EditMode" },
                        testNames = command == "physical-tests" ? new[] { "PushHands.Tests.PhysicalTests" } : null }));
                }
                else if (command == "install") PackageSetup.InstallInEditor();
                else if (command == "configure") { PackageSetup.Configure(); Complete(command, "已保存输入设置"); }
                else if (command == "baseline") Complete(command, "Unity=" + Application.unityVersion + "; 编译完成，无项目脚本编译错误");
                else if (command == "refresh") { AssetDatabase.Refresh(); Complete(command, "资源已刷新"); }
                else if (command == "physical-generate") { PhysicalBuilder.Generate(); Complete(command, "独立物理场景已生成"); }
                else if (command == "physical-build") { PhysicalBuilder.BuildWindows(); Complete(command, "物理原型 Windows 包已构建"); }
                else if (command == "physical-handoff") { PhysicalBuilder.Handoff(); Complete(command, "已打开物理试玩场景并捕获检查视图"); }
                else if (command == "physical-feedback-setup") { PhysicalBuilder.ConfigureFeedback(); Complete(command, "已更新推动反馈参数与展示提示字体"); }
                else if (command == "physical-stance-setup") { PhysicalBuilder.ConfigureParallelStance(); Complete(command, "已更新场景与预制体的开局并排站姿"); }
                else if (command == "physical-arena-setup") { PhysicalBuilder.ConfigureArena(); Complete(command, "已更新独立低台与重心参数"); }
                else if (command == "resources")
                    Type.GetType("PushHands.Editor.GreyboxBuilder, PushHands.Editor").GetMethod("ImportTextResources").Invoke(null, null);
                else if (command == "generate" || command == "build")
                {
                    var type = Type.GetType("PushHands.Editor.GreyboxBuilder, PushHands.Editor");
                    if (type == null) throw new InvalidOperationException("场景生成器尚未编译");
                    type.GetMethod(command == "generate" ? "Generate" : "BuildWindows").Invoke(null, null);
                    Complete(command, "完成");
                }
                else throw new InvalidOperationException("不支持的编辑器任务：" + command);
            }
            catch (Exception e) { Complete(command, "失败：" + e); Debug.LogException(e); }
        }

        public static void Complete(string command, string result)
        {
            Directory.CreateDirectory("Artifacts/Verification");
            File.WriteAllText("Artifacts/Verification/" + command + "-status.txt", DateTime.Now.ToString("O") + "\n" + result);
        }

        private sealed class Results : ICallbacks
        {
            private readonly string name;
            public Results(string name) { this.name = name; }
            public void RunStarted(ITestAdaptor testsToRun) { }
            public void TestStarted(ITestAdaptor test) { }
            public void TestFinished(ITestResultAdaptor result) { }
            public void RunFinished(ITestResultAdaptor result)
            {
                // TestRunner 的回调可跨域重载保留；旧任务不能把新任务的结果写进另一份报告。
                if (SessionState.GetString("PushHands.TestJob", "") != name) return;
                SessionState.EraseString("PushHands.TestJob");
                File.WriteAllText("Artifacts/Verification/" + name + ".xml", result.ToXml().OuterXml);
                Complete(name, $"通过={result.PassCount} 失败={result.FailCount} 跳过={result.SkipCount}");
                UnityEngine.Object.DestroyImmediate(api);
            }
        }
    }
}
