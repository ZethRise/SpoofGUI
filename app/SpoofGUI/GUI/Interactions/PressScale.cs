using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.Foundation;

namespace SpoofGUI.GUI.Interactions;

public static class PressScale
{
    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.RegisterAttached(
            "IsEnabled", typeof(bool), typeof(PressScale), new PropertyMetadata(false, OnChanged));

    public static bool GetIsEnabled(DependencyObject element) => (bool)element.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);

    private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement element) return;

        element.PointerPressed -= OnPressed;
        element.PointerReleased -= OnReleased;
        element.PointerExited -= OnReleased;
        element.PointerCanceled -= OnReleased;
        element.PointerCaptureLost -= OnReleased;

        if (e.NewValue is true)
        {
            element.PointerPressed += OnPressed;
            element.PointerReleased += OnReleased;
            element.PointerExited += OnReleased;
            element.PointerCanceled += OnReleased;
            element.PointerCaptureLost += OnReleased;
        }
    }

    private static void OnPressed(object sender, PointerRoutedEventArgs e) => AnimateTo((UIElement)sender, 0.96, 90);

    private static void OnReleased(object sender, PointerRoutedEventArgs e) => AnimateTo((UIElement)sender, 1.0, 150);

    private static void AnimateTo(UIElement element, double target, int milliseconds)
    {
        if (element is not FrameworkElement fe) return;
        if (fe.RenderTransform is not ScaleTransform scale)
        {
            fe.RenderTransformOrigin = new Point(0.5, 0.5);
            scale = new ScaleTransform();
            fe.RenderTransform = scale;
        }

        var storyboard = new Storyboard();
        foreach (var property in new[] { "ScaleX", "ScaleY" })
        {
            var animation = new DoubleAnimation
            {
                To = target,
                Duration = TimeSpan.FromMilliseconds(milliseconds),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            };
            Storyboard.SetTarget(animation, scale);
            Storyboard.SetTargetProperty(animation, property);
            storyboard.Children.Add(animation);
        }

        storyboard.Begin();
    }
}
