using CliWrap;
using CliWrap.Buffered;
using Microsoft.VisualStudio.Threading;
using RemoteNET;
using Spectre.Console;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
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

    private async Task<(IEnumerable<string> StdOutLines, IEnumerable<string> StdErrLines)> RnetDumpAsync(string command)
    {
        var commandTask = CliWrap.Cli.Wrap("rnet-dump.exe")
            .WithArguments(command)
            .WithValidation(CommandResultValidation.None)
            .ExecuteBufferedAsync();
        var domainsResults = await commandTask.Task;
        var domainDumpsLines = domainsResults.StandardOutput.Split('\n')
            .Skip(2)
            .Select(str => str.Trim());
        var stderrLines = domainsResults.StandardError.Split('\n')
            .Select(str => str.Trim())
            .Where(str => !string.IsNullOrWhiteSpace(str));
        return (domainDumpsLines, stderrLines);
    }

    private string UnmanagedFlagIfNeeded()
    {
        if (_parent.TargetRuntime == RuntimeType.Unmanaged)
            return "-u";
        return string.Empty;
    }

    public async Task<Dictionary<string, AssemblyModel>> LoadAssembliesAsync(Dispatcher d)
    {
        return await UpdateAssemblies(d);
    }

    private Regex r = new Regex(@"\[(?<runtime>.*?)\]\[(?<assembly>.*?)\]\[(?<methodTable>.*?)\](?<type>.*)");
    private Regex _typesDumpErrorRegex = new Regex(@"^\[(?<runtime>[^\]]+)\]\[(?<assembly>[^\]]+)\]\s*(?<error>.*)$");

    private async Task<Dictionary<string, AssemblyModel>> UpdateAssemblies(Dispatcher d)
    {
        var assemblyModels = new Dictionary<string, AssemblyModel>();

        string typesDumpArgs = $"types -t {_parent.TargetPid} -q * {UnmanagedFlagIfNeeded()}";
        Task<(IEnumerable<string> StdOutLines, IEnumerable<string> StdErrLines)> typesTask = RnetDumpAsync(typesDumpArgs);

        // Also look for types-less assemblies
        Task<(IEnumerable<string> StdOutLines, IEnumerable<string> StdErrLines)> domainDumpsLinesTask = RnetDumpAsync($"domains -t {_parent.TargetPid} {UnmanagedFlagIfNeeded()}");

        // Dump domains and consume results
        (IEnumerable<string> domainDumpsLines, _) = await domainDumpsLinesTask;
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

        (IEnumerable<string> typesDumpLines, IEnumerable<string> typesDumpErrors) = await typesTask;
        if (typesDumpLines.All(string.IsNullOrWhiteSpace))
        {
            d.Invoke(() => MessageBox.Show(
                "rnet-dump returned only empty type entries.\n" +
                "Check target/runtime and try again. Types listing failed.\n" +
                "We use these arguments:\n" +
                typesDumpArgs,
                "Error",
                MessageBoxButton.OK,
                MessageBoxImage.Warning));
        }
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

        Dictionary<string, List<string>> loadErrorsByAssembly = new();
        foreach (string errorLine in typesDumpErrors)
        {
            var match = _typesDumpErrorRegex.Match(errorLine);
            if (!match.Success)
                continue;

            string assemblyName = match.Groups["assembly"].Value.Trim();
            string errorMessage = match.Groups["error"].Value.Trim();
            if (string.IsNullOrWhiteSpace(errorMessage))
                errorMessage = errorLine;

            if (!loadErrorsByAssembly.TryGetValue(assemblyName, out List<string> errors))
            {
                errors = new List<string>();
                loadErrorsByAssembly[assemblyName] = errors;
            }
            errors.Add(errorMessage);
        }

        foreach (var loadErrorEntry in loadErrorsByAssembly)
        {
            AssemblyModel assembly = GetOrCreateAssembly(loadErrorEntry.Key);
            assembly.HasLoadErrors = true;
            string combinedErrorMessage = string.Join(" | ", loadErrorEntry.Value.Distinct());
            assembly.AddType(new ErrorNodeModel(loadErrorEntry.Key, combinedErrorMessage));
        }

        d.Invoke(() =>
        {
            FilteredAssemblies = new ObservableCollection<AssemblyModel>(Assemblies);
        });
        return assemblyModels;

        AssemblyModel GetOrCreateAssembly(string assemblyName)
        {
            if (!assemblyModels.TryGetValue(assemblyName, out AssemblyModel assembly))
            {
                    assembly = new AssemblyModel(assemblyName, _parent.TargetRuntime, anyTypes: true);
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

        Task<(IEnumerable<string> StdOutLines, IEnumerable<string> StdErrLines)> task = RnetDumpAsync($"heap -t {_parent.TargetPid} -q {assemblyFilter} {UnmanagedFlagIfNeeded()}");
        (IEnumerable<string> rnetDumpStdOutLines, _) = await task;

        foreach (AssemblyModel assembly in Assemblies)
        {
            foreach (ITypeSystemNode node in assembly.Types)
            {
                if (node is DumpedTypeModel type)
                {
                    type.PreviousNumInstances = type.NumInstances;
                    type.NumInstances = 0;
                }
            }
        }

        // Temporary working dict & reset instance counts
        Dictionary<string, DumpedTypeModel> fullTypeNamesToTypes = new Dictionary<string, DumpedTypeModel>();
        foreach (ITypeSystemNode node in Assemblies.SelectMany(assm => assm.Types))
        {
            if (node is DumpedTypeModel typeModel)
            {
                fullTypeNamesToTypes[typeModel.FullTypeName] = typeModel;
            }
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

