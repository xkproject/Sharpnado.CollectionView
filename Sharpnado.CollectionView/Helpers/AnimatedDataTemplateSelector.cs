using System.Threading.Tasks;
using Microsoft.Maui.Controls;
using Microsoft.Maui;

namespace Sharpnado.CollectionView.Helpers
{
    public abstract class AnimatedDataTemplateSelector : DataTemplateSelector
    {
        public abstract Task AnimateSelectedDataTemplateAsync(object item, BindableObject container, ViewCell viewCell);
    }
}
