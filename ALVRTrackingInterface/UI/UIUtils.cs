//using System;
//using System.Collections.Generic;
//using System.Linq;
//using System.Text;
//using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows;

namespace ALVRTrackingInterface.UI
{
    public static class UIHelper
    {
        public static TabControl FindTabControl(DependencyObject obj)
        {
            if (obj == null)
                return null;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(obj); ++i)
            {
                var child = VisualTreeHelper.GetChild(obj, i);
                if (child is TabControl tabControl)
                    return tabControl;
                else
                {
                    var result = FindTabControl(child);
                    if (result != null)
                        return result;
                }
            }
            return null;
        }

        public static TabControl FindParentTabControl(DependencyObject obj)
        {
            if (obj == null)
                return null;
            var parent = VisualTreeHelper.GetParent(obj);
            while (parent != null && !(parent is TabControl))
            {
                parent = VisualTreeHelper.GetParent(parent);
            }
            return parent as TabControl;
        }

        public static void SwitchToMainTab(DependencyObject obj)
        {
            if (obj == null)
                return;
            var tabControl = UIHelper.FindParentTabControl(obj);
            if (tabControl == null)
                return;
            int selectedIndex = 0;
            for (int i = 0; i < tabControl.Items.Count; ++i)
            {
                var tabItem = tabControl.Items[i] as TabItem;
                if (tabItem != null)
                {
                    var headerTitle = tabItem.Header as string;
                    if (headerTitle != null && headerTitle.ToLower() == "main")
                    {
                        selectedIndex = i;
                        break;
                    }
                }
            }
            tabControl.SelectedIndex = selectedIndex;
        }
    }
}
