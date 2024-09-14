# RemoteNET Spy
`RemoteNetSpy.exe` is a GUI application utilizing the [RemoteNET library](https://github.com/theXappy/RemoteNET).  
Use this app to research and explore .NET apps:
1. Find loaded assemblies & types
2. Find and inspect every object instance
3. Quickly set hooks on methods to trace when they're called.

### ✳️ Download compiled binaries at the [Releases](https://github.com/theXappy/rnet-kit/releases) page.

![screenshot](https://raw.githubusercontent.com/theXappy/rnet-kit/main/pr.png)

# Installation
1. Install [.NET 7](https://dotnet.microsoft.com/en-us/download/dotnet/thank-you/runtime-desktop-7.0.20-windows-x64-installer).
2. Install [Microsoft Visual C++ 2015-2022 Redistributable (x64)](https://aka.ms/vs/17/release/vc_redist.x64.exe).
3. Download the `zip` file from the [Releases](https://github.com/theXappy/rnet-kit/releases) page.
4. Run `RemoteNetSpy.exe`

# rnet-kit
This repo also contains other programs that use RemoteNET.  
Together with RemoteNetSpy, they are called the **rnet-kit**.   
Every other program has a command-line interface and is meant for specific, "lower-level", tasks.  
RemoteNetSpy uses some of those command-line programs to gather information and operate.  
Using RemoteNetSpy itself should be enough for most research use-cases.  

The other utilities in this repo:
1. `rnet-trace.exe`  
Hooks arbitrary methods and prints when they are called and with which parameters.
2. `rnet-dump.exe`  
Dump specific peices of info from a .NET process. Like Assemblies, Types, Object instances in the heap.
3. `rnet-ps.exe`  
List injectable .NET processes in the system. Only processes that return from rnet-ps can be used with the other utils.
4. `rnet-repl.exe`  
A REPL console that allows low level access to a target app. You can grab objects, exmaine or modify them, hook methods and even inejct other .NET assemblies.


## Thanks
**icons8** for the "Puppet" icon
