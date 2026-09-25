using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using OpenUtau.App.Controls;
using OpenUtau.Core;
using Serilog;

namespace OpenUtau.App.Views {
    /// <summary>
    /// MessageBox 门面（2026-09-25：渲染为**自研 MD3 模态窗口**）。
    /// 60+ 调用点签名零改动：Show/ShowError/ShowModal/ShowProcessing + 结果枚举。
    /// - Show：确认框，按钮按 <see cref="MessageBoxButtons"/> 生成（主操作走实心胶囊）
    /// - ShowError：链接化正文 + 详情 Expander + 复制按钮
    /// - ShowModal/ShowProcessing：进度内容 + 关闭/取消按钮；窗口实例由本类持有，
    ///   SetText / Close 直接作用于它（窗口实例由本类持有）
    /// </summary>
    public class MessageBox {
        public enum MessageBoxButtons { Ok, OkCancel, YesNo, YesNoCancel, OkCopy }
        public enum MessageBoxResult { Ok, Cancel, Yes, No }

        /// <summary>按钮描述（label + 结果 + 是否主操作 + 可选自定义动作）。</summary>
        private sealed class DialogButton {
            public string Label = string.Empty;
            public MessageBoxResult Result = MessageBoxResult.Ok;
            public bool Primary;
            public Func<Task>? Action;
        }

        private TextBlock? _contentText;
        private Window? _window;
        private bool _closeRequested;

        /// <summary>窗口关闭时触发（ShowModal 调用方用它感知取消）。</summary>
        public event EventHandler? Closed;

        /// <summary>更新对话框正文（进度文本等）。</summary>
        public void SetText(string text) {
            Dispatcher.UIThread.Post(() => {
                if (_contentText != null) {
                    _contentText.Text = text;
                }
            });
        }

        /// <summary>程序化关闭对话框（幂等；窗口未挂载时延迟到挂载后执行）。</summary>
        public void Close() {
            Dispatcher.UIThread.Post(() => {
                if (_window == null) {
                    _closeRequested = true;
                    return;
                }
                _window.Close();
            });
        }

        /// <summary>错误对话框：异常翻译/聚合/版本号逻辑保留，渲染走自研 MD3 窗口。</summary>
        public static Task<MessageBoxResult> ShowError(Window parent, Exception? e, string message = "", bool fromNotif = false) {
            string text = message;
            string title = ThemeManager.GetString("errors.caption");
            if (fromNotif) {
                IReadOnlyList<Window> dialogs = ((IClassicDesktopStyleApplicationLifetime)Application.Current!.ApplicationLifetime!).Windows;
                foreach (var dialog in dialogs) {
                    if (dialog.IsActive) {
                        parent = dialog;
                        break;
                    }
                }
            }

            var builder = new StringBuilder();
            if (e != null) {
                if (e is AggregateException ae && ae.Flatten().InnerExceptions.Count == 1) {
                    e = ae.InnerExceptions.First();
                }

                if (e is MessageCustomizableException mce) {
                    text = Translate(mce);
                    builder.AppendLine(mce.SubstanceException.Message);
                    builder.AppendLine();
                    builder.Append(mce.SubstanceException.ToString());
                    if (!mce.ShowStackTrace) {
                        return Show(parent, text, title, MessageBoxButtons.Ok);
                    }
                } else if (e is AggregateException nestedAe) {
                    foreach (var ie in nestedAe.Flatten().InnerExceptions) {
                        if (!string.IsNullOrWhiteSpace(text)) {
                            text += "\n";
                        }
                        if (ie is MessageCustomizableException innnerMce) {
                            text += Translate(innnerMce);
                            builder.AppendLine(innnerMce.SubstanceException.Message);
                            builder.AppendLine();
                            builder.Append(innnerMce.SubstanceException.ToString());
                        } else {
                            text += ie.Message;
                            builder.AppendLine(ie.Message);
                            builder.AppendLine();
                            builder.AppendLine(ie.ToString());
                        }
                        builder.AppendLine();
                    }
                } else {
                    builder.AppendLine(e.Message);
                    builder.AppendLine();
                    builder.Append(e.ToString());
                    if (string.IsNullOrEmpty(text)) {
                        text = e.Message;
                    }
                }
            }
            builder.AppendLine();
            builder.AppendLine();
            builder.AppendLine(System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "Unknown Version");

            return Show(parent, text, title, MessageBoxButtons.OkCopy, builder.ToString());

            string Translate(MessageCustomizableException mce) {
                string text;
                if (string.IsNullOrWhiteSpace(mce.TranslatableMessage)) {
                    text = mce.Message;
                } else {
                    text = mce.TranslatableMessage;
                    try {
                        var matches = Regex.Matches(mce.TranslatableMessage, "<translate:(.*?)>");
                        foreach (Match match in matches) {
                            if (ThemeManager.TryGetString(match.Groups[1].Value, out string translated)) {
                                text = text.Replace(match.Value, translated);
                            } else {
                                text = mce.Message;
                                break;
                            }
                        }
                    } catch {
                        text = mce.Message;
                    }
                }

                if (mce.Replaces != null && mce.Replaces.Length > 0) {
                    return string.Format(text, mce.Replaces);
                } else {
                    return text;
                }
            }
        }

