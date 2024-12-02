using CliWrap;
using CliWrap.Buffered;
using HostingWfInWPF;
using ICSharpCode.Decompiler.Metadata;
using RemoteNET;
using RemoteNetSpy.Controls;
using Spectre.Console;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;

namespace RemoteNetSpy.Models;

public class ClassesModel : INotifyPropertyChanged
{
    private RemoteAppModel _parent;

    public ClassesModel(RemoteAppModel parent)
    {
        _parent = parent;
    }

    private DumpedTypeModel selectedType;
    private ObservableCollection<AssemblyModel> _assemblies = new ObservableCollection<AssemblyModel>();
    private ObservableCollection<AssemblyModel> _filteredAssemblies = new ObservableCollection<AssemblyModel>();

    public RemoteAppModel Parent => _parent;

    public ObservableCollection<AssemblyModel> Assemblies
    {
        get => _assemblies;
        set
        {
            // Create and apply a sorted CollectionView
            SetField(ref _assemblies, value);
        }
    }

    public ObservableCollection<AssemblyModel> FilteredAssemblies
    {
        get => _filteredAssemblies;
        set
        {
            // Create and apply a sorted CollectionView
            SetField(ref _filteredAssemblies, value);
        }
    }

    public DumpedTypeModel SelectedType
    {
        get => selectedType;
        set => SetField(ref selectedType, value);
    }

    private async Task<IEnumerable<string>> RnetDumpAsync(string command)
    {
        var commandTask = CliWrap.Cli.Wrap("rnet-dump.exe")
            .WithArguments(command)
            .WithValidation(CommandResultValidation.None)
            .ExecuteBufferedAsync();
        var domainsResults = await commandTask.Task;
        var domainDumpsLines = domainsResults.StandardOutput.Split('\n')
            .Skip(2)
            .Select(str => str.Trim());
        return domainDumpsLines;
    }

    private string UnmanagedFlagIfNeeded()
    {
        if (_parent.TargetRuntime == RuntimeType.Unmanaged)
            return "-u";
        return string.Empty;
    }

    public async Task LoadAssembliesAsync()
    {
        Assemblies = await Task.Run(FetchAssemblies);
    }

    private Regex r = new Regex(@"\[(?<runtime>.*?)\]\[(?<assembly>.*?)\]\[(?<methodTable>.*?)\](?<type>.*)");

    private ObservableCollection<AssemblyModel> FetchAssemblies()
    {
        var assemblyModels = new Dictionary<string, AssemblyModel>();
        var assemblyToTypes = new Dictionary<string, List<DumpedTypeModel>>();

        IEnumerable<string> dumpedTypesLines = RnetDumpAsync($"types -t {_parent.TargetPid} -q * {UnmanagedFlagIfNeeded()}").Result;
        foreach (string dumpedTypeLine in dumpedTypesLines)
        {
            var match = r.Match(dumpedTypeLine);
            if (!match.Success)
                continue;
            string runtime = match.Groups["runtime"].Value.Trim();
            string assemblyName = match.Groups["assembly"].Value.Trim();

            if (!assemblyModels.TryGetValue(assemblyName, out AssemblyModel assembly))
            {
                assembly = new AssemblyModel(assemblyName, runtime, anyTypes: true);
                assemblyModels[assemblyName] = assembly;
            }

            string methodTableStr = match.Groups["methodTable"].Value.Trim();
            ulong? methodTable = null;
            if (methodTableStr != "null")
                methodTable = Convert.ToUInt64(methodTableStr, 16);
            string typeName = match.Groups["type"].Value.Trim();
            DumpedTypeModel type = new DumpedTypeModel(assemblyName, typeName, methodTable, numInstances: null);

            if (!assemblyToTypes.TryGetValue(assemblyName, out List<DumpedTypeModel> typesList))
            {
                assemblyToTypes[assemblyName] = new List<DumpedTypeModel>();
            }
            assemblyToTypes[assemblyName].Add(type);
        }

        foreach (var kvp in assemblyModels)
        {
            List<DumpedTypeModel> typesList = assemblyToTypes[kvp.Key];
            typesList.Sort((type1, type2) => type1.FullTypeName.CompareTo(type2.FullTypeName));
            kvp.Value.Types = new ObservableCollection<DumpedTypeModel>(typesList);
        }

        // Also look for types-less assemblies
        IEnumerable<string> domainDumpsLines = RnetDumpAsync($"domains -t {_parent.TargetPid} {UnmanagedFlagIfNeeded()}").Result;
        foreach (string domainDumpsLine in domainDumpsLines)
        {
            if (!domainDumpsLine.StartsWith("[module] "))
                continue;

            string assemblyName = domainDumpsLine.Substring("[module] ".Length);
            if (!assemblyModels.TryGetValue(assemblyName, out AssemblyModel assembly))
            {
                assembly = new AssemblyModel(assemblyName, _parent.TargetRuntime, anyTypes: false);
                assemblyModels[assemblyName] = assembly;
            }
        }

        List<AssemblyModel> assembliesList = assemblyModels.Values.ToList();
        assembliesList.Sort((asm1, asm2) => asm1.Name.CompareTo(asm2.Name));

        return new ObservableCollection<AssemblyModel>(assembliesList);
    }

    public async Task CountInstancesAsync()
    {
        string assemblyFilter = "*"; // Wildcard

        RemoteApp app = Parent.App;
        if (app is UnmanagedRemoteApp)
            assemblyFilter += "!*"; // Indicate we want any type within the module
        else if (app is ManagedRemoteApp)
            assemblyFilter += ".*"; // Indicate we want any type within the assembly

        Task<IEnumerable<string>> task = RnetDumpAsync($"heap -t {_parent.TargetPid} -q {assemblyFilter} {UnmanagedFlagIfNeeded()}");
        IEnumerable<string> rnetDumpStdOutLines = await task;

        foreach (AssemblyModel assembly in Assemblies)
        {
            foreach (DumpedTypeModel type in assembly.Types)
            {
                type.PreviousNumInstances = type.NumInstances;
                type.NumInstances = 0;
            }
        }

        // Temporary working dict & reset instance counts
        Dictionary<string, DumpedTypeModel> fullTypeNamesToTypes = new Dictionary<string, DumpedTypeModel>();
        foreach (DumpedTypeModel typeModel in Assemblies.SelectMany(assm => assm.Types))
        {
            fullTypeNamesToTypes[typeModel.FullTypeName] = typeModel;
        }

        foreach (string addrAndType in rnetDumpStdOutLines)
        {
            // Get rid of the address:
            // 0x00aabbcc System.Int32 => System.Int32
            string fullTypeName = addrAndType.Substring(addrAndType.IndexOf(' ') + 1);

            if (fullTypeNamesToTypes.ContainsKey(fullTypeName))
                fullTypeNamesToTypes[fullTypeName].NumInstances++;
        }
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
}

public class RemoteAppModel : INotifyPropertyChanged
{
    public RemoteApp App { get; private set; }
    public int TargetPid { get; set; }


    private bool _hasIInspectables;

    public ClassesModel ClassesModel { get; private set; }

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
    }

    public void Update(RemoteApp app, int pid)
    {
        App = app;
        TargetPid = pid;
        try
        {
            CandidateType candidate = app.QueryTypes("WinRT.IInspectable").FirstOrDefault();
            HasIInspectables = candidate != null;
        }
        catch
        {
            HasIInspectables = false;
        }
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
}

public class DebugClassesModel : ClassesModel
{
    public DebugClassesModel() : base(null)
    {
    }
}
