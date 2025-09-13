using CliWrap;
using CliWrap.Buffered;
using HostingWfInWPF;
using RemoteNET;
using RemoteNetSpy.Models.Helpers;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;

namespace RemoteNetSpy.Models;

public class RemoteAppModel : INotifyPropertyChanged
{
    public RemoteApp App { get; private set; }
    public int TargetPid { get; set; }


    private bool _hasIInspectables;

    public ClassesModel ClassesModel { get; private set; }
    public FunctionsTracer Tracer { get; }
    public Interactor Interactor { get; }


    public bool HasIInspectables
    {
        get { return _hasIInspectables; }
        set
        {
            SetField(ref _hasIInspectables, value);
        }
    }

    public RuntimeType TargetRuntime => App is UnmanagedRemoteApp ? RuntimeType.Unmanaged : RuntimeType.Managed;

    public RemoteAppModel()
    {
        ClassesModel = new ClassesModel(this);
        Tracer = new FunctionsTracer(this);
        Interactor = new Interactor(this);
    }

    public void Update(RemoteApp app, int pid)
    {
        try
        {
            App?.Dispose();
        }
        catch
        {
            // Ignored
        }

        App = app;
        TargetPid = pid;
        _ = Task.Run(QueryIInspectables);
    }

    private void QueryIInspectables()
    {
        try
        {
            CandidateType candidate = App.QueryTypes("WinRT.IInspectable").FirstOrDefault();
            HasIInspectables = candidate != null;
        }
        catch
        {
            HasIInspectables = false;
        }
    }

    public string UnmanagedFlagIfNeeded()
    {
        if (TargetRuntime == RuntimeType.Unmanaged)
            return "-u";
        return string.Empty;
    }

    public void ShowMemoryView(Window owner = null, ulong? address = null)
    {
        if (App == null)
        {
            MessageBox.Show("You must attach to a process first", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        var mvw = new MemoryViewWindow(this, address) { Owner = owner };
        mvw.Show();
    }

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


    public async Task<IEnumerable<HeapObjectViewModel>> SearchHeap(string fullTypeNameQuery)
    {
        var x = CliWrap.Cli.Wrap("rnet-dump.exe")
                    .WithArguments($"heap -t {TargetPid} -q \"{fullTypeNameQuery}\" " + UnmanagedFlagIfNeeded())
                    .WithValidation(CommandResultValidation.None)
                    .ExecuteBufferedAsync();
        var res = await x.Task;
        var newInstances = res.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .SkipWhile(line => !line.Contains("Found "))
            .Skip(1)
            .Select(str => str.Trim())
            .Select(HeapObjectViewModel.Parse);

        return newInstances;
    }

    public async Task<bool> PromptForVariableCastAsync(HeapObjectViewModel heapObject, System.Windows.Threading.Dispatcher dispatcher)
    {
        // Preparting a "Types Model" for the TypeSelectionWindow
        IEnumerable<DumpedTypeModel> types = ClassesModel.FilteredAssemblies.SelectMany(a => a.Types);
        List<DumpedTypeModel> deduplicatedList = types.GroupBy(x => x.FullTypeName)
                                                      .Select(group => group.First())
                                                      .ToList();

        var typesModel = new TypesModel();
        typesModel.Types = new ObservableCollection<DumpedTypeModel>(deduplicatedList);

        bool? res = false;
        await dispatcher.InvokeAsync(() =>
        {
            var typeSelectionWindow = new TypeSelectionWindow();
            typeSelectionWindow.DataContext = typesModel;

            string currFullTypeName = heapObject.FullTypeName;
            if (currFullTypeName.Contains("::"))
            {
                string currTypeName = currFullTypeName.Split("::").Last();
                string regex = "::" + currTypeName + @"$";
                typeSelectionWindow.ApplyRegexFilter(regex);
            }

            res = typeSelectionWindow.ShowDialog();
        });

        if (res != true)
            return false;

        DumpedTypeModel selectedType = typesModel.SelectedType;
        if (selectedType == null)
            return false;

        try
        {
            Type newType = App.GetRemoteType(selectedType.FullTypeName);
            heapObject.Cast(newType);
            Interactor.CastVar(heapObject, selectedType.FullTypeName);
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to cast object: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
    }
}
