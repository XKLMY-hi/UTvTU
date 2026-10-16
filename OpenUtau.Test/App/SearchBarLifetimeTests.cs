using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using OpenUtau.App.Controls;
using OpenUtau.App.ViewModels;
using OpenUtau.Core;
using Xunit;

namespace OpenUtau.Test.App;

[Collection("WorkspaceSession")]
public sealed class SearchBarLifetimeTests {
    static IReadOnlyList<ICmdSubscriber> Subscriptions =>
        ((List<ICmdSubscriber>)typeof(DocManager).GetField("subscribers", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(DocManager.Inst)!).ToArray();

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReopeningAndClosingRemovesEveryPreviousSearchSubscription(bool escape) {
        using var notes = new NotesViewModel();
        var baseline = Subscriptions.Count;
        var search = new SearchBar();
        var host = new Window { Content = search }; host.Show();
        try {
            for (int i = 0; i < 3; i++) {
                search.Show(notes);
                Assert.Equal(baseline + 1, Subscriptions.Count);
            }
            if (escape) {
                search.FindControl<TextBox>("box")!.RaiseEvent(new KeyEventArgs {
                    RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape,
                });
            } else {
                search.OnClose(search, new RoutedEventArgs());
            }
            Assert.False(search.IsVisible);
            Assert.Equal(baseline, Subscriptions.Count);
            search.Show(notes);
            host.Content = null;
            Assert.Equal(baseline, Subscriptions.Count);
        } finally { host.Content = null; host.Close(); }
    }
}
