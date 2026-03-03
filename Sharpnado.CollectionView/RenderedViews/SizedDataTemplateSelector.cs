using Microsoft.Maui.Controls;
using Microsoft.Maui;

namespace Sharpnado.CollectionView.RenderedViews
{
    public abstract class SizedDataTemplateSelector : DataTemplateSelector
    {
        public abstract double GetItemSize(object item, double defaultSize);
    }
}