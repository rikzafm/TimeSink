using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace TimeSink.UI.Services;

/// <summary>
/// Eases mouse-wheel scrolling for every <see cref="ScrollViewer"/>: the wheel moves a target offset and each rendered frame
/// closes part of the gap, instead of jumping a fixed step. Call <see cref="Install"/> once at startup.
/// </summary>
public static class SmoothScroll
{
    private const double Ease = 0.22;       // fraction of the remaining distance covered per frame
    private const double WheelStep = 0.6;   // pixels of travel per wheel-delta unit (120 = one notch, so ~72 px)

    private sealed class State { public double Target; public bool Running; }
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<ScrollViewer, State> States = new();

    public static void Install() =>
        EventManager.RegisterClassHandler(typeof(ScrollViewer), UIElement.PreviewMouseWheelEvent, new MouseWheelEventHandler(OnWheel), true);

    private static void OnWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.Handled || sender is not ScrollViewer sv || sv.ScrollableHeight <= 0) return;
        if (Keyboard.Modifiers != ModifierKeys.None) return;

        var state = States.GetOrCreateValue(sv);
        if (!state.Running) state.Target = sv.VerticalOffset;
        state.Target = Math.Clamp(state.Target - e.Delta * WheelStep, 0, sv.ScrollableHeight);
        e.Handled = true;
        if (state.Running) return;

        state.Running = true;
        void Frame(object? s, EventArgs _)
        {
            var gap = state.Target - sv.VerticalOffset;
            if (!sv.IsLoaded || Math.Abs(gap) < 0.5)
            {
                if (sv.IsLoaded) sv.ScrollToVerticalOffset(state.Target);
                CompositionTarget.Rendering -= Frame;
                state.Running = false;
                return;
            }
            sv.ScrollToVerticalOffset(sv.VerticalOffset + gap * Ease);
        }
        CompositionTarget.Rendering += Frame;
    }
}
