using CliWrap;
using CliWrap.Buffered;
using HostingWfInWPF;
using ICSharpCode.Decompiler.Metadata;
using RemoteNET;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
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

    static int lol = 0;
    public ClassesModel()
    {
        Debug.WriteLine($"############## ClassesModel === {lol++}");
    }


    private IEnumerable<string> RnetDump(string command)
    {
        var commandTask = CliWrap.Cli.Wrap("rnet-dump.exe")
            .WithArguments(command)
            .WithValidation(CommandResultValidation.None)
            .ExecuteBufferedAsync();
        var domainsResults = commandTask.Task.Result;
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

    private Regex r = new Regex(@"\[(.*?)\]\[(.*?)\](.*)");

    private ObservableCollection<AssemblyModel> FetchAssemblies()
    {
        var assemblyModels = new Dictionary<string, AssemblyModel>();
        var assemblyToTypes = new Dictionary<string, List<DumpedTypeModel>>();

        IEnumerable<string> dumpedTypesLines = RnetDump($"types -t {_parent.TargetPid} -q * {UnmanagedFlagIfNeeded()}");
        foreach (string dumpedTypeLine in dumpedTypesLines)
        {
            var match = r.Match(dumpedTypeLine);
            string[] parts = match.Groups.Values.Select(g => g.Value).Skip(1).ToArray();
            if (parts.Length < 3)
                continue;
            string runtime = parts[0].Trim();
            string assemblyName = parts[1].Trim();

            if (!assemblyModels.TryGetValue(assemblyName, out AssemblyModel assembly))
            {
                assembly = new AssemblyModel(assemblyName, runtime, anyTypes: true);
                assemblyModels[assemblyName] = assembly;
            }

            string typeName = parts[2].Trim();
            DumpedTypeModel type = new DumpedTypeModel(assemblyName, typeName, numInstances: null);

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
        IEnumerable<string> domainDumpsLines = RnetDump($"domains -t {_parent.TargetPid} {UnmanagedFlagIfNeeded()}");
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