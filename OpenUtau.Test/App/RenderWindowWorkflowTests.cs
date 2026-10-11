using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using OpenUtau.App;
using OpenUtau.App.Views;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Util;
using OpenUtau.Test.TestSupport;
using Xunit;

namespace OpenUtau.Test.App {
    [Collection("WorkspaceSession")]
    public sealed class RenderWindowWorkflowTests : IDisposable {
        readonly DocManagerTestSetup.ScopedDispatcher dispatcher = DocManagerTestSetup.EnterScopedDispatcher();
        readonly UProject previous = DocManager.Inst.Project;
        readonly string previousPath = PathManager.Inst.DataPath;
        readonly string directory = Path.Combine(Path.GetTempPath(), "utvtu-render-ui-" + Guid.NewGuid().ToString("N"));
        public RenderWindowWorkflowTests() {
            Directory.CreateDirectory(directory);
            typeof(PathManager).GetProperty(nameof(PathManager.DataPath))!.SetValue(PathManager.Inst, directory);
            var project = new UProject();
            var part = new UWavePart { trackNo = 0, channels = 2, sampleRate = 44100 };
            typeof(UWavePart).GetProperty(nameof(UWavePart.Samples))!.SetValue(part, Enumerable.Repeat(.25f, 8820).ToArray());
            typeof(UWavePart).GetField("duration", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(part, 96);
            project.parts.Add(part);
            typeof(DocManager).GetProperty(nameof(DocManager.Project))!.SetValue(DocManager.Inst, project);
        }
        static T Field<T>(RenderWindow window, string name) where T : Control => window.FindControl<T>(name)!;
        [AvaloniaFact]
        public void ModeRangeTrackSelectionAndInvalidInputControlTheRealStartButton() {
            var window = new RenderWindow { Width = 620, Height = 480 };
            try {
                window.Show();
                Dispatcher.UIThread.RunJobs();
                Assert.True(Field<Button>(window, "StartBtn").IsEnabled);
                Field<RadioButton>(window, "RadioPerTrack").IsChecked = true;
                Assert.False(Field<CheckBox>(window, "ChkMaster").IsEnabled);
                Assert.True(Field<ComboBox>(window, "FormatCombo").IsEnabled);
                Assert.True(Field<ComboBox>(window, "SampleRateCombo").IsEnabled);
                window.OnSelectNoTracks(null, new RoutedEventArgs());
                Assert.False(Field<Button>(window, "StartBtn").IsEnabled);
                window.OnSelectAllTracks(null, new RoutedEventArgs());
                Field<RadioButton>(window, "RadioCustom").IsChecked = true;
                Assert.True(Field<Grid>(window, "CustomSecondsPanel").IsVisible);
                Field<TextBox>(window, "CustomEndSecondsBox").Text = "NaN";
                Dispatcher.UIThread.RunJobs(); // TextChanged is posted by Avalonia after the property update.
                Assert.False(Field<Button>(window, "StartBtn").IsEnabled);
                Field<TextBox>(window, "CustomEndSecondsBox").Text = ".05";
                Dispatcher.UIThread.RunJobs();
                Assert.True(Field<Button>(window, "StartBtn").IsEnabled);
                Field<TextBox>(window, "TailSecondsBox").Text = "31";
                Dispatcher.UIThread.RunJobs();
                Assert.False(Field<Button>(window, "StartBtn").IsEnabled);
                Field<TextBox>(window, "TailSecondsBox").Text = "0";
                Dispatcher.UIThread.RunJobs();
                Assert.True(Field<Button>(window, "StartBtn").IsEnabled);
                Assert.InRange(Field<Button>(window, "StartBtn").Bounds.Height, 35, 37);
            } finally { window.Close(); }
        }
        [AvaloniaFact]
        public async Task StartWritesARealFileAndRestoresTheJobControls() {
            var window = new RenderWindow();
            try {
                window.Show();
                Field<TextBox>(window, "OutputPathBox").Text = Path.Combine(directory, "actual.wav");
                Field<CheckBox>(window, "ChkVst").IsChecked = false;
                Field<CheckBox>(window, "ChkBuiltinFx").IsChecked = false;
                Field<ComboBox>(window, "FormatCombo").SelectedIndex = 1;
                Field<ComboBox>(window, "SampleRateCombo").SelectedIndex = 1;
                window.OnStartRender(null, new RoutedEventArgs());
                Assert.False(Field<StackPanel>(window, "InputPanel").IsEnabled);
                Assert.True(Field<Button>(window, "CancelBtn").IsEnabled);
                var watch = System.Diagnostics.Stopwatch.StartNew();
                while (Field<StackPanel>(window, "InputPanel").IsEnabled == false && watch.Elapsed.TotalSeconds < 5) {
                    await Task.Delay(10);
                    Dispatcher.UIThread.RunJobs();
                }
                Assert.True(Field<StackPanel>(window, "InputPanel").IsEnabled);
                Assert.False(Field<Button>(window, "CancelBtn").IsEnabled);
                Assert.Equal(100, Field<ProgressBar>(window, "ProgressBarControl").Value);
                Assert.Equal(ThemeManager.GetString("render.status.done"), Field<TextBlock>(window, "ProgressLabel").Text);
                using var reader = new NAudio.Wave.WaveFileReader(Path.Combine(directory, "actual.wav"));
                Assert.Equal(24, reader.WaveFormat.BitsPerSample);
                Assert.Equal(48000, reader.WaveFormat.SampleRate);
            } finally { window.Close(); }
        }
        public void Dispose() {
            typeof(DocManager).GetProperty(nameof(DocManager.Project))!.SetValue(DocManager.Inst, previous);
            typeof(PathManager).GetProperty(nameof(PathManager.DataPath))!.SetValue(PathManager.Inst, previousPath);
            dispatcher.Dispose();
            Directory.Delete(directory, recursive: true);
        }
    }
}
