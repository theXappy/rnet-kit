# RemoteNetSpy
`RemoteNetSpy.exe` is a GUI application utilizing the RemoteNET library.  
Use this app to research and explore .NET targets:
1. Find loaded assemblies & types
2. Find and inspect every object instance
3. Quickly set hooks on methods to trace when they're called.


# rnet-kit
This repo also contains other programs that use RemoteNET. Together with RemoteNetSpy, they are called the *rnet-kit*.  
Every other program has a command-line interface and is meant for very specific tasks.  
RemoteNetSpy uses some of those command-line programs to gather information. Using RemoteNetSpy itself should be enough for most use cases.  

The other utilities in this repo:
1. `rnet-trace.exe`  
Hooks arbitrary methods and prints when they are called and with which parameters.
2. `rnet-dump.exe`  
Dump specific peices of info from a .NET process. Like Assemblies, Types, Object instances in the heap.
3. `rnet-ps.exe`  
List injectable .NET processes in the system. Only processes that return from rnet-ps can be used with the other utils.
4. `rnet-repl.exe`  
A REPL console that allows low level access to a target app. You can grab objects, exmaine or modify them, hook methods and even inejct other .NET assemblies.
