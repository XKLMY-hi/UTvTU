using System;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using OpenUtau.App.ViewModels;
using OpenUtau.App.Views;
using Serilog.Events;
using Serilog.Parsing;
using Xunit;

namespace OpenUtau.Test.App {
    [Collection("Theme")]
    public class DeveloperToolsTests {
        static LogEvent Message(string text) => new(DateTimeOffset.Now, LogEventLevel.Error,
            new Exception(text), new MessageTemplateParser().Parse("Test"), Array.Empty<LogEventProperty>());
        [AvaloniaFact]
        public void BackgroundLogsFilterExceptionsAndClosingDetachesTheView() {
            var window = new DebugWindow();
            var model = (DebugViewModel)window.DataContext!;
            try {
                window.Show();
                model.Clear();
                model.Filter = "exception-marker";
                Task.Run(() => DebugViewModel.Sink.Inst.Emit(Message("exception-marker"))).GetAwaiter().GetResult();
                Dispatcher.UIThread.RunJobs();
                Assert.Single(model.LogEvents);
                model.Filter = "no-match";
                Assert.Empty(model.LogEvents);
                model.Filter = string.Empty;
                Assert.Single(model.LogEvents);
                for (int i = 0; i < 2004; i++) DebugViewModel.Sink.Inst.Emit(Message("bounded"));
                Assert.Equal(2000, DebugViewModel.Sink.Inst.LogEvents.Count);
                window.Close();
                int count = model.LogEvents.Count;
                DebugViewModel.Sink.Inst.Emit(Message("after-close"));
                Assert.Equal(count, model.LogEvents.Count);
            } finally { window.Close(); model.Detach(); model.Clear(); }
        }
    }
}
