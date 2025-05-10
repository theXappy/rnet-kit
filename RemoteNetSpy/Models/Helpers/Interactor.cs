using HostingWfInWPF;
using RemoteNET;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Threading;

namespace RemoteNetSpy.Models.Helpers
{
    public class Interactor
    {

        private RemoteAppModel Parent;
        internal ConEnumWpfHost _interactivePanel;
        public ICommand AddVarCommand { get; }


        int _remoteObjectIndex = 0;

        private Task _interactiveWindowInitTask = null;

        public Interactor(RemoteAppModel parent)
        {
            Parent = parent;
            _interactivePanel = null;
            AddVarCommand = new RelayCommand<HeapObject>(AddVar);
        }

        public async Task InitAsync(MainWindow mw)
        {
            _interactivePanel = mw.interactivePanel;

            // Already completed initialization
            if (_interactivePanel.IsStarted)
                return;

            // Initialization is in progress
            if (_interactiveWindowInitTask != null)
            {
                await _interactiveWindowInitTask;
                return;
            }

            // Initialization is not started yet, starting now
            RuntimeType runtime = Parent.TargetRuntime;
            string RuntimeTypeFullTypeName = typeof(RuntimeType).FullName;

            await _interactivePanel.StartAsync("rnet-repl.exe");
            string connectionScript =
$"var app = RemoteAppFactory.Connect(Process.GetProcessById({Parent.TargetPid}), {RuntimeTypeFullTypeName}.{runtime});\r\n";
            _interactiveWindowInitTask = _interactivePanel.WriteInputTextAsync(connectionScript, clearLast: false);
            await _interactiveWindowInitTask;
            return;
        }

        public void AddVar(HeapObject dataContext)
        {
            if (!dataContext.Frozen || dataContext.RemoteObject == null)
            {
                throw new Exception("ERROR: Object must be frozen.");
            }

            _remoteObjectIndex++;
            string roVarName = $"ro{_remoteObjectIndex}";
            string droVarName = $"dro{_remoteObjectIndex}";
            string objectScript =
$"var {roVarName} = app.GetRemoteObject(0x{dataContext.Address:X16}, \"{dataContext.FullTypeName}\");\r\n" +
$"dynamic {droVarName} = {roVarName}.Dynamify();\r\n";

            _ = _interactiveWindowInitTask.ContinueWith(_ =>
            {
                _ = _interactivePanel.Dispatcher.Invoke(async () =>
                {
                    await _interactivePanel.WriteInputTextAsync(objectScript);
                    dataContext.InteractiveRoVarName = roVarName;
                    dataContext.InteractiveDroVarName = droVarName;
                });
            }, TaskScheduler.Default);
        }

        public void DeleteVar(HeapObject dataContext)
        {
            string roVarName = dataContext.InteractiveRoVarName;
            string droVarName = dataContext.InteractiveDroVarName;
            string objectScript =
$"{roVarName} = null;\r\n" +
$"{droVarName} = null;\r\n";
            _ = _interactivePanel.Dispatcher.Invoke(async () =>
            {
                await _interactivePanel.WriteInputTextAsync(objectScript);
                dataContext.InteractiveRoVarName = null;
                dataContext.InteractiveDroVarName = null;
            });
        }

        public void CastVar(HeapObject dataContext, string fullTypeName)
        {
            if (!dataContext.Frozen || dataContext.RemoteObject == null)
            {
                throw new Exception("ERROR: Object must be frozen.");
            }

            string roVarName = dataContext.InteractiveRoVarName;
            string droVarName = dataContext.InteractiveDroVarName;
            string objectScript =
$"{roVarName} = {roVarName}.Cast(app.GetRemoteType(\"{fullTypeName}\"));\r\n" +
$"{droVarName} = {roVarName}.Dynamify();\r\n";

            _ = _interactiveWindowInitTask.ContinueWith(_ =>
            {
                _ = _interactivePanel.Dispatcher.Invoke(async () =>
                {
                    await _interactivePanel.WriteInputTextAsync(objectScript);
                });
            }, TaskScheduler.Default);
        }
    }
}
