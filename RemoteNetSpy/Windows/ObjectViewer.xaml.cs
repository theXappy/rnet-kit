﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using RemoteNET;
using RemoteNET.Common;
using RemoteNET.Internal;
using RemoteNET.Internal.Reflection;
using RemoteNET.Internal.Reflection.DotNet;
using RemoteNetSpy.Models;
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

        private RemoteAppModel _appModel;

        private ObjectViewer(Window parent, RemoteAppModel appModel, RemoteObject ro)
        {
            _appModel = appModel;

            InitializeComponent();
            double multiplier = parent is ObjectViewer ? 1 : 0.9;
            this.Height = Math.Max(parent.Height * multiplier, 800);
            this.Width = Math.Max(parent.Width * multiplier, 1200);

            _ro = ro;
            _type = _ro.GetType();

            objTypeTextBox.Text = TypeNameUtils.Normalize(_ro.GetType().FullName);
            objAddrTextBox.Text = $"0x{_ro.RemoteToken:x8}";

            DynamicRemoteObject dro = _ro.Dynamify() as DynamicRemoteObject;

            List<MembersGridItem> tempItems = new List<MembersGridItem>();
            List<MemberInfo> ordered = _type.GetMembers(~(BindingFlags.DeclaredOnly)).OrderBy(m => m.Name).ToList();
            foreach (MemberInfo member in ordered)
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
                        mgi.Type = TypeNameUtils.Normalize(rmi.LazyRetType.TypeName);
                    }
                    else
                    {
                        // TODO: This triggers a cascade of recursive calls to the resolve remote Types.
                        mgi.Type = TypeNameUtils.Normalize(mi.ReturnType.ToString());
                    }

                    mgi.MemberType = "Method";
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
                    mgi.RawValue = ex;
                    mgi.Value = ex.Message;
                    mgi.IsThrownException = true;
                }
                tempItems.Add(mgi);
            }

            _items = new ObservableCollection<MembersGridItem>(tempItems);

            // Try to spot IEnumerables
            IEnumerable<MemberInfo> methods = _type.GetMethods(~(BindingFlags.DeclaredOnly));
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

        private static void GetMemberValue(DynamicRemoteObject dro, MemberInfo member, MembersGridItem mgi)
        {
            if (member is FieldInfo || member is PropertyInfo)
            {
                GetFieldPropValue(dro, member, mgi);
            }
            else if (member is MethodInfo mi)
            {
                bool isDotNetInvokable = mi.GetParameters().Length == 0;
                bool isMsvcInvokable = mi is RemoteRttiMethodInfo;
                if (isDotNetInvokable || isMsvcInvokable)
                {
                    mgi.Value = "Invokable!";
                }
                else
                {
                    mgi.Value = "<Argumented functions not supported for .NET targets>";
                }
            }
        }

        private static void GetFieldPropValue(DynamicRemoteObject dro, MemberInfo field, MembersGridItem mgi)
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

            private bool _isThrownException;
            public bool IsThrownException
            {
                get => _isThrownException;
                set
                {
                    _isThrownException = value;
                    NotifyPropertyChanged(nameof(IsThrownException));
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
            try
            {
                if (memInfo.GetParameters().Length == 0)
                {
                    object results = memInfo.Invoke(_ro, Array.Empty<object>());
                    mgi.RawValue = results;
                    mgi.Value = results?.ToString() ?? "null";
                    mgi.IsThrownException = false;
                }
                else
                {
                    if (_ro is UnmanagedRemoteObject)
                    {
                        var arguments = PromptForArguments(memInfo.GetParameters().Length);
                        if (arguments != null)
                        {
                            object results = memInfo.Invoke(_ro, arguments);
                            mgi.RawValue = results;
                            mgi.Value = results?.ToString() ?? "null";
                            mgi.IsThrownException = false;
                        }
                    }
                    else
                    {
                        mgi.Value = "<Argumented functions not supported>";
                    }
                }
            }
            catch (Exception ex)
            {
                mgi.RawValue = ex;
                mgi.Value = ex.Message.ToString();
                mgi.IsThrownException = true;
            }
        }

        private object[] PromptForArguments(int parameterCount)
        {
            ArgumentPromptWindow promptWindow = new ArgumentPromptWindow(parameterCount);
            if (promptWindow.ShowDialog() == true)
            {
                return promptWindow.Arguments;
            }
            return null;
        }

        private void InspectClicked(object sender, RoutedEventArgs e)
        {
            MembersGridItem mgi = (sender as Button)?.DataContext as MembersGridItem;
            if (mgi == null)
                return;
            if (mgi.RawValue == null)
                return;
            RemoteObject ro = mgi.RawValue as RemoteObject;
            if (mgi.RawValue is not RemoteObject)
                ro = (mgi.RawValue as DynamicRemoteObject)?.__ro;


            // Make sure we forward either the RO or the primitive (this is mostly here for strings)
            object obj = mgi.RawValue;
            if (ro != null)
            {
                obj = ro;
            }

            //MessageBox.Show("Value is not a Remote Object.\nHere's a ToString():\n" + ro);
            CreateViewerWindow(this, _appModel, obj).ShowDialog();
        }

        private void ViewMemoryClicked(object sender, RoutedEventArgs e)
        {
            MembersGridItem mgi = (sender as Button)?.DataContext as MembersGridItem;
            if (mgi == null)
                return;
            if (mgi.RawValue == null)
                return;
            ulong addr;
            if (mgi.RawValue is UIntPtr uPtr)
            {
                addr = (ulong)uPtr;
            }
            else if (mgi.RawValue is string strVal)
            {
                try
                {
                    addr = ulong.Parse(strVal);
                }
                catch
                {
                    MessageBox.Show($"Could not convert raw value {mgi.RawValue} to a ulong.", "Error");
                    return;
                }
            }
            else // int? long? ulong? idk
            {
                try
                {
                    addr = Convert.ToUInt64(mgi.RawValue);
                }
                catch
                {
                    MessageBox.Show($"Could not convert raw value {mgi.RawValue} to a ulong.", "Error");
                    return;
                }
            }

            MemoryViewWindow mvw = new MemoryViewWindow(_appModel, addr);
            mvw.Show();
        }

        private void UIElement_OnPreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            var parent = (sender as TextBlock).Parent as Grid;
            TextBox tBox = parent?.Children.Cast<UIElement>().Single(c => c is TextBox) as TextBox;
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


        public static Window CreateViewerWindow(Window parent, RemoteAppModel appModel, object obj)
        {
            if (obj is RemoteObject ro)
            {
                // Ugly Hack
                Type remoteType = ro.GetRemoteType();
                if (remoteType?.FullName == "System.Byte[]")
                {
                    byte[] LocalBytes = (byte[])(ro.Dynamify() as DynamicRemoteObject);
                    StringBuilder builder = new StringBuilder();
                    for (int i = 0; i < LocalBytes.Length; i++)
                    {
                        if (i % 16 == 0 && i != 0)
                        {
                            builder.AppendLine();
                        }

                        builder.Append($"{LocalBytes[i]:X2} ");
                    }
                    return new StringObjectViewer(parent, remoteType, builder.ToString());
                }
                else
                {
                    return new ObjectViewer(parent, appModel, ro);
                }
            }
            if (obj is string str)
                return new StringObjectViewer(parent, typeof(string), str);
            throw new Exception($"Unsupported object type to view. Type: {obj.GetType().Name}");
        }

        private void memoryViewButton_Click(object sender, RoutedEventArgs e)
        {
            ulong address = _ro.RemoteToken;

            MemoryViewWindow mvw = new MemoryViewWindow(_appModel, address);
            mvw.Show();
        }
    }
}
