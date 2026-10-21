using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Reflection;
using System.Runtime.Loader;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.ReactiveUI;
using Avalonia.VisualTree;
using OpenUtau.App;
using OpenUtau.App.Views;
using OpenUtau.App.Controls;
using OpenUtau.App.ViewModels;
using OpenUtau.Classic;
using OpenUtau.Core;
using OpenUtau.Core.Util;
using OpenUtau.Core.SignalChain;
using OpenUtau.Core.Theming;
using OpenUtau.Theming;
using ReactiveUI;

class CapturePreferences {
    static string output;
    static MainWindow shell;
    static readonly List<string> checks=new();
    static readonly string[] names={"Audio","Playback","Library","Appearance","Editor","General","Midi","Shortcuts","About"};
    static void Check(bool result,string name) {
        if(!result)throw new Exception("FAILED: "+name);
        checks.Add(name);Console.WriteLine("PASS "+name);
    }
    static void Tick(int ms=40) {
        Dispatcher.UIThread.RunJobs();AvaloniaHeadlessPlatform.ForceRenderTimerTick(1);
        Thread.Sleep(ms);Dispatcher.UIThread.RunJobs();AvaloniaHeadlessPlatform.ForceRenderTimerTick(1);
    }
    static void Settle(int ms=240) { for(int i=0;i<ms/40;i++)Tick(); }
    static void Shot(string name) {
        shell.UpdateLayout();Dispatcher.UIThread.RunJobs();AvaloniaHeadlessPlatform.ForceRenderTimerTick(1);
        using var frame=shell.CaptureRenderedFrame()??throw new Exception("No frame");
        frame.Save(Path.Combine(output,name+".png"));
    }
    static void Nav(PreferencesView view,string name) {
        view.FindControl<Button>("Nav"+name).RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));Settle();
    }
    static void Inspect(PreferencesView view,string name,string mode) {
        var page=view.FindControl<Control>("Page"+name);
        var content=view.FindControl<StackPanel>("SettingsContent");
        var scroll=view.FindControl<ScrollViewer>("SettingsScroll");
        Check(names.Count(n=>view.FindControl<Control>("Page"+n).IsVisible)==1 && page.IsVisible,mode+" "+name+": exactly one category is visible");
        Check(names.Count(n=>view.FindControl<Button>("Nav"+n).Classes.Contains("selected"))==1,mode+" "+name+": navigation has one selected item");
        Check(view.FindControl<TextBlock>("PageDescription").Text==ThemeManager.GetString("prefs.description."+name.ToLower()),mode+" "+name+": category description follows navigation");
        Check(scroll.Offset.Y<.1,mode+" "+name+": category starts at the top");
        var fields=page.GetVisualDescendants().OfType<Control>().Where(c=>c is ComboBox || c is Slider || c is ToggleSwitch || c is TextBox).ToArray();
        Check(fields.All(c=>{var p=c.TranslatePoint(new Point(),content).Value;return p.X>=-.1 && p.X+c.Bounds.Width<=content.Bounds.Width+.1;}),mode+" "+name+": fields fit within the content width");
        foreach(var slider in page.GetVisualDescendants().OfType<Slider>().Where(c=>c.Classes.Contains("rowSlider"))) {
            var thumb=slider.GetVisualDescendants().OfType<Thumb>().FirstOrDefault();
            var p=thumb?.TranslatePoint(new Point(),slider);
            Check(slider.Bounds.Height>=32 && thumb!=null && p.HasValue && p.Value.Y>=-.1 && p.Value.Y+thumb.Bounds.Height<=slider.Bounds.Height+.1,mode+" "+name+": slider thumb fits its vertical layout");
            var track=slider.GetVisualDescendants().OfType<Track>().Single();
            Check(track.Bounds.Width>=slider.Bounds.Width*.9 && Math.Abs(track.Value-slider.Value)<.001,mode+" "+name+": slider track spans the field and follows its value");
            var rails=track.GetVisualDescendants().OfType<RepeatButton>().ToArray();
            Check(rails.Length==2 && rails.All(b=>b.Background!=null && b.Bounds.Height>=6) && rails.Sum(b=>b.Bounds.Width)>=track.Bounds.Width-thumb.Bounds.Width-1,mode+" "+name+": active and inactive slider rails fill the track");
        }
        foreach(var toggle in page.GetVisualDescendants().OfType<ToggleSwitch>()) {
            var track=toggle.GetVisualDescendants().OfType<Border>().Single(c=>c.Name=="SwitchKnobBounds");
            var p=track.TranslatePoint(new Point(),toggle).Value;
            Check(p.Y>=-.1 && p.Y+track.Bounds.Height<=toggle.Bounds.Height+.1,mode+" "+name+": switch track is fully visible");
        }
        if(page is AdaptiveColumns columns && columns.Children.Count>1) {
            var left=columns.Children[0];var right=columns.Children[1];
            bool narrow=shell.Width==800;
            Check(narrow?Math.Abs(left.Bounds.X-right.Bounds.X)<.1 && right.Bounds.Top>=left.Bounds.Bottom+columns.Spacing-.1:Math.Abs(left.Bounds.Y-right.Bounds.Y)<.1 && right.Bounds.Left>=left.Bounds.Right+columns.Spacing-.1,mode+" "+name+": settings columns reflow in reading order");
        }
        var contours=view.FindControl<Grid>("SettingsContours");
        Check(!contours.IsHitTestVisible && contours.ClipToBounds && contours.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Shape>().Count()==3,mode+" "+name+": header outlines are clipped and do not intercept input");
        if(name=="About") {
            var panel=view.FindControl<StackPanel>("AboutContent");
            Check(Math.Abs(panel.TranslatePoint(new Point(),content).Value.X)<.1 && Math.Abs(panel.Bounds.Width-content.Bounds.Width)<.1,mode+" About: cards share the settings left edge and width");
            var details=view.FindControl<StackPanel>("AboutDetails");
            var left=details.TranslatePoint(new Point(),content).Value.X;
            Check(new[]{"AboutBrand","AboutLinks","AboutCredit"}.All(n=>Math.Abs(view.FindControl<Control>(n).TranslatePoint(new Point(),content).Value.X-left)<.1),mode+" About: brand, links and credit share the left inset");
            Check(details.GetVisualDescendants().OfType<TextBlock>().Where(t=>!t.GetVisualAncestors().OfType<Button>().Any()).All(t=>t.TextAlignment!=TextAlignment.Center),mode+" About: information text is left aligned");
            Check(page.GetVisualDescendants().OfType<Button>().Count(b=>b.Content is string text && (text=="GitHub" || text=="README"))==2,mode+" About: existing repository actions remain available");
        }
        if(name=="Library") {
            var source=(SidebarViewModel)view.SingersDataContext;
            Check(ReferenceEquals(view.FindControl<ItemsControl>("SingerListHost").ItemsSource,source.Singers),mode+" Library: detected singers use the actual sidebar collection");
            Check(view.GetVisualDescendants().OfType<StackPanel>().Any(c=>ReferenceEquals(c.DataContext,source) && c.IsVisible==!source.HasSingers),mode+" Library: singer empty state follows actual data");
        }
    }
    static int Main(string[] args) {
        AssemblyLoadContext.Default.Resolving+=(_,name)=>{
            string p=Path.Combine(AppContext.BaseDirectory,name.Name+".dll");
            return File.Exists(p)?AssemblyLoadContext.Default.LoadFromAssemblyPath(p):null;
        };
        try { Run(args[0]);return 0; }catch(Exception e){Console.Error.WriteLine(e);return 1;}
    }
    static void Run(string path) {
        output=path;Directory.CreateDirectory(path);Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        AudioSettings.Configure(44100,2);Preferences.Default.Language="zh-CN";Preferences.Default.ThemeName="Dark";
        Preferences.Default.ReduceMotion=true;Preferences.Default.PreRender=false;
        AppBuilder.Configure<App>().UseSkia().UseHarfBuzz().UseReactiveUI()
            .With(new FontManagerOptions{DefaultFamilyName="HarmonyOS Sans SC"})
            .UseHeadless(new AvaloniaHeadlessPlatformOptions{UseHeadlessDrawing=false}).SetupWithoutStarting();
        SynchronizationContext.SetSynchronizationContext(new AvaloniaSynchronizationContext(DispatcherPriority.Normal));
        App.SetLanguage("zh-CN");ThemeManager.Apply("Dark");
        DocManager.Inst.Initialize(Thread.CurrentThread,TaskScheduler.FromCurrentSynchronizationContext());
        DocManager.Inst.PostOnUIThread=action=>Dispatcher.UIThread.Post(action);
        PlaybackManager.Inst.AudioOutput=new OpenUtau.Audio.DummyAudioOutput();
        ToolsManager.Inst.Initialize();SingerManager.Inst.Initialize();
        shell=new MainWindow{Width=1440,Height=900};shell.Show();Settle();shell.ShowPreferences();Settle();
        var view=shell.FindControl<PreferencesView>("PreferencesHost");
        foreach(var mode in new[]{"dark","light","narrow"}) {
            ThemeManager.Apply(mode=="light"?"Light":"Dark");shell.Width=mode=="narrow"?800:1440;shell.Height=mode=="narrow"?720:900;Settle();
            foreach(var name in names) {
                Nav(view,name);Inspect(view,name,mode);Shot("preferences-"+name.ToLower()+"-"+mode);
                var scroll=view.FindControl<ScrollViewer>("SettingsScroll");
                if(scroll.Extent.Height>scroll.Viewport.Height+1) {
                    scroll.Offset=new Vector(0,10000);Settle();Shot("preferences-"+name.ToLower()+"-"+mode+"-bottom");
                }
            }
        }
        App.SetLanguage("en-US");Nav(view,"Appearance");Shot("preferences-appearance-narrow-en");
        Check(view.FindControl<TextBlock>("PageDescription").Text==ThemeManager.GetString("prefs.description.appearance"),"Category description updates after language changes");
        App.SetLanguage("zh-CN");shell.Width=1440;shell.Height=900;ThemeManager.Apply("Dark");Nav(view,"Audio");
        var original=view.FindControl<Control>("PagePlayback").GetVisualDescendants().OfType<Slider>().First();
        Nav(view,"Playback");shell.Width=800;Settle();shell.Width=1440;Settle();
        Check(ReferenceEquals(original,view.FindControl<Control>("PagePlayback").GetVisualDescendants().OfType<Slider>().First()),"Resizing retains the same bound controls");
        double prior=original.Value;
        original.Value=.4;Settle();
        var track=original.GetVisualDescendants().OfType<Track>().Single();
        Check(Math.Abs(track.Value-.4)<.001 && Math.Abs(((PreferencesViewModel)view.DataContext).PlayPosMarkerMargin-.4)<.001,"Slider changes update the actual preferences view model");
        original.Focus(NavigationMethod.Tab);
        original.RaiseEvent(new KeyEventArgs{RoutedEvent=InputElement.KeyDownEvent,Key=Key.Right});Settle();
        Check(original.Value>.4 && Math.Abs(track.Value-original.Value)<.001,"Slider keyboard adjustment retains native behavior");
        original.Value=.4;Settle();
        var thumb=original.GetVisualDescendants().OfType<Thumb>().Single();
        var start=thumb.TranslatePoint(new Point(thumb.Bounds.Width/2,thumb.Bounds.Height/2),shell).Value;
        shell.MouseDown(start,MouseButton.Left,RawInputModifiers.None);
        shell.MouseMove(new Point(start.X+original.Bounds.Width*.2,start.Y),RawInputModifiers.LeftMouseButton);Tick();
        shell.MouseUp(new Point(start.X+original.Bounds.Width*.2,start.Y),MouseButton.Left,RawInputModifiers.None);Settle();
        Check(original.Value>.4 && Math.Abs(((PreferencesViewModel)view.DataContext).PlayPosMarkerMargin-original.Value)<.001,"Slider drag updates the actual preferences value");
        original.Value=prior;Settle();
        Nav(view,"Editor");
        var toggle=view.FindControl<Control>("PageEditor").GetVisualDescendants().OfType<ToggleSwitch>().First();
        bool? priorToggle=toggle.IsChecked;
        toggle.Focus(NavigationMethod.Tab);
        toggle.RaiseEvent(new KeyEventArgs{RoutedEvent=InputElement.KeyDownEvent,Key=Key.Space});
        toggle.RaiseEvent(new KeyEventArgs{RoutedEvent=InputElement.KeyUpEvent,Key=Key.Space});Settle();
        Check(toggle.IsChecked!=priorToggle && ((PreferencesViewModel)view.DataContext).LyricsHelperBrackets==toggle.IsChecked,"Switch keyboard activation updates the actual preferences view model");
        toggle.IsChecked=priorToggle;Settle();
        Preferences.Default.ReduceMotion=false;Md3Motion.ApplyPreference();Nav(view,"Appearance");Settle(480);
        var shape=view.FindControl<MaterialMorph>("SettingsShape");
        Check(shape.Kind==MaterialShapeKind.Cookie7 && !shape.HasActiveAnimation,"Category shape settles to the selected target");
        Preferences.Default.ReduceMotion=true;Md3Motion.ApplyPreference();Nav(view,"General");
        Check(shape.Kind==MaterialShapeKind.Square && !shape.HasActiveAnimation,"Reduced motion changes the category shape without animation");
        File.WriteAllText(Path.Combine(output,"validation.json"),JsonSerializer.Serialize(new{checks,
            note="Real compiled Avalonia/Skia UI. Nine pages in dark/light/narrow layouts, plus scroll bottoms and English appearance. DummyAudioOutput; no actual audio test, plugin scan, filesystem setting action or preferences save was invoked."},new JsonSerializerOptions{WriteIndented=true}));
        view.ReleaseSession();DocManager.Inst.Project.Saved=true;shell.Close();
    }
}
