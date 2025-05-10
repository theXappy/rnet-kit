using CliWrap;
using CliWrap.Buffered;
using Microsoft.VisualStudio.Threading;
using RemoteNET;
using Spectre.Console;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Data;
using System.Windows.Threading;

namespace RemoteNetSpy.Models;

public class ClassesModel : INotifyPropertyChanged
{
    private RemoteAppModel _parent;

    public ClassesModel(RemoteAppModel parent)
    {
        _parent = parent;
        BindingOperations.EnableCollectionSynchronization(_assemblies, new object());
    }

    private DumpedTypeModel selectedType;
    private readonly ObservableCollection<AssemblyModel> _assemblies = new ObservableCollection<AssemblyModel>();
    private ObservableCollection<AssemblyModel> _filteredAssemblies = new ObservableCollection<AssemblyModel>();

    public RemoteAppModel Parent => _parent;

    public ObservableCollection<AssemblyModel> Assemblies
    {
        get => _assemblies;
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

    public async Task LoadAssembliesAsync(Dispatcher d)
    {
        await Task.Run(() => UpdateAssemblies(d));
    }

    private Regex r = new Regex(@"\[(?<runtime>.*?)\]\[(?<assembly>.*?)\]\[(?<methodTable>.*?)\](?<type>.*)");

    private void UpdateAssemblies(Dispatcher d)
    {
        var assemblyModels = new Dictionary<string, AssemblyModel>();

        Task<IEnumerable<string>> typesTask = RnetDumpAsync($"types -t {_parent.TargetPid} -q * {UnmanagedFlagIfNeeded()}");

        // Also look for types-less assemblies
        Task<IEnumerable<string>> domainDumpsLinesTask = RnetDumpAsync($"domains -t {_parent.TargetPid} {UnmanagedFlagIfNeeded()}");

        Task handleDomainsTask = domainDumpsLinesTask.ContinueWith(t =>
        {
            IEnumerable<string> domainDumpsLines = t.Result;
            List<string> orderedAssembliesLines = domainDumpsLines.ToList();
            orderedAssembliesLines.Sort((asm1, asm2) => asm1.CompareTo(asm2));

            Assemblies.Clear();
            foreach (string domainDumpsLine in domainDumpsLines)
            {
                if (!domainDumpsLine.StartsWith("[module] "))
                    continue;

                string assemblyName = domainDumpsLine.Substring("[module] ".Length);
                GetOrCreateAssembly(assemblyName);
            }
        }, TaskScheduler.Default);

        _ = Task.WhenAll(handleDomainsTask, typesTask).ContinueWith(x =>
        {
#pragma warning disable VSTHRD002 // Avoid problematic synchronous waits
            var typesDumpLines = typesTask.Result;
#pragma warning restore VSTHRD002 // Avoid problematic synchronous waits
            foreach (string dumpedTypeLine in typesDumpLines)
            {
                var match = r.Match(dumpedTypeLine);
                if (!match.Success)
                    continue;
                string runtime = match.Groups["runtime"].Value.Trim();
                string assemblyName = match.Groups["assembly"].Value.Trim();

                AssemblyModel assembly = GetOrCreateAssembly(assemblyName);

                string methodTableStr = match.Groups["methodTable"].Value.Trim();
                ulong? methodTable = null;
                if (methodTableStr != "null")
                    methodTable = Convert.ToUInt64(methodTableStr, 16);
                string typeName = match.Groups["type"].Value.Trim();
                DumpedTypeModel type = new DumpedTypeModel(assemblyName, typeName, methodTable, numInstances: null);
                assembly.AddType(type);
            }
        }, TaskScheduler.Default);
        return;

        AssemblyModel GetOrCreateAssembly(string assemblyName)
        {
            if (!assemblyModels.TryGetValue(assemblyName, out AssemblyModel assembly))
            {
                assembly = new AssemblyModel(assemblyName, _parent.TargetRuntime, anyTypes: false);
                assemblyModels[assemblyName] = assembly;
                Assemblies.Add(assembly);
                // We need to call EnableCollectionSynchronization on the UI thread so that the collection can be updated from other threads.
                d.Invoke(() => BindingOperations.EnableCollectionSynchronization(assembly.Types, assembly.TypesLock));
                d.Invoke(() => BindingOperations.EnableCollectionSynchronization(assembly.FilteredTypes, assembly.TypesLock));
            }
            return assembly;
        }
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

