using RemoteNET;
using RemoteNET.Common;
using RemoteNET.Internal;
using RemoteNET.Internal.Reflection;
using RemoteNET.Internal.Reflection.DotNet;
using RemoteNetSpy.Controls;
using RemoteNetSpy.Models;
using RnetKit.Common;
using System;
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
using System.Xml.Linq;

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

        public ObjectViewer(Window parent, RemoteAppModel appModel, HeapObjectViewModel ho)
        {
            InitializeComponent();
            double multiplier = parent is ObjectViewer ? 1 : 0.9;
            if (parent != null)
            {
                this.Height = Math.Max(parent.Height * multiplier, 800);
                this.Width = Math.Max(parent.Width * multiplier, 1200);
            }
            objViewerControl.Init(this, appModel, ho);
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

        private void CloseButtonClicked(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void dockButton_Click(object sender, RoutedEventArgs e)
        {
            // Find the MainWindow
            var mainWindow = Application.Current.MainWindow as MainWindow;
            if (mainWindow == null)
            {
                MessageBox.Show("Could not find main window.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // Get the heap object from the control
            var heapObject = objViewerControl.GetHeapObject();
            
            // Get the control reference before removing it
            var control = objViewerControl;
            
            // Create new tab in main window with existing control
            mainWindow.CreateNewInstanceTabWithControl(heapObject, control);

            // Close this ObjectViewer window
            this.Close();
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
                        var arguments = PromptForArguments(memInfo.GetParameters(), memInfo.Name);
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

        private object[] PromptForArguments(ParameterInfo[] parameters, string methodName)
        {
            ArgumentPromptWindow promptWindow = new ArgumentPromptWindow(parameters, _appModel.App, methodName);
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
            CreateViewerWindow(this, _appModel, obj).Show();
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
                    // TODO: I hope it's ok to create this VM here
                    var ho = new HeapObjectViewModel()
                    {
                        FullTypeName = remoteType?.FullName,
                        Address = ro.RemoteToken,
                        RemoteObject = ro
                    };
                    return new ObjectViewer(parent, appModel, ho);
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