        /// <summary>
        /// 普通对话框：Ok/OkCancel/YesNo/YesNoCancel 生成对应按钮；OkCopy 追加「复制」按钮。
        /// 门面保持同步方法返回 Task（旧实现即如此），避免 CS4014 波及相关 async 调用点。
        /// </summary>
        public static Task<MessageBoxResult> Show(Window parent, string text, string title, MessageBoxButtons buttons, string? stackTrace = null) {
            bool withDetails = buttons == MessageBoxButtons.OkCopy;

            var contentPanel = new StackPanel {
                MaxWidth = 560,
                Spacing = 4,
                VerticalAlignment = VerticalAlignment.Center,
            };
            SetTextWithLink(text, contentPanel);
            if (stackTrace != null) {
                var stackTracePanel = new StackPanel();
                contentPanel.Children.Add(new Expander {
                    Header = ThemeManager.GetString("errors.details"),
                    Content = stackTracePanel,
                });
                SetTextWithLink(stackTrace, stackTracePanel);
            }

            var dialogButtons = new List<DialogButton>();
            if (withDetails) {
                dialogButtons.Add(new DialogButton {
                    Label = ThemeManager.GetString("dialogs.messagebox.copy"),
                    Result = MessageBoxResult.Ok,
                    Action = async () => {
                        try {
                            var data = new DataTransfer();
                            data.Add(DataTransferItem.CreateText(text + "\n" + stackTrace));
                            var clipboard = TopLevel.GetTopLevel(parent)?.Clipboard;
                            if (clipboard != null) {
                                await clipboard.SetDataAsync(data);
                            }
                        } catch (Exception ex) {
                            Log.Error(ex, "Failed to copy message");
                        }
                    },
                });
            }
            foreach (var (label, result, primary) in ButtonsOf(buttons)) {
                dialogButtons.Add(new DialogButton { Label = label, Result = result, Primary = primary });
            }

            var msgbox = new MessageBox();
            var window = BuildWindow(title, contentPanel, dialogButtons, msgbox, DefaultResult(buttons));
            return ShowDialogAsync(parent, window, msgbox, DefaultResult(buttons));
        }

        /// <summary>模态进度框：正文 + OK 按钮；返回实例供 SetText/Close/Closed 使用。</summary>
        public static MessageBox ShowModal(Window parent, string text, string title) {
            var msgbox = new MessageBox();
            var content = new TextBlock {
                Text = text,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 560,
                VerticalAlignment = VerticalAlignment.Center,
            };
            msgbox._contentText = content;
            var buttons = new List<DialogButton> {
                new DialogButton { Label = ThemeManager.GetString("button.ok"), Result = MessageBoxResult.Ok, Primary = true },
            };
            var window = BuildWindow(title, content, buttons, msgbox, MessageBoxResult.Ok);
            _ = ShowDialogAsync(parent, window, msgbox, MessageBoxResult.Ok)
                .ContinueWith(_ => msgbox.Closed?.Invoke(msgbox, EventArgs.Empty),
                    TaskScheduler.FromCurrentSynchronizationContext());
            return msgbox;
        }

