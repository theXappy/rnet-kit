using RemoteNET;
using RemoteNET.Internal.Reflection;
using RemoteNetSpy;
using RemoteNetSpy.Models;
using RnetKit.Common;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace RemoteNetSpy.Controls
{
    /// <summary>
    /// Interaction logic for InstanceView.xaml
    /// </summary>
    public partial class InstanceView : UserControl
    {
        private RemoteAppModel _remoteAppModel;
        private HeapObjectViewModel _heapObject;
        private ObservableCollection<MembersGridItem> _items;

        public InstanceView()
        {
            InitializeComponent();
        }

        public void Init(RemoteAppModel remoteAppModel, HeapObjectViewModel heapObject)
        {
            _remoteAppModel = remoteAppModel;
            _heapObject = heapObject;
            DataContext = _heapObject;
            RefreshMembersGrid();
        }

        private void RefreshMembersGrid()
        {
            try
            {
                if (_heapObject == null || _heapObject.RemoteObject == null)
                {
                    MessageBox.Show("Object is no longer available.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                var remoteObject = _heapObject.RemoteObject;
                var type = remoteObject.GetType();
                var dro = remoteObject.Dynamify() as DynamicRemoteObject;

                List<MembersGridItem> tempItems = new List<MembersGridItem>();
                List<MemberInfo> ordered = type.GetMembers(~(BindingFlags.DeclaredOnly)).OrderBy(m => m.Name).ToList();
                foreach (MemberInfo member in ordered)
                {
                    var mgi = new MembersGridItem(member)
                    {
                        Name = member.Name,
                    };
                    if (member is FieldInfo fi)
                    {
                        mgi.MemberType = "Field";
                        mgi.Type = TypeNameUtils.Normalize(fi.FieldType.ToString());
                    }
                    else if (member is PropertyInfo pi)
                    {
                        mgi.MemberType = "Property";
                        mgi.Type = TypeNameUtils.Normalize(pi.PropertyType.ToString());
                    }
                    else if (member is MethodInfo mi)
                    {
                        mgi.Type = TypeNameUtils.Normalize(mi.ReturnType.ToString());
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
                //membersGrid.ItemsSource = _items;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error refreshing members: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
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
                        mgi.Type += " {" + TypeNameUtils.Normalize(acutalType) + '}';
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

        private void refreshButton_Click(object sender, RoutedEventArgs e)
        {
            RefreshMembersGrid();
        }

        private async void castTypeButton_Click(object sender, RoutedEventArgs e)
        {
            await PromptForVariableCastInnerAsync(_heapObject);
            RefreshMembersGrid();
        }

        private async Task PromptForVariableCastInnerAsync(HeapObjectViewModel heapObject)
        {
            await _remoteAppModel.PromptForVariableCastAsync(heapObject, Dispatcher);
        }

        private async Task<List<DumpedTypeModel>> GetTypesListAsync(bool all)
        {
            IEnumerable<DumpedTypeModel> types = null;
            if (all)
            {
                await Task.Run(() =>
                {
                    types = _remoteAppModel.ClassesModel.Assemblies.SelectMany(a => a.Types);
                });
            }
            else
            {
                throw new ArgumentException();
            }

            var tempList = types.ToHashSet().ToList();
            tempList.Sort((dt1, dt2) => dt1.FullTypeName.CompareTo(dt2.FullTypeName));
            return tempList;
        }
    }
}
