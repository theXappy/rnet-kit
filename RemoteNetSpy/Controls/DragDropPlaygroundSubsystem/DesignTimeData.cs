using System;

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
            var methodInfo1 = typeof(string).GetMethod("Contains", new[] { typeof(string) });
            var methodInfo2 = typeof(string).GetMethod("Replace", new[] { typeof(string), typeof(string) });
            var methodInfo3 = typeof(string).GetMethod("ToString", new Type[0]);
            vm.ReservoirItems.Add(new MethodInfoWrapper(methodInfo1));
            vm.ReservoirItems.Add(new MethodInfoWrapper(methodInfo2));
            var instance1 = new Instance { Type = typeof(string), Tag = "StringInstance" };
            var instance2 = new Instance { Type = typeof(int), Tag = "IntInstance" };
            var instance3 = new Instance { Type = typeof(DateTime), Tag = "DateTimeInstance" };
            var instance4 = new Instance { Type = typeof(bool), Tag = "BooleanInstance" };
            vm.ReservoirItems.Add(instance1);
            vm.ReservoirItems.Add(instance2);
            vm.ReservoirItems.Add(instance3);
            vm.ReservoirItems.Add(instance4);
            // Add a dropped method for design
            var dropped = new DroppedMethodItem(new MethodInvocation(new MethodInfoWrapper(methodInfo1)), 100, 100);
            dropped.Invocation.ReturnValue = new MethodInvocationParameter(typeName: "System.Boolean", paramName: "ReturnValue")
            {
                AssignedInstance = instance4,
            };
            dropped.Invocation.Parameters[0].AssignedInstance = instance1;
            vm.DroppedMethods.Add(dropped);

            // Add a dropped method for design
            var dropped2 = new DroppedMethodItem(new MethodInvocation(new MethodInfoWrapper(methodInfo2)), 150, 180);
            dropped2.Invocation.ReturnValue = new MethodInvocationParameter(typeName: "System.String", paramName: "ReturnValue")
            {
                AssignedInstance = instance1,
            };
            dropped2.Invocation.Parameters[0].AssignedInstance = instance1;
            vm.DroppedMethods.Add(dropped2);

            // Add a dropped method for design
            var dropped3 = new DroppedMethodItem(new MethodInvocation(new MethodInfoWrapper(methodInfo3)), 200, 270);
            dropped3.Invocation.ReturnValue = new MethodInvocationParameter(typeName: "System.String", paramName: "ReturnValue")
            {
                AssignedInstance = instance1,
            };
            vm.DroppedMethods.Add(dropped3);

            return vm;
        }
    }
}