        /// <summary>
        /// 后台任务进度框：正文 + 取消按钮。取消 → token 取消；任务完成 → 自动关窗。
        /// 语义与旧实现一致：返回执行任务本身（fault 检查、取消时 Result=Cancel）。
        /// </summary>
        public static Task<MessageBoxResult> ShowProcessing(
                Window parent,
                string text,
                string title,
                Action<MessageBox, CancellationToken> action,
                Action<Task>? onFinished = null) {
            var msgbox = new MessageBox();
            var content = new TextBlock {
                Text = text,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 560,
                VerticalAlignment = VerticalAlignment.Center,
            };
            msgbox._contentText = content;
            var buttons = new List<DialogButton> {
                new DialogButton { Label = ThemeManager.GetString("button.cancel"), Result = MessageBoxResult.Cancel },
            };
            var window = BuildWindow(title, content, buttons, msgbox, MessageBoxResult.Cancel);

            var res = MessageBoxResult.Ok;
            var tokenSource = new CancellationTokenSource();
            var scheduler = TaskScheduler.FromCurrentSynchronizationContext();
            var task = Task.Run(() => {
                action.Invoke(msgbox, tokenSource.Token);
                return res;
            }, tokenSource.Token);

            // 任务完成 → 关窗（若还在）+ 通知调用方
            task.ContinueWith(_ => {
                msgbox.Close();
                onFinished?.Invoke(task);
            }, scheduler);

            // 窗口关闭（用户点取消 / 关窗）→ 若任务未完成则取消
            _ = ShowDialogAsync(parent, window, msgbox, MessageBoxResult.Cancel).ContinueWith(_ => {
                if (!task.IsCompleted) {
                    res = MessageBoxResult.Cancel;
                    tokenSource.Cancel();
                }
            });

            return task;
        }

        // ── 自研 MD3 模态窗口 ────────────────────────────────────

        /// <summary>
        /// 组装 MD3 对话框窗口：标题 16 medium、正文 13、右下角按钮（主操作实心胶囊）。
        /// ESC = 默认按钮；回车 = 第一个主操作按钮。
        /// </summary>
        private static Window BuildWindow(string title, Control content, List<DialogButton> buttons, MessageBox messageBox, MessageBoxResult escapeResult) {
            var root = new StackPanel {
                Margin = new Thickness(24),
                Spacing = 16,
                MinWidth = 360,
            };
            root.Children.Add(new TextBlock {
                Text = title,
                FontSize = 16,
                FontWeight = FontWeight.Medium,
                TextWrapping = TextWrapping.Wrap,
            });
            root.Children.Add(content);

            var buttonRow = new StackPanel {
                Orientation = Orientation.Horizontal,
                Spacing = 12,
                HorizontalAlignment = HorizontalAlignment.Right,
            };
            foreach (var descriptor in buttons) {
                var button = new Button {
                    Content = descriptor.Label,
                    MinWidth = 88,
                };
                if (descriptor.Primary) {
                    button.Classes.Add("primary");
                }
                var captured = descriptor;
                button.Click += async (_, _) => {
                    if (captured.Action != null) {
                        await captured.Action();
                    }
                    messageBox.Result = captured.Result;
                    messageBox._window?.Close();
                };
                buttonRow.Children.Add(button);
            }
            root.Children.Add(buttonRow);

            var window = new WindowEx {
                Title = title,
                Content = root,
                SizeToContent = SizeToContent.WidthAndHeight,
                CanResize = false,
                ShowInTaskbar = false,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                MaxWidth = 640,
            };
            window.KeyDown += (_, e) => {
                if (e.Key == Key.Escape) {
                    messageBox.Result = escapeResult;
                    window.Close();
                    e.Handled = true;
                }
            };
            return window;
        }

