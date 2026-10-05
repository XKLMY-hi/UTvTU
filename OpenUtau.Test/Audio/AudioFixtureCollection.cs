using OpenUtau.Test.TestSupport;
using Xunit;

namespace OpenUtau.Test.Audio {
    /// <summary>
    /// W7 音频验收装置的测试集合：**串行执行**（与其它集合也不并行）。
    ///
    /// 原因：这些用例会装配产品级全局单例——<c>DocManager.Project</c>/调度器、
    /// <c>PhonemizerRunner</c>、<c>VstPluginManager.Bridge</c>（假 VST）、
    /// <c>ToolsManager</c> 等。并行运行会与仓库里其它共享单例的用例互相干扰。
    ///
    /// 集合级 fixture <see cref="AudioGlobalStateGuard"/> 在集合结束时**还原进程级全局
    /// 线程态**（<c>DocManager.mainThread</c>/<c>mainScheduler</c>）：即使某个用例中途失败，
    /// 也不会把"主线程是测试线程"这种状态泄漏给之后运行的其它集合（W14 flake 同类根因）。
    /// </summary>
    [CollectionDefinition("AudioFixture", DisableParallelization = true)]
    public class AudioFixtureCollection : ICollectionFixture<AudioGlobalStateGuard> { }

    /// <summary>集合范围的全局态守卫：集合跑完即还原测试改过的进程级字段。</summary>
    public sealed class AudioGlobalStateGuard : System.IDisposable {
        readonly bool singerWasRegistered;

        public AudioGlobalStateGuard() {
            // 集合开始前是否已存在 W7 伪声库条目（通常没有；若已有说明是别人装的，不动）
            singerWasRegistered = OpenUtau.Core.SingerManager.Inst.Singers.ContainsKey(DummyVoicebank.FolderName);
        }

        public void Dispose() {
            AudioFixtures.RestoreSchedulers();
            // 本集合用例会把伪声库塞进**进程级** SingerManager.Inst.Singers
            // （UstxpFixtureTests 等）——集合结束若不摘掉，之后运行的用例会看到一个指向
            // %TEMP% 的假歌手（加载/搜索路径都可能命中）。只摘自己装的那个。
            if (!singerWasRegistered) {
                OpenUtau.Core.SingerManager.Inst.Singers.Remove(DummyVoicebank.FolderName);
            }
        }
    }
}
