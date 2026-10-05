using Xunit;

namespace OpenUtau.Test.Audio {
    /// <summary>
    /// W7 音频验收装置的测试集合：**串行执行**（与其它集合也不并行）。
    ///
    /// 原因：这些用例会装配产品级全局单例——<c>DocManager.Project</c>/调度器、
    /// <c>PhonemizerRunner</c>、<c>VstPluginManager.Bridge</c>（假 VST）、
    /// <c>ToolsManager</c> 等。并行运行会与仓库里其它共享单例的用例互相干扰。
    /// </summary>
    [CollectionDefinition("AudioFixture", DisableParallelization = true)]
    public class AudioFixtureCollection { }
}
