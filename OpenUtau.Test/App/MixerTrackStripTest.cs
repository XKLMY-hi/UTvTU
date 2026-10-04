using System;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using Avalonia.Headless.XUnit;
using OpenUtau.App.Controls;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;
using OpenUtau.Test.TestSupport;
using ReactiveUI;
using Xunit;

namespace OpenUtau.Test.App {
    public class MixerTrackStripTest {
        public MixerTrackStripTest() {
            // MixerTrackStrip 的声像写回路径调用 DocManager.Inst.ExecuteCmd；
            // wire it to run inline on the test (UI) thread.
            DocManagerTestSetup.RunOnCurrentThread();
        }
        /// <summary>
        /// Regression: LoadTrackData re-subscribed the pan handler each call without
        /// unsubscribing, so N refreshes caused one pan change to fire N
        /// PanChangeNotification messages. The fix keeps it at one.
        /// （2026-10 通道条按规格重绘：声像从 PanKnob 旋钮改为设计稿的"行"（拖拽/滚轮/←→ 改值），
        ///   驱动入口随之由 `PanKnobControl.Value` 变成 `PanValue`；**断言与语义未变**——
        ///   这个用例守的是"反复 Refresh 后一次改动仍只发一条通知"，与控件外观无关。）
        ///
        /// 订阅按 TrackNo 过滤：MessageBus 是全进程共享的，断言意图（"这一条轨反复 Refresh
        /// 后仍只发一条 Pan 通知"）本来就只针对本轨。不过滤时别的用例只要在同一时间窗内
        /// 构造 TrackHeaderViewModel（其构造函数会写回 Volume/Pan 并广播通知）就会把计数顶成 2，
        /// 属于对全局总线的隐式依赖，而非本用例要断言的行为。断言强度未变。
        /// </summary>
        [AvaloniaFact]
        public void RepeatedRefresh_StillSendsSinglePanNotification() {
            var track = new UTrack { TrackNo = 0 };
            var strip = new MixerTrackStrip(track);

            int received = 0;
            using var sub = MessageBus.Current.Listen<PanChangeNotification>()
                .Where(n => n.TrackNo == track.TrackNo)
                .ObserveOn(ImmediateScheduler.Instance)
                .Subscribe(_ => received++);

            // LoadTrackData is called once by the ctor; refresh 3 more times.
            strip.Refresh();
            strip.Refresh();
            strip.Refresh();

            // Changing the pan value should fire exactly one notification.
            strip.PanValue = 50;
            Assert.Equal(1, received);
        }

        /// <summary>
        /// DisposeSubscriptions must stop the strip from writing pan changes back
        /// (the mixer calls it when a strip is rebuilt / torn down).
        /// （同样按 TrackNo 过滤，去掉对全局总线的隐式依赖。）
        /// </summary>
        [AvaloniaFact]
        public void DisposeSubscriptions_StopsFurtherNotifications() {
            var track = new UTrack { TrackNo = 1 };
            var strip = new MixerTrackStrip(track);

            int received = 0;
            using var sub = MessageBus.Current.Listen<PanChangeNotification>()
                .Where(n => n.TrackNo == track.TrackNo)
                .ObserveOn(ImmediateScheduler.Instance)
                .Subscribe(_ => received++);

            strip.PanValue = 30;
            int beforeDispose = received;
            Assert.Equal(1, beforeDispose);

            strip.DisposeSubscriptions();
            strip.PanValue = 70;  // should produce no new notification
            Assert.Equal(beforeDispose, received);
        }
    }
}
