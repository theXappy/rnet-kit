using Microsoft.Diagnostics.Runtime.AbstractDac;
using RemoteNetSpy.Models;
using System;
using System.Collections.ObjectModel;
using System.Reflection;

namespace DragDropExpressionBuilder
{
    public class PlaygroundViewModel
    {
        public ObservableCollection<object> ReservoirItems { get; } = new();
        public ObservableCollection<DroppedMethodItem> DroppedMethods { get; } = new();

        public void LoadDemoData()
        {
            ReservoirItems.Clear();
            DroppedMethods.Clear();
        }

        public void AddHeapObject(HeapObject heapObj)
        {
            if (!ReservoirItems.Contains(heapObj))
            {
                ReservoirItems.Add(heapObj);
            }
        }

        [Obsolete("Use AddHeapObject instead")]
        public void AddObject(object o, string tag) => ReservoirItems.Add(new Instance { Type = o.GetType(), Tag = tag, Obj = o });

        [Obsolete("Use AddHeapObject instead")]
        public void AddObject(object o, string tag, Type forcedType) => ReservoirItems.Add(new Instance { Type = forcedType, Tag = tag, Obj = o });

        [Obsolete("Methods should be accessed through HeapObject")]
        public void AddMethod(System.Reflection.MethodInfo mi) => ReservoirItems.Add(new MethodInfoWrapper(mi));
    }
}
