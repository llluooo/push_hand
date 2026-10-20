using System;
using System.IO;
using UnityEngine;

namespace PushHands.Physical
{
    [Serializable] public sealed class PhysicalFrame
    {
        public string type = "frame", version = PhysicalConfig.Version;
        public int episode;
        public float time, dt;
        public PlayerIntent first, second;
        public BalanceSample one, two;
        public PoseSample poseOne, poseTwo;
        public int contactsOne, contactsTwo;
        public Technique techniqueOne, techniqueTwo;
        public float[] observationOne, observationTwo;
        public RoundResult result;
    }
    [Serializable] public sealed class PhysicalRecordHeader
    {
        public string type = "header", version = PhysicalConfig.Version, unity, source, config;
        public int observationSize = LinearBalancePolicy.ObservationSize;
    }
    public sealed class PhysicalRecorder : MonoBehaviour
    {
        public bool Recording => writer != null;
        public string LastPath { get; private set; }
        private StreamWriter writer;
        private int episode, frames;
        public void Begin(PhysicalMatch match, string source)
        {
            Stop();
            string root = Application.isEditor ? Path.GetFullPath("Artifacts/PhysicalRecords") : Path.Combine(Application.persistentDataPath, "PhysicalRecords");
            try
            {
                Directory.CreateDirectory(root); LastPath = Path.Combine(root, DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + ".jsonl");
                writer = new StreamWriter(LastPath, false, new System.Text.UTF8Encoding(false)); episode = frames = 0;
                writer.WriteLine(JsonUtility.ToJson(new PhysicalRecordHeader { unity = Application.unityVersion, source = source, config = JsonUtility.ToJson(match.Config) }));
                writer.Flush(); Debug.Log("开始本地玩法记录：" + LastPath);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            { Stop(); Debug.LogError("无法创建玩法记录：" + e.Message); }
        }
        public void Capture(PhysicalMatch match, PlayerIntent first, PlayerIntent second)
        {
            if (writer == null) return;
            var a = match.Fighters[0].CaptureBalance(); var b = match.Fighters[1].CaptureBalance();
            if (!a.Finite || !b.Finite) { Stop(); Debug.LogError("非有限物理数据已停止记录"); return; }
            try
            {
                writer.WriteLine(JsonUtility.ToJson(new PhysicalFrame { episode = episode, time = match.FightSeconds, dt = match.Config.FixedStep,
                    first = first, second = second, one = a, two = b,
                    poseOne = match.Fighters[0].CapturePose(), poseTwo = match.Fighters[1].CapturePose(),
                    contactsOne = match.Fighters[0].ContactCount, contactsTwo = match.Fighters[1].ContactCount,
                    techniqueOne = match.Fighters[0].Technique.Active, techniqueTwo = match.Fighters[1].Technique.Active,
                    observationOne = LinearBalancePolicy.Observe(match.Fighters[0], first), observationTwo = LinearBalancePolicy.Observe(match.Fighters[1], second), result = match.Round.Result }));
                if (++frames % 100 == 0 || match.Round.Result != RoundResult.None) writer.Flush();
            }
            catch (IOException e) { Stop(); Debug.LogError("玩法记录写入失败：" + e.Message); }
        }
        public void MarkReset(PhysicalMatch match)
        { if (Recording) { episode++; Capture(match, default, default); } }
        public void Stop() { writer?.Dispose(); writer = null; }
        private void OnDestroy() => Stop();
        private void OnApplicationQuit() => Stop();
    }
}
