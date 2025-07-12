using Microsoft.Diagnostics.Runtime.AbstractDac;
using System;
using System.Collections.ObjectModel;
using System.Reflection;

namespace DragDropExpressionBuilder
{
    public class PlaygroundViewModel
    {
        public ObservableCollection<object> ReservoirItems { get; } = new();
        public ObservableCollection<DroppedMethodItem> DroppedMethods { get; } = new();

        //
        public void LoadDemoData()
        {
            ReservoirItems.Clear();
            DroppedMethods.Clear();
            var methodInfo1 = typeof(string).GetMethod("Contains", new[] { typeof(string) });
            var methodInfo2 = typeof(string).GetMethod("Replace", new[] { typeof(string), typeof(string) });
            var staticMethodInfo = typeof(string).GetMethod("IsNullOrEmpty", new[] { typeof(string) });
            AddMethod(methodInfo1);
            AddMethod(methodInfo2);
            AddMethod(staticMethodInfo);
            AddObject("Hello!", "StringInstance");
            //ReservoirItems.Add(new Instance { Type = typeof(string), Tag = "EmptyString", Obj = string.Empty });
            AddObject(string.Empty, "EmptyString");
            AddObject(1, "IntInstance");
            AddObject(DateTime.Now, "DateTimeInstance");
        }

        public void AddObject(object o, string tag) => ReservoirItems.Add(new Instance { Type = o.GetType(), Tag = tag, Obj = o });
        public void AddObject(object o, string tag, Type forcedType) => ReservoirItems.Add(new Instance { Type = forcedType, Tag = tag, Obj = o });

        public void AddMethod(System.Reflection.MethodInfo mi) => ReservoirItems.Add(new MethodInfoWrapper(mi));
    }
}
