using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive.Linq;
using Avalonia.Media;
using OpenUtau.App.Controls;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace OpenUtau.App.ViewModels {
    public class ExpSelectorViewModel : ViewModelBase, ICmdSubscriber, IDisposable {
        [Reactive] public int Index { get; set; }
        [Reactive] public int SelectedIndex { get; set; }
        [Reactive] public ExpDisMode DisplayMode { get; set; }
        [Reactive] public UExpressionDescriptor? Descriptor { get; set; }
        public string Abbr {
            get{
                if (Descriptor == null) {
                    return "";
                }
                return Descriptor.abbr;
            }
        }
        public ObservableCollection<UExpressionDescriptor> Descriptors => descriptors;
        public string Header => header.Value;
        [Reactive] public IBrush TagBrush { get; set; }
        [Reactive] public IBrush Background { get; set; }

        ObservableCollection<UExpressionDescriptor> descriptors = new ObservableCollection<UExpressionDescriptor>();
        ObservableAsPropertyHelper<string> header;
        readonly OpenUtau.App.UiThreadAffinity affinity = new OpenUtau.App.UiThreadAffinity();
        IDisposable? themeSub;
        bool unsubscribed;

        public ExpSelectorViewModel() {
            DocManager.Inst.AddSubscriber(this);
            this.WhenAnyValue(x => x.DisplayMode)
                .Subscribe(_ => RefreshBrushes());
            this.WhenAnyValue(x => x.Descriptor)
                .Select(descriptor => descriptor == null ? string.Empty : descriptor.abbr.ToUpperInvariant())
                .ToProperty(this, x => x.Header, out header);
            this.WhenAnyValue(x => x.Descriptor)
                .Subscribe(SelectionChanged);
            this.WhenAnyValue(x => x.Index, x => x.Descriptors)
                .Subscribe(tuple => {
                    SetExp(DocManager.Inst.Project.expSelectors[tuple.Item1]);
                });
            themeSub = MessageBus.Current.Listen<ThemeChangedEvent>()
                .Subscribe(_ => RefreshBrushes());
            TagBrush = ThemeManager.ExpNameBrush;
            Background = ThemeManager.ExpBrush;
            OnListChange();
        }

        public bool SetExp(string abbr) {
            if(Descriptors.Any(d => d.abbr == abbr)) {
                Descriptor = Descriptors.First(d => d.abbr == abbr);
                return true;
            } else {
                if (Descriptors != null && Descriptors.Count > Index) {
                    Descriptor = Descriptors[Index];
                }
                return false;
            }
        }

        public void OnSelected(bool store) {
            if (DisplayMode != ExpDisMode.Visible && Descriptor != null) {
                DocManager.Inst.ExecuteCmd(new SelectExpressionNotification(Descriptor.abbr, Index, true));
            }
            if(store) {
                var project = DocManager.Inst.Project;
                project.expSecondary = project.expPrimary;
                project.expPrimary = Index;
            }
        }

        void SelectionChanged(UExpressionDescriptor? descriptor) {
            if (descriptor != null) {
                DocManager.Inst.ExecuteCmd(new SelectExpressionNotification(descriptor.abbr, Index, DisplayMode != ExpDisMode.Visible));
            }
            if (!string.IsNullOrEmpty(Abbr)) {
                DocManager.Inst.Project.expSelectors[Index] = Abbr;
            }
        }

        public void OnNext(UCommand cmd, bool isUndo) {
            if (!(cmd is LoadProjectNotification ||
                  cmd is LoadPartNotification ||
                  cmd is ConfigureExpressionsCommand ||
                  cmd is SelectExpressionNotification)) {
                return;
            }
            // 项目可能在**非 UI 线程**被加载（后台渲染/测试宿主）⇒ 这里动的是绑到 ItemsControl 的
            // `Descriptors` 与绑到控件属性的 `SelectedIndex`/`DisplayMode`，跨线程会直接抛
            // `Dispatcher.VerifyAccess`（W25 那 7 例确定性红的栈顶就是这个位置）。
            // 口径见 `UiThreadAffinity`：锚定订阅线程 + `Post` 重入队（不用 `Invoke`：会与 DocManager 锁互锁）。
            affinity.Post(() => OnNextCore(cmd));
        }

        private void OnNextCore(UCommand cmd) {
            if (cmd is LoadProjectNotification ||
                cmd is LoadPartNotification ||
                cmd is ConfigureExpressionsCommand) {
                OnListChange();
            } else if (cmd is SelectExpressionNotification) {
                OnSelectExp((SelectExpressionNotification)cmd);
            }
        }

        /// <summary>
        /// 退订（关窗/测试释放用）。此前该 VM 一旦构造就**永久**留在 DocManager 的订阅表里：
        /// 卷帘被分离/重建（`DetachAndFlush` 路径）或 headless 用例反复构造时会越积越多。
        /// </summary>
        public void Unsubscribe() {
            if (unsubscribed) {
                return;
            }
            unsubscribed = true;
            DocManager.Inst.RemoveSubscriber(this);
            themeSub?.Dispose();
            themeSub = null;
        }

        public void Dispose() => Unsubscribe();

        private void OnListChange() {
            var selectedIndex = SelectedIndex;
            Descriptors.Clear();
            DocManager.Inst.Project.expressions.Values.ToList().ForEach(Descriptors.Add);
            if (selectedIndex >= descriptors.Count) {
                selectedIndex = Index;
            }
            SelectedIndex = selectedIndex;
        }

        private void OnSelectExp(SelectExpressionNotification cmd) {
            if (Descriptors.Count == 0) {
                return;
            }
            if (cmd.SelectorIndex == Index) {
                if (Descriptors[SelectedIndex].abbr != cmd.ExpKey) {
                    SelectedIndex = Descriptors.IndexOf(Descriptors.First(d => d.abbr == cmd.ExpKey));
                }
                DisplayMode = ExpDisMode.Visible;
            } else if (cmd.UpdateShadow) {
                DisplayMode = DisplayMode == ExpDisMode.Visible ? ExpDisMode.Shadow : ExpDisMode.Hidden;
            }
        }

        private void RefreshBrushes() {
            TagBrush = DisplayMode == ExpDisMode.Visible
                    ? ThemeManager.ExpActiveNameBrush
                    : DisplayMode == ExpDisMode.Shadow
                    ? ThemeManager.ExpShadowNameBrush
                    : ThemeManager.ExpNameBrush;
            Background = DisplayMode == ExpDisMode.Visible
                    ? ThemeManager.ExpActiveBrush
                    : DisplayMode == ExpDisMode.Shadow
                    ? ThemeManager.ExpShadowBrush
                    : ThemeManager.ExpBrush;
        }
    }
}
