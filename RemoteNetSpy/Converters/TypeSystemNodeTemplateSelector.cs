using System.Windows;
using System.Windows.Controls;
using RemoteNetSpy.Models;

namespace RemoteNetSpy.Converters;

public class TypeSystemNodeTemplateSelector : DataTemplateSelector
{
    public DataTemplate TypeTemplate { get; set; }
    public DataTemplate ErrorTemplate { get; set; }

    public override DataTemplate SelectTemplate(object item, DependencyObject container)
    {
        return item switch
        {
            ErrorNodeModel => ErrorTemplate,
            DumpedTypeModel => TypeTemplate,
            _ => base.SelectTemplate(item, container)
        };
    }
}
