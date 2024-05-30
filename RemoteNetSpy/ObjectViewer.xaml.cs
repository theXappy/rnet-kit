using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using RemoteNET;
using RemoteNET.Common;
using RemoteNET.Internal;
using RemoteNET.Internal.Reflection;
using RemoteNET.Internal.Reflection.DotNet;
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

            List<MembersGridItem> tempItems = new List<MembersGridItem>();
            foreach (MemberInfo member in _type.GetMembers((BindingFlags)0xffff).OrderBy(m => m.Name))
            {
                MembersGridItem mgi = new MembersGridItem(member)
                {
                    Name = member.Name,
                };
                if (member is FieldInfo fi)
                {
                    mgi.MemberType = "Field";
                    mgi.Type = TypeNameUtils.Normalize(fi.FieldType.ToString()); // Specifying expected type
                }
                else if (member is PropertyInfo pi)
                {
                    mgi.MemberType = "Property";
                    mgi.Type = TypeNameUtils.Normalize(pi.PropertyType.ToString()); // Specifying expected type
                }
                else if (member is MethodInfo mi)
                {
                    if (mi is IRttiMethodBase rmi)
                    {
                        mgi.Name = rmi.UndecoratedSignature;
                    }

                    mgi.MemberType = "Method";
                    mgi.Type = TypeNameUtils.Normalize(mi.ReturnType.ToString()); // Specifying expected type
                }
                else
                {
                    continue;
                }

                try
                {
                    GetMemberValue(dro, member, mgi);
                }
                catch (Exception ex)
                {
                    mgi.Value = $"ERROR: Couldn't read value (Exception thrown: {ex.Message})";
                }
                tempItems.Add(mgi);
            }

            tempItems.Sort((member1, member2) => member1.Name.CompareTo(member2.Name));

            _items = new ObservableCollection<MembersGridItem>(tempItems);


            // Try to spot IEnumerables
            IEnumerable<MemberInfo> methods = _type.GetMethods((BindingFlags)0xffff);
            if (methods.Any(mi => mi.Name == "GetEnumerator"))
            {
                MembersGridItem iEnumerableMgi = new MembersGridItem(null)
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

        private static void GetMemberValue(DynamicRemoteObject? dro, MemberInfo member, MembersGridItem mgi)
        {
            if (member is FieldInfo || member is PropertyInfo)
            {
                GetFieldPropValue(dro, member, mgi);
            }
            else if (member is MethodInfo mi)
            {
                if (mi.GetParameters().Length == 0)
                {
                    mgi.Value = "Invokable!";
                }
                else
                {
                    mgi.Value = "<Argumented functions not supported>";
                }
            }
        }

        private static void GetFieldPropValue(DynamicRemoteObject? dro, MemberInfo field, MembersGridItem mgi)
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
                        byte[] LocalBytes = (byte[])(res as DynamicRemoteObject);
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

        [DebuggerDisplay("MemberGridItem: {MemberType} {Name}")]
        public class MembersGridItem : INotifyPropertyChanged
        {
            private object _rawValue;
            public object RawValue
            {
                get { return _rawValue; }
                set
                {
                    if (_rawValue != value)
                    {
                        _rawValue = value;
                        NotifyPropertyChanged(nameof(RawValue));
                    }
                }
            }

            private string _memberType;
            public string MemberType
            {
                get { return _memberType; }
                set
                {
                    if (_memberType != value)
                    {
                        _memberType = value;
                        NotifyPropertyChanged(nameof(MemberType));
                    }
                }
            }

            private string _name;
            public string Name
            {
                get { return _name; }
                set
                {
                    if (_name != value)
                    {
                        _name = value;
                        NotifyPropertyChanged(nameof(Name));
                    }
                }
            }

            private string _value;
            public string Value
            {
                get { return _value; }
                set
                {
                    if (_value != value)
                    {
                        _value = value;
                        NotifyPropertyChanged(nameof(Value));
                    }
                }
            }

            private string _type;
            public string Type
            {
                get { return _type; }
                set
                {
                    if (_type != value)
                    {
                        _type = value;
                        NotifyPropertyChanged(nameof(Type));
                    }
                }
            }

            private MemberInfo _memInfo;

            public MembersGridItem(MemberInfo memInfo)
            {
                _memInfo = memInfo;
            }

            public MemberInfo GetOriginalMemberInfo() => _memInfo;

            public event PropertyChangedEventHandler PropertyChanged;

            protected void NotifyPropertyChanged(string propertyName)
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            }
        }

        private void CloseButtonClicked(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void InvokeClicked(object sender, RoutedEventArgs e)
        {
            MembersGridItem mgi = (sender as Button)?.DataContext as MembersGridItem;
            MethodInfo memInfo = mgi.GetOriginalMemberInfo() as MethodInfo;
            object results = memInfo.Invoke(_ro, Array.Empty<object?>());
            mgi.RawValue = results;
            mgi.Value = results.ToString();
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


            // Make sure we forward either the RO or the primitive (this is mostly here for strings)
            object obj = mgi.RawValue;
            if (ro != null)
            {
                obj = ro;
            }

            //MessageBox.Show("Value is not a Remote Object.\nHere's a ToString():\n" + ro);
            CreateViewerWindow(this, obj).ShowDialog();
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
                MembersGridItem itemMgi = new MembersGridItem(null)
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


        public static Window CreateViewerWindow(Window parent, object obj)
        {
            if(obj is RemoteObject ro)
                return new ObjectViewer(parent, ro);
            if (obj is string str)
                return new StringObjectViewer(parent, str);
            throw new Exception($"Unsupported object type to view. Type: {obj.GetType().Name}");
        }
    }
}
