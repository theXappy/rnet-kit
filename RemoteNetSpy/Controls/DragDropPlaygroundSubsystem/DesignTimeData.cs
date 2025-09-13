using System;
using System.Collections.ObjectModel;
using System.Reflection;
using RemoteNetSpy.Models;

namespace DragDropExpressionBuilder
{
    internal static class DesignTimeData
    {
        public static PlaygroundViewModel DesignInstance { get; } = CreateDesignInstance();

        private static PlaygroundViewModel CreateDesignInstance()
        {
            var vm = new PlaygroundViewModel();
            vm.ReservoirItems.Clear();
            vm.DroppedMethods.Clear();

            // Create mock HeapObjects
            var stringMethods = new ObservableCollection<MethodInfo>(typeof(string).GetMethods(BindingFlags.Public | BindingFlags.Instance));
            var intMethods = new ObservableCollection<MethodInfo>(typeof(int).GetMethods(BindingFlags.Public | BindingFlags.Instance));
            var dateTimeMethods = new ObservableCollection<MethodInfo>(typeof(DateTime).GetMethods(BindingFlags.Public | BindingFlags.Instance));

            var heapObj1 = new HeapObjectViewModel { Address = 0x1000, FullTypeName = "System.String" };
            var heapObj2 = new HeapObjectViewModel { Address = 0x2000, FullTypeName = "System.Int32" };
            var heapObj3 = new HeapObjectViewModel { Address = 0x3000, FullTypeName = "System.DateTime" };
            heapObj1.SetTypeMethodsForDesign(stringMethods);
            heapObj2.SetTypeMethodsForDesign(intMethods);
            heapObj3.SetTypeMethodsForDesign(dateTimeMethods);
            heapObj1.RemoteObject = null; // Not frozen for design
            heapObj2.RemoteObject = null;
            heapObj3.RemoteObject = null;

            vm.ReservoirItems.Add(heapObj1);
            vm.ReservoirItems.Add(heapObj2);
            vm.ReservoirItems.Add(heapObj3);

            // DroppedMethods example (using HeapObject as AssignedInstance)
            var methodInfo1 = typeof(string).GetMethod("Contains", new[] { typeof(string) });
            var dropped = new DroppedMethodItem(new MethodInvocation(new MethodInfoWrapper(methodInfo1)), 100, 100);
            dropped.Invocation.ReturnValue = new MethodInvocationParameter(type: typeof(bool), paramName: "ReturnValue")
            {
                AssignedInstance = new Instance { Type = typeof(bool), Tag = "True" },
            };
            dropped.Invocation.Parameters[0].AssignedInstance = new Instance { Type = typeof(string), Tag = "Hello" };
            vm.DroppedMethods.Add(dropped);

            return vm;
        }
    }
}