        /// <summary>本次对话的结果（窗口关闭后读取）。</summary>
        private MessageBoxResult Result { get; set; } = MessageBoxResult.Cancel;

        /// <summary>显示并等待关闭，返回结果（关闭窗口/ESC → escapeResult 已由调用方设定）。</summary>
        private static async Task<MessageBoxResult> ShowDialogAsync(Window parent, Window window, MessageBox messageBox, MessageBoxResult escapeResult) {
            messageBox._window = window;
            if (messageBox._closeRequested) {
                messageBox._closeRequested = false;
                return escapeResult;
            }
            await window.ShowDialog(parent);
            messageBox._window = null;
            return messageBox.Result;
        }

        private static IEnumerable<(string label, MessageBoxResult result, bool primary)> ButtonsOf(MessageBoxButtons buttons) {
            string ok = ThemeManager.GetString("button.ok");
            string cancel = ThemeManager.GetString("button.cancel");
            string yes = ThemeManager.TryGetString("button.yes", out var y) ? y : "Yes";
            string no = ThemeManager.TryGetString("button.no", out var n) ? n : "No";
            return buttons switch {
                MessageBoxButtons.Ok => new[] { (ok, MessageBoxResult.Ok, true) },
                MessageBoxButtons.OkCancel => new[] { (cancel, MessageBoxResult.Cancel, false), (ok, MessageBoxResult.Ok, true) },
                MessageBoxButtons.YesNo => new[] { (no, MessageBoxResult.No, false), (yes, MessageBoxResult.Yes, true) },
                MessageBoxButtons.YesNoCancel => new[] {
                    (cancel, MessageBoxResult.Cancel, false), (no, MessageBoxResult.No, false), (yes, MessageBoxResult.Yes, true),
                },
                _ => new[] { (ok, MessageBoxResult.Ok, true) },   // OkCopy：复制按钮单独加，末尾补确定
            };
        }

        /// <summary>旧实现"关闭窗口返回最后设置的默认按钮"语义。</summary>
        private static MessageBoxResult DefaultResult(MessageBoxButtons buttons) {
            return buttons switch {
                MessageBoxButtons.Ok => MessageBoxResult.Ok,
                MessageBoxButtons.OkCancel => MessageBoxResult.Cancel,
                MessageBoxButtons.YesNo => MessageBoxResult.No,
                _ => MessageBoxResult.Cancel, // YesNoCancel
            };
        }

        private static void SetTextWithLink(string text, StackPanel textPanel) {
            // @"http(s)?://([\w-]+\.)+[\w-]+(/[A-Z0-9-.,_/?%&=]*)?"
            var regex = new Regex(@"http(s)?://[^(\r\n|\n| )]+", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            var match = regex.Match(text);
            if (match.Success) {
                textPanel.Children.Add(new TextBlock { Text = text.Substring(0, match.Index), TextWrapping = TextWrapping.Wrap });
                var hyperlink = new Button {
                    Content = match.Value.Trim(),
                    Cursor = new Cursor(StandardCursorType.Hand),
                    Classes = { "linkButton" },
                    HorizontalAlignment = HorizontalAlignment.Left,
                };
                hyperlink.Click += OnUrlClick;
                textPanel.Children.Add(hyperlink);

                SetTextWithLink(text.Substring(match.Index + match.Length), textPanel);
            } else {
                if (!string.IsNullOrEmpty(text)) {
                    textPanel.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap });
                }
            }
        }

        private static void OnUrlClick(object? sender, RoutedEventArgs e) {
            try {
                if (sender is Button button && button.Content is string url) {
                    OS.OpenWeb(url);
                }
            } catch (Exception ex) {
                Log.Error(ex, "Failed to open url");
            }
        }
    }
}
