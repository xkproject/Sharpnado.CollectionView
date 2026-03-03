using System;
using System.Threading.Tasks;

using Microsoft.Maui.Controls;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;

namespace Sharpnado.CollectionView.Helpers
{
    /// <summary>
    /// From: https://docs.microsoft.com/en-US/xamarin/xamarin-forms/user-interface/animation/custom.
    /// </summary>
    public static class ViewExtensions
    {
        public static Task<bool> ColorTo(this VisualElement self, Color fromColor, Color toColor, Action<Color> callback, uint length = 250, Easing easing = null)
        {
            Color Transform(double t) =>
                Color.FromRgba(
                    fromColor.Red + t * (toColor.Red - fromColor.Red),
                    fromColor.Green + t * (toColor.Green - fromColor.Green),
                    fromColor.Blue + t * (toColor.Blue - fromColor.Blue),
                    fromColor.Alpha + t * (toColor.Alpha - fromColor.Alpha));
            return ColorAnimation(self, "ColorTo", Transform, callback, length, easing);
        }

        public static void CancelAnimation(this VisualElement self)
        {
            self.AbortAnimation("ColorTo");
        }

        private static Task<bool> ColorAnimation(VisualElement element, string name, Func<double, Color> transform, Action<Color> callback, uint length, Easing easing)
        {
            easing = easing ?? Easing.Linear;
            var taskCompletionSource = new TaskCompletionSource<bool>();

            element.Animate<Color>(name, transform, callback, 16, length, easing, (v, c) => taskCompletionSource.SetResult(c));
            return taskCompletionSource.Task;
        }
    }
}
