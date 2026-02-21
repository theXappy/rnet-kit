using CommandLine;
using Microsoft.Win32;
using RemoteNET;
using RnetKit.Common;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using System.Windows;
using System.Windows.Input;

namespace RemoteNetSpy.Models;

public class FunctionsTracer : INotifyPropertyChanged
{
    private RemoteAppModel Parent;
    private int _notificationsCount = 0;

    public ICommand RunFridaTracesCommand { get; }
    public ICommand RunRemoteNetTracesCommand { get; }
    public ICommand OpenCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand ClearCommand { get; }
    public ICommand AddFuncCommand { get; }
    public ICommand DeleteFuncCommand { get; }

    public ObservableCollection<TraceFunction> TraceList { get; } = new();

    public int TotalItemsCount => TraceList.Count;

    public int NotificationsCount 
    { 
        get => _notificationsCount;
        private set
        {
            if (_notificationsCount != value)
            {
                _notificationsCount = value;
                OnPropertyChanged();
            }
        }
    }

    public FunctionsTracer(RemoteAppModel parent)
    {
        Parent = parent;
        RunFridaTracesCommand = new RelayCommand<object>(OnRunFridaTrace);
        RunRemoteNetTracesCommand = new RelayCommand<object>(OnRunRemoteNetTrace);
        OpenCommand = new RelayCommand<object>(_ => Open());
        SaveCommand = new RelayCommand<object>(_ => Save());
        ClearCommand = new RelayCommand<object>(_ => Clear());
        AddFuncCommand = new RelayCommand<object>(o => AddFunc(o as DumpedMember));
        DeleteFuncCommand = new RelayCommand<object>(DeleteFunc);

        // Subscribe to collection changes to update notification count
        TraceList.CollectionChanged += (sender, e) => 
        {
            OnPropertyChanged(nameof(TotalItemsCount));
            
            // If items were added, increment notifications count
            if (e.Action == NotifyCollectionChangedAction.Add)
            {
                NotificationsCount += e.NewItems?.Count ?? 0;
            }
            // If collection was cleared, reset notifications count
            else if (e.Action == NotifyCollectionChangedAction.Reset)
            {
                NotificationsCount = 0;
            }
        };
    }

    public void ClearNotifications()
    {
        NotificationsCount = 0;
    }

    public void AddFunc(DumpedMember sender)
    {
        string fullDemangledName;

        if (sender.MemberType != "Method" && sender.MemberType != "Constructor")
            return;

        string targetClass = sender.MemberInfo.DeclaringType.FullName;
        string module = sender.MemberInfo.DeclaringType.Assembly.GetName().Name;

        if (Parent.App is ManagedRemoteApp)
        {
            // Managed

            string methodName = sender.MemberInfo.Name;
            MemberInfo mi = sender.MemberInfo;
            string[] parameters = mi is MethodInfo methodInfo
                ? methodInfo.GetParameters().Select(p => p.ParameterType.FullName).ToArray()
                : Array.Empty<string>();

            fullDemangledName = $"{targetClass}.{methodName}({string.Join(", ", parameters)})";
        }
        else
        {
            // Unmanaged
            string methodName = sender.MemberInfo.Name;
            fullDemangledName = $"{targetClass}.{methodName}";
        }

        if (!TraceList.Any(tf => tf.DemangledName == fullDemangledName))
        {
            if (!module.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                module += ".dll";
            string methodName = sender.MemberInfo.Name;
            string fullMangledName = $"{module}!{methodName}";
            TraceList.Add(new TraceFunction(fullDemangledName, fullMangledName));
        }
    }

    public void DeleteFunc(object sender)
    {
        TraceFunction trace = (sender as FrameworkElement)?.DataContext as TraceFunction;
        if (trace == null)
            return;

        TraceList.Remove(trace);
    }

    private void OnRunRemoteNetTrace(object sender)
    {
        if (!TraceList.Any())
        {
            MessageBox.Show("List of functions to trace is empty.", "Error", MessageBoxButton.OK,
                MessageBoxImage.Error);
            return;
        }

        if (Parent.App == null)
        {
            MessageBox.Show("You must attach to a process first", "Error", MessageBoxButton.OK,
                MessageBoxImage.Error);
            return;
        }

        string tempFlistPath = Path.ChangeExtension(Path.GetTempFileName(), "flist");
        SaveInner(tempFlistPath);

        try
        {
            List<string> args = new List<string>() { "-t", Parent.TargetPid.ToString(), Parent.UnmanagedFlagIfNeeded() };
            args.Add("-l");
            args.Add($"\"{tempFlistPath}\"");

            string argsLine = string.Join(' ', args);
            ProcessStartInfo psi = new ProcessStartInfo("rnet-trace.exe", argsLine)
            {
                UseShellExecute = true
            };

            Process.Start(psi);
        }
        catch
        {
            try { File.Delete(tempFlistPath); } catch { }
        }
    }
    private void OnRunFridaTrace(object parameter)
    {
        if (!TraceList.Any())
        {
            MessageBox.Show("List of functions to trace is empty.", "Error", MessageBoxButton.OK,
                MessageBoxImage.Error);
            return;
        }

        if (Parent.App == null)
        {
            MessageBox.Show("You must attach to a process first", "Error", MessageBoxButton.OK,
                MessageBoxImage.Error);
            return;
        }

        try
        {
            List<string> args = new List<string>() { "-p", Parent.TargetPid.ToString() };

            foreach (var traceFunction in TraceList)
            {
                args.Add("-i");
                args.Add($"\"{traceFunction.FullMangledName}\"");
            }

            string argsLine = string.Join(' ', args);
            ProcessStartInfo psi = new ProcessStartInfo("frida-trace", argsLine)
            {
                UseShellExecute = true
            };

            Process.Start(psi);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to start Frida trace: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Save()
    {
        var sfd = new SaveFileDialog();
        sfd.Filter = "Functions List File (*.flist)|*.flist";
        sfd.OverwritePrompt = true;
        bool? success = sfd.ShowDialog();
        if (success == true)
        {
            string path = sfd.FileName;
            SaveInner(path);
        }
    }

    private void Open()
    {
        var ofd = new OpenFileDialog();
        ofd.Filter = "Functions List File (*.flist)|*.flist";
        bool? success = ofd.ShowDialog();
        if (success == true)
        {
            string path = ofd.FileName;
            if (path == null)
            {
                MessageBox.Show("Invalid file name.");
                return;
            }

            TraceList.Clear();
            string[] functions = File.ReadAllText(path).Split("\n", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            foreach (var func in functions)
            {
                TraceList.Add(TraceFunction.FromJson(func));
            }
        }
    }

    public void SaveInner(string path)
    {
        FileStream f;
        StreamWriter sw;
        if (path == null)
        {
            MessageBox.Show("Invalid file name.");
            return;
        }
        f = File.Open(path, FileMode.Create);
        sw = new StreamWriter(f);
        foreach (TraceFunction traceFunction in TraceList)
        {
            sw.WriteLine(traceFunction.ToJson());
        }

        sw.Flush();
        f.Close();
    }

    internal void Clear() => TraceList.Clear();

    #region INotifyPropertyChanged
    public event PropertyChangedEventHandler PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    #endregion
}
