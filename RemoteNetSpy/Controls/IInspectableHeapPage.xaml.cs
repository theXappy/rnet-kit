using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Xml.Linq;
using CliWrap;
using CliWrap.Buffered;
using RemoteNET;
using RemoteNetSpy.Models;

namespace RemoteNetSpy.Controls
{
    /// <summary>
    /// Interaction logic for IInspectableHeapPage.xaml
    /// </summary>
    public partial class IInspectableHeapPage : UserControl
    {
        private List<HeapObjectViewModel> _instancesList;

        public IInspectableHeapPage()
        {
            InitializeComponent();
        }

        private void FindHeapInstancesButtonClicked(object sender, RoutedEventArgs e)
        {
            RemoteApp app = (DataContext as RemoteAppModel).App;
            IEnumerable<CandidateType> iinspectable = app.QueryTypes("WinRT.IInspectable");
            Type iinspectaleType = app.GetRemoteType(iinspectable?.FirstOrDefault());
            if (iinspectaleType == null)
            {
                MessageBox.Show("Couldn't find IInspectable type in the remote process.");
                return;
            }

            IEnumerable<CandidateObject> candidates = app.QueryInstances(iinspectaleType);

            findHeapInstancesButtonSpinner.Width = findHeapInstancesButtonTextPanel.ActualWidth;
            findHeapInstancesButtonSpinner.Visibility = Visibility.Visible;
            findHeapInstancesButtonTextPanel.Visibility = Visibility.Collapsed;

            List<HeapObjectViewModel> newInstances = new List<HeapObjectViewModel>();
            foreach (CandidateObject candidate in candidates)
            {
                RemoteObject ro;
                try
                {
                    ro = app.GetRemoteObject(candidate);
                    
                }
                catch(Exception ex)
                {
                    // damn
                    Debug.WriteLine(
                        $"[IInspectable Heap Search] Failed to get remote object of this candidate: {candidate}.\nEx:\n{ex}");
                    continue;
                }

                string name = "?";
                try
                {
                    dynamic dro = ro.Dynamify();
                    name = dro.GetRuntimeClassName( /*throw:*/ false).ToString();
                }
                catch (Exception ex)
                {
                    // damn
                    Debug.WriteLine(
                        $"[IInspectable Heap Search] Failed to examine name of remote object: {ro}.\nEx:\n{ex}");
                }

                newInstances.Add(new HeapObjectViewModel()
                {
                    FullTypeName = name,
                    Address = ro.RemoteToken
                    // Not adding `RemoteObject` field right now because I don't really want to freeze them all...
                });
            }

            // Carry with us all previously frozen objects
            if (_instancesList != null)
            {
                List<HeapObjectViewModel> combined = new List<HeapObjectViewModel>(_instancesList.Where(oldObj => oldObj.Frozen));
                foreach (var instance in newInstances)
                {
                    if (!combined.Contains(instance))
                        combined.Add(instance);
                }

                newInstances = combined;
            }

            _instancesList = newInstances.ToList();
            _instancesList.Sort();

            heapInstancesListBox.ItemsSource = _instancesList;

            findHeapInstancesButtonSpinner.Visibility = Visibility.Collapsed;
            findHeapInstancesButtonTextPanel.Visibility = Visibility.Visible;
        }

        private void ExportHeapInstancesButtonClicked(object sender, RoutedEventArgs e)
        {
            throw new NotImplementedException();
        }

        private void ExploreButtonBaseOnClick(object sender, RoutedEventArgs e)
        {
            throw new NotImplementedException();
        }

        private void InspectButtonBaseOnClick(object sender, RoutedEventArgs e)
        {
            throw new NotImplementedException();
        }

        private void FreezeUnfreezeHeapObject(object sender, RoutedEventArgs e)
        {
            throw new NotImplementedException();
        }
    }
}
