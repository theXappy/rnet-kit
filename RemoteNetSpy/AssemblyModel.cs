using RemoteNET;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RemoteNetSpy
{
    public class AssemblyModel
    {
        public string Name { get; private set; }
        public RuntimeType Runtime { get; private set; }
        public bool IsMonitoringAllocation { get; set; }

        /// <summary>
        /// Whether any types spotted inside this assembly
        /// </summary>
        public bool AnyTypes { get; set; }

        public AssemblyModel(string name, RuntimeType runtime, bool anyTypes)
        {
            Name = name;
            Runtime = runtime;
            IsMonitoringAllocation = false;
            AnyTypes = anyTypes;
        }

        public AssemblyModel(AssemblyDesc desc) : this(desc.Name, desc.Runtime, desc.AnyTypes)
        {
        }

        public override bool Equals(object? obj)
        {
            if (obj is not AssemblyDesc other) return false;
            // Intentionally not checking 'anyTypes'
            return Name.Equals(other?.Name) && Runtime.Equals(other?.Runtime);
        }

        public override int GetHashCode()
        {
            return Name.GetHashCode();
        }
    }
}
