using System;
using System.Collections;
using System.Linq;
using GameLogic.World;
using NUnit.Framework;
using TEngine;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace GameLogic.Tests
{
    /// <summary>
    /// NPC 移动实证（R2：每游戏小时沿最短路径走 1 格）：
    /// 加速档位 ×4 下推进 3 个游戏小时（≈45 真实秒），断言至少一名 NPC 格位发生变化。
    /// 回应「场景角色不动」疑虑：移动是整点制 + 低速滑行，肉眼在 1x 下几乎不可见，本测试用数据证明链路活着。
    /// </summary>
    public sealed class NpcMovementTests
    {
        private const string ScenePath = "Assets/Scenes/main.unity";

        private int _prevEditorPlayMode;

        [SetUp]
        public void ForceEditorSimulateMode()
        {
            // 编辑器下 ResourceModule.PlayMode 读 EditorPrefs["EditorPlayMode"]（工具栏可切换）。
            // 若被切到 Offline/Host，PlayMode 测试引导时 YooAsset 会去读
            // StreamingAssets/package/DefaultPackage/BuildinCatalog.bytes，
            // 未构建 AssetBundle 时该文件不存在 → HTTP 404。强制回模拟模式（EPlayMode.EditorSimulateMode = 0）。
            _prevEditorPlayMode = UnityEditor.EditorPrefs.GetInt("EditorPlayMode", 0);
            UnityEditor.EditorPrefs.SetInt("EditorPlayMode", 0);
        }

        [TearDown]
        public void RestoreEditorPlayMode()
        {
            UnityEditor.EditorPrefs.SetInt("EditorPlayMode", _prevEditorPlayMode);
        }

        [UnityTest]
        public IEnumerator NpcsMoveOverHours()
        {
            LogAssert.ignoreFailingMessages = true;
            EditorSceneManager.LoadSceneInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Single));

            // 等世界仿真就绪（同 UiVisualCaptureTests：不碰自动创建式 SingletonBehaviour.Instance）。
            float deadline = Time.realtimeSinceStartup + 60f;
            while ((GameModule.World == null || GameModule.World.Sim == null || !GameModule.World.Sim.IsRunning)
                   && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }
            Assert.IsNotNull(GameModule.World?.Sim, "世界仿真应在 60s 内启动");

            var sim = GameModule.World.Sim;
            sim.SetSpeedLevel(3); // ×4：1 游戏小时 ≈ 15 真实秒

            // 记录起始格位与起始小时（人口 = 定居 + 暂住）。
            var before = sim.Population.Residents.Concat(sim.Population.Visitors)
                .Where(c => c.IsAlive && !c.IsHidden)
                .ToDictionary(c => c.Id, c => c.Position);
            int startHour = sim.Time.Hour + sim.Time.DayOverall * 24;
            Debug.Log($"[MoveProbe] 起始: hour={sim.Time.Hour} npcs={before.Count}");

            // 推进至少 3 个游戏小时。
            yield return new WaitForSecondsRealtime(50f);

            int nowHour = sim.Time.Hour + sim.Time.DayOverall * 24;
            int moved = 0;
            foreach (var pair in before)
            {
                var c = sim.Population.Residents.Concat(sim.Population.Visitors).FirstOrDefault(x => x.Id == pair.Key);
                if (c != null && c.IsAlive && !c.Position.Equals(pair.Value))
                {
                    moved++;
                    Debug.Log($"[MoveProbe] NPC {c.Name}({c.Profession}): {pair.Value} -> {c.Position}");
                }
            }
            Debug.Log($"[MoveProbe] 经过 {nowHour - startHour} 游戏小时，移动 NPC 数: {moved}/{before.Count}");

            Assert.GreaterOrEqual(nowHour - startHour, 2, "应至少推进 2 个游戏小时");
            Assert.Greater(moved, 0, "至少一名 NPC 应在数小时内移动（R2 整点走 1 格）");
        }
    }
}
