using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using RemoteNET;
using RemoteNET.Internal;
using RnetKit.Common;

namespace RemoteNetSpy
{
    /// <summary>
    /// Interaction logic for ObjectViewer.xaml
    /// </summary>
    public partial class ObjectViewer : Window
    {
        private RemoteObject _ro;
        private Type _type;
        ObservableCollection<MembersGridItem> _items;


        private ObjectViewer(Window parent, RemoteObject ro)
        {
            InitializeComponent();
            double multiplier = parent is ObjectViewer ? 1 : 0.9;
            this.Height = parent.Height * multiplier;
            this.Width = parent.Width * multiplier;

            _ro = ro;
            _type = _ro.GetType();

            objTypeTextBox.Text = TypeNameUtils.Normalize(_ro.GetType().FullName);
            objAddrTextBox.Text = $"0x{_ro.RemoteToken:x8}";

            DynamicRemoteObject dro = _ro.Dynamify() as DynamicRemoteObject;

            _items = new ObservableCollection<MembersGridItem>();

            IEnumerable<MemberInfo> fields = _type.GetFields((BindingFlags)0xffff);
            IEnumerable<MemberInfo> props = _type.GetProperties((BindingFlags)0xffff);

            foreach (MemberInfo member in fields.Concat(props).OrderBy(m => m.Name))
            {
                MembersGridItem mgi = new MembersGridItem()
                {
                    Name = member.Name,
                };
                if (member is FieldInfo fi)
                {
                    mgi.MemberType = "Field";
                    mgi.Type = TypeNameUtils.Normalize(fi.FieldType.ToString()); // Specifying expected type
                }

                if (member is PropertyInfo pi)
                {
                    mgi.MemberType = "Property";
                    mgi.Type = TypeNameUtils.Normalize(pi.PropertyType.ToString()); // Specifying expected type
                }

                try
                {
                    GetMemberValue(dro, member, mgi);
                }
                catch (Exception ex)
                {
                    mgi.Value = $"ERROR: Couldn't read value (Exception thrown: {ex.Message})";
                }

                _items.Add(mgi);
            }

            // Try to spot IEnumerables
            IEnumerable<MemberInfo> methods = _type.GetMethods((BindingFlags)0xffff);
            if (methods.Any(mi => mi.Name == "GetEnumerator"))
            {
                MembersGridItem iEnumerableMgi = new MembersGridItem()
                {
                    MemberType = "Field",
                    Name = "Raw View",
                    Value = "",
                    Type = "IEnumerable",
                };
                _items.Add(iEnumerableMgi);
            }

            membersGrid.ItemsSource = _items;
        }

        private static void GetMemberValue(DynamicRemoteObject? dro, MemberInfo field, MembersGridItem mgi)
        {
            if (DynamicRemoteObject.TryGetDynamicMember(dro, field.Name, out dynamic res))
            {
                mgi.RawValue = res;
                if (res != null)
                {
                    mgi.Value = res.ToString();
                    string acutalType = res.GetType().FullName;
                    if (mgi.Type != acutalType)
                    {
                        // Specifying actual type if it's different (might be a subclass)
                        mgi.Type += " {" + TypeNameUtils.Normalize(acutalType) +
                                    '}';
                    }

                    // TODO: Ugly hack...
                    if (mgi.Type == "System.Byte[]")
                    {
                        byte[] LocalBytes = (res as DynamicRemoteObject).Cast<byte>().ToArray();
                        mgi.Value += " { ";
                        bool first = true;
                        foreach (byte localByte in LocalBytes)
                        {
                            if (!first)
                                mgi.Value += ", ";
                            first = false;
                            mgi.Value += "0x";
                            mgi.Value += localByte.ToString("X2");
                        }
                        mgi.Value += " }";
                    }
                }
                else
                {
                    mgi.Value = "null";
                }
            }
            else
            {
                mgi.Value = "ERROR: Couldn't read value";
            }
        }

        public class MembersGridItem
        {
            public object RawValue { get; set; }

            public string MemberType { get; set; }
            public string Name { get; set; }
            public string Value { get; set; }
            public string Type { get; set; }
        }

        private void CloseButtonClicked(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void InspectClicked(object sender, RoutedEventArgs e)
        {
            MembersGridItem mgi = (sender as Button)?.DataContext as MembersGridItem;
            if (mgi == null)
                return;
            if (mgi.RawValue == null)
                return;
            RemoteObject? ro = mgi.RawValue as RemoteObject;
            if (mgi.RawValue is not RemoteObject)
                ro = (mgi.RawValue as DynamicRemoteObject)?.__ro;

            if (ro == null)
            {
                MessageBox.Show("Value is not a Remote Object.\nHere's a ToString():\n" + ro);
            }

            (ObjectViewer.CreateViewerWindow(this, ro)).ShowDialog();
        }

        private void UIElement_OnPreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            var parent = (sender as TextBlock).Parent as Grid;
            TextBox? tBox = parent?.Children.Cast<UIElement>().Single(c => c is TextBox) as TextBox;
            if (tBox == null)
                return;
            tBox.Visibility = Visibility.Visible;
            tBox.Focus();
        }



        private void UIElement_OnLostFocus(object sender, RoutedEventArgs e)
        {
            (sender as TextBox).Visibility = Visibility.Hidden;
        }

        private void EnumerateRawValueButton_OnClick(object sender, RoutedEventArgs e)
        {
            // TODO: Add children to that _items source thingy
            dynamic dro = _ro.Dynamify();
            int i = 0;
            foreach (dynamic item in dro)
            {
                MembersGridItem itemMgi = new MembersGridItem()
                {
                    MemberType = "Field",
                    Name = $"Raw View[{i}]",
                    RawValue = item,
                    Value = item?.ToString() ?? "null",
                    Type = "???",
                };
                try
                {
                    itemMgi.Type = TypeNameUtils.Normalize(item.GetType().FullName);
                }
                catch
                {
                }

                _items.Add(itemMgi);
                i++;
            }
            
            (sender as Button).IsEnabled = false;
        }


        public static Window CreateViewerWindow(Window parent, RemoteObject ro)
        {
            return new ObjectViewer(parent, ro);
        }
    }
}
