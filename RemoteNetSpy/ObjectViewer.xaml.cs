using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
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
            double multiplier = parent is ObjectViewer ? 1 : 0.9;
            this.Height = parent.Height * multiplier;
            this.Width = parent.Width * multiplier;

            _ro = ro;
            _type = _ro.GetType();

            objTypeTextBox.Text = _ro.GetType().FullName;
            objAddrTextBox.Text = $"0x{_ro.RemoteToken:x8}";

            DynamicRemoteObject dro = _ro.Dynamify() as DynamicRemoteObject;

            ObservableCollection<MembersGridItem> items = new ObservableCollection<MembersGridItem>();

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
                    mgi.Type = NormalizeGenericTypeFullName(fi.FieldType.ToString()); // Specifying expected type
                }
                if (member is PropertyInfo pi)
                {
                    mgi.MemberType = "Property";
                    mgi.Type = NormalizeGenericTypeFullName(pi.PropertyType.ToString()); // Specifying expected type
                }

                try
                {
                    GetMemberValue(dro, member, mgi);
                }
                catch (Exception ex)
                {
                    mgi.Value = $"ERROR: Couldn't read value (Exception thrown: {ex.Message})";
                }

                items.Add(mgi);
            }
           
            membersGrid.ItemsSource = items;
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
                        mgi.Type += " {" + NormalizeGenericTypeFullName(acutalType) +
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
            if(mgi.RawValue == null)
                return;
            RemoteObject? ro = mgi.RawValue as RemoteObject;
            if (mgi.RawValue is not RemoteObject)
                ro = (mgi.RawValue as DynamicRemoteObject)?.__ro;

            if (ro == null)
            {
                MessageBox.Show("Value is not a Remote Object.\nHere's a ToString():\n" + ro);
            }

            (new ObjectViewer(this, ro)).ShowDialog();
        }

        private void UIElement_OnPreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            var parent = (sender as TextBlock).Parent as Grid;
            TextBox? tBox = parent?.Children.Cast<UIElement>().Single(c => c is TextBox) as TextBox;
            if(tBox == null)
                return;
            tBox.Visibility = Visibility.Visible;
            tBox.Focus();
        }

        private static string NormalizeGenericTypeFullName(string fullName)
        {
            // Use a regular expression to match and replace the extra type information
            string parsedFullName = Regex.Replace(fullName, @",\s*[^,]+\s*,\s*Version=\d+\.\d+\.\d+\.\d+\s*,\s*Culture=\w+\s*,\s*PublicKeyToken=\w+", "");

            // Use another regular expression to match and replace the square brackets
            parsedFullName = parsedFullName.Replace("[[", "<");
            parsedFullName = parsedFullName.Replace("]]", ">");
            parsedFullName = Regex.Replace(parsedFullName, @"<([^<>]*)\],\[([^<>]*)>", "<$1, $2>");

            // Use another regular expression to match and remove the backtick and number after it
            parsedFullName = Regex.Replace(parsedFullName, @"\`\d", "");

            // Output the parsed full name of the generic type, which should be "System.Collections.Generic.List<System.Threading.Tasks.Task<System.Collections.Generic.Dictionary<System.Int32, System.String>>>"
            return parsedFullName;
        }

        private void UIElement_OnLostFocus(object sender, RoutedEventArgs e)
        {
            (sender as TextBox).Visibility = Visibility.Hidden;
        }
    }
}
