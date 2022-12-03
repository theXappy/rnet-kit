using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using RemoteNET;
using RemoteNET.Internal;

namespace RemoteNetSpy
{
    /// <summary>
    /// Interaction logic for ObjectViewer.xaml
    /// </summary>
    public partial class ObjectViewer : Window
    {
        private RemoteObject _ro;
        private Type _type;

        public ObjectViewer(Window parent, RemoteObject ro)
        {
            InitializeComponent();
            this.Height = parent.Height * 0.9;
            this.Width = parent.Width * 0.9;

            _ro = ro;
            _type = _ro.GetType();

            objTypeTextBox.Text = _ro.GetType().FullName;
            objAddrTextBox.Text = $"0x{_ro.RemoteToken:x8}";

            DynamicRemoteObject dro = _ro.Dynamify() as DynamicRemoteObject;

            ObservableCollection<MembersGridItem> items = new ObservableCollection<MembersGridItem>();
            foreach (FieldInfo field in _type.GetFields((BindingFlags)0xffff))
            {
                MembersGridItem mgi = new MembersGridItem()
                {
                    MemberType = "Field",
                    Name = field.Name,
                    Type = field.FieldType.ToString() // Specifying expected type
                };
                if (DynamicRemoteObject.TryGetDynamicMember(dro, field.Name, out dynamic res))
                {
                    mgi.Value = res.ToString();
                    string acutalType = res.GetType().FullName;
                    if (mgi.Type != acutalType)
                    {
                        mgi.Type += "\t{" + acutalType + '}'; // Specifying actual type if it's different (might be a subclass)
                    }
                }
                else
                {
                    mgi.Value = "ERROR: Couldn't read value";
                }
                items.Add(mgi);
            }

            membersGrid.ItemsSource = items;
        }

        public class MembersGridItem
        {
            public string MemberType { get; set; }
            public string Name { get; set; }
            public string Value { get; set; }
            public string Type { get; set; }
        }
    }
}
