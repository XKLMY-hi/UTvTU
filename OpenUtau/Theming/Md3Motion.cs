using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using OpenUtau.Core.Util;

namespace OpenUtau.Theming;

/// <summary>
/// Standard M3 spring response, evaluated by Avalonia's built-in transitions.
/// Official damping/stiffness: androidx compose material3 tokens/StandardMotionTokens.kt.
/// This finite interpolation uses a 0.001 settling envelope, starts/ends exactly at the target,
/// and does not replace transforms, subscribe to a render clock, or animate editor data.
/// </summary>
public sealed class Md3StandardSpringEasing : Easing {
    public double Stiffness { get; set; } = 700;
    public double Damping { get; set; } = 0.9;

    public static TimeSpan SettlingDuration(double stiffness, double damping) {
        double omega = Math.Sqrt(Math.Max(1, stiffness));
        double ratio = Math.Clamp(damping, 0.1, 1);
        // Critical damping: (1+x) exp(-x) < 0.001 at x=9.234.
        double time = ratio >= 1 ? 9.234 / omega :
            -Math.Log(0.001 * Math.Sqrt(1 - ratio * ratio)) / (ratio * omega);
        return TimeSpan.FromSeconds(time);
    }

    public override double Ease(double progress) {
        if (progress <= 0) return 0;
        if (progress >= 1) return 1;
        double omega = Math.Sqrt(Math.Max(1, Stiffness));
        double ratio = Math.Clamp(Damping, 0.1, 1);
        double seconds = SettlingDuration(Stiffness, Damping).TotalSeconds;
        double Response(double time) {
            if (ratio >= 1) return 1 - (1 + omega * time) * Math.Exp(-omega * time);
            double damped = omega * Math.Sqrt(1 - ratio * ratio);
            return 1 - Math.Exp(-ratio * omega * time) *
                (Math.Cos(damped * time) + ratio * omega / damped * Math.Sin(damped * time));
        }
        double value = Response(progress * seconds) / Response(seconds);
        // Effects must never overshoot color/opacity; spatial springs can have minimal bounce.
        return ratio >= 1 ? Math.Clamp(value, 0, 1) : value;
    }
}

/// <summary>One live motion preference for transitions, popup mounts and busy indicators.</summary>
public sealed class Md3Motion : AvaloniaObject {
    public static readonly AttachedProperty<bool> ReducedProperty =
        AvaloniaProperty.RegisterAttached<Md3Motion, Control, bool>("Reduced", inherits: true);
    static readonly List<WeakReference<Window>> windows = new();
    public static bool GetReduced(Control control) => control.GetValue(ReducedProperty);
    public static void SetReduced(Control control, bool value) => control.SetValue(ReducedProperty, value);

    static Md3Motion() {
        ReducedProperty.Changed.AddClassHandler<Control>((control, _) =>
            ((IPseudoClasses)control.Classes).Set(":reduced-motion", GetReduced(control)));
    }

    public static TimeSpan EffectsFast => Duration(3800, 1);
    public static TimeSpan EffectsDefault => Duration(1600, 1);
    public static TimeSpan EffectsSlow => Duration(800, 1);
    public static TimeSpan SpatialFast => Duration(1400, 0.9);
    public static TimeSpan SpatialDefault => Duration(700, 0.9);
    public static TimeSpan SpatialSlow => Duration(300, 0.9);
    static TimeSpan Duration(double stiffness, double damping) => Preferences.Default.ReduceMotion
        ? TimeSpan.Zero : Md3StandardSpringEasing.SettlingDuration(stiffness, damping);

    public static void Register(Window window) {
        windows.Add(new WeakReference<Window>(window));
        window.Classes.Set("no-motion", Preferences.Default.ReduceMotion);
        SetReduced(window, Preferences.Default.ReduceMotion);
    }

    public static void ApplyPreference() {
        if (Application.Current is { } app) {
            app.Resources["Md3ReducedMotion"] = Preferences.Default.ReduceMotion;
            app.Resources["Md3MotionEffectsFast"] = EffectsFast;
            app.Resources["Md3MotionEffectsDefault"] = EffectsDefault;
            app.Resources["Md3MotionEffectsSlow"] = EffectsSlow;
            app.Resources["Md3MotionSpatialFast"] = SpatialFast;
            app.Resources["Md3MotionSpatialDefault"] = SpatialDefault;
            app.Resources["Md3MotionSpatialSlow"] = SpatialSlow;
        }
        for (int i = windows.Count - 1; i >= 0; i--) {
            if (!windows[i].TryGetTarget(out var window)) { windows.RemoveAt(i); continue; }
            window.Classes.Set("no-motion", Preferences.Default.ReduceMotion);
            SetReduced(window, Preferences.Default.ReduceMotion);
        }
    }
}
