using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace RemoteNetSpy.Controls.StretchingTree
{
    public class StretchingTreeView : TreeView
    {
        protected override DependencyObject GetContainerForItemOverride()
        {
            return new StretchingTreeViewItem();
        }

        protected override bool IsItemItsOwnContainerOverride(object item)
        {
            return item is StretchingTreeViewItem;
        }
    }
}
