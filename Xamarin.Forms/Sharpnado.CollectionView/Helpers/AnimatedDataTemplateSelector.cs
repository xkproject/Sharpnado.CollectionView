using System.Threading.Tasks;
using Xamarin.Forms;

namespace Sharpnado.CollectionView.Helpers
{
    public abstract class AnimatedDataTemplateSelector : DataTemplateSelector
    {
        public abstract Task AnimateSelectedDataTemplateAsync(object item, BindableObject container, ViewCell viewCell);
    }
}
