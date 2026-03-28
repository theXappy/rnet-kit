using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using CommandLine;
using RemoteNET;
using RemoteNET.Access;

namespace rnet_mem
{
    internal class Program
    {
        [Verb("read", HelpText = "Read memory from a target process")]
        public class ReadOptions
        {
            [Option('t', "target", Required = true, HelpText = "Target process name or PID.")]
            public string TargetProcess { get; set; }

            [Option('u', "unmanaged", Required = false, HelpText = "Whether the target is a native app")]
            public bool Unmanaged { get; set; }

            [Option('a', "address", Required = true, HelpText = "Address (hex, e.g. 0x7ffdf00) to read from")]
            public string Address { get; set; }

            [Option('n', "length", Required = true, HelpText = "Amount of bytes to read")]
            public int Length { get; set; }

            [Option("look-behind", Required = false, HelpText = "Amount of bytes to print BEFORE the address")]
            public int LookBehind { get; set; }

            [Option("launchdebugger", Required = false, HelpText = "Launch debugger and wait for it to attach.")]
            public bool LaunchDebugger { get; set; }
        }

        [Verb("write", HelpText = "Write memory to a target process")]
        public class WriteOptions
        {
            [Option('t', "target", Required = true, HelpText = "Target process name or PID.")]
            public string TargetProcess { get; set; }

            [Option('u', "unmanaged", Required = false, HelpText = "Whether the target is a native app")]
            public bool Unmanaged { get; set; }

            [Option('a', "address", Required = true, HelpText = "Address (hex) to write to")]
            public string Address { get; set; }

            [Option('b', "bytes", Required = true, HelpText = "Non-empty hex string of bytes to write (e.g. deadbeef)")]
            public string BytesHex { get; set; }

            [Option("launchdebugger", Required = false, HelpText = "Launch debugger and wait for it to attach.")]
            public bool LaunchDebugger { get; set; }
        }

        static int Main(string[] args)
        {
            if (args?.Any(a => a == "--launchdebugger") ?? false)
            {
                Debugger.Launch();
            }

            return Parser.Default.ParseArguments<ReadOptions, WriteOptions>(args)
                .MapResult(
                    (ReadOptions opts) => RunRead(opts),
                    (WriteOptions opts) => RunWrite(opts),
                    errs => HandleParseError(errs));
        }

        static int HandleParseError(IEnumerable<Error> errs)
        {
            return 1;
        }

        static RemoteApp Connect(string targetQuery, bool unmanaged)
        {
            RuntimeType runtime = RuntimeType.Managed;
            if (unmanaged)
                runtime = RuntimeType.Unmanaged;

            Process? targetProc = null;
            if (int.TryParse(targetQuery, out int pid))
            {
                try
                {
                    targetProc = Process.GetProcessById(pid);
                }
                catch
                {
                    // ignored
                }
            }

            return targetProc != null ?
                RemoteAppFactory.Connect(targetProc, runtime) :
                RemoteAppFactory.Connect(targetQuery, runtime);
        }

        static byte[] ParseHexString(string hex)
        {
            if (string.IsNullOrWhiteSpace(hex))
                throw new ArgumentException("Empty hex string");
            string s = hex.Trim();
            if (s.StartsWith("0x", StringComparison.InvariantCultureIgnoreCase))
                s = s.Substring(2);
            s = s.Replace(" ", "");
            if (s.Length % 2 != 0)
                throw new ArgumentException("Hex string must have even length");
            byte[] bytes = new byte[s.Length / 2];
            for (int i = 0; i < bytes.Length; i++)
            {
                bytes[i] = Convert.ToByte(s.Substring(i * 2, 2), 16);
            }
            return bytes;
        }

        static ulong ParseAddress(string addr)
        {
            string s = addr.Trim();
            if (s.StartsWith("0x", StringComparison.InvariantCultureIgnoreCase))
                s = s.Substring(2);
            return Convert.ToUInt64(s, 16);
        }

        static void PrintHexDump(ulong startAddress, byte[] data)
        {
            const int width = 16;
            for (int i = 0; i < data.Length; i += width)
            {
                int take = Math.Min(width, data.Length - i);
                ulong addr = startAddress + (ulong)i;
                StringBuilder hex = new StringBuilder();
                StringBuilder chars = new StringBuilder();
                for (int j = 0; j < take; j++)
                {
                    hex.AppendFormat("{0:X2} ", data[i + j]);
                    byte b = data[i + j];
                    // Latin-1 direct mapping: display printable range 32-126, else '.'
                    chars.Append((b >= 32 && b <= 126) ? (char)b : '.');
                }
                // pad hex to full 16 bytes
                if (take < width)
                {
                    int missing = width - take;
                    for (int k = 0; k < missing; k++)
                        hex.Append("   ");
                }
                Console.WriteLine($"0x{addr:X16}  {hex.ToString()} {chars.ToString()}");
            }
        }

        static byte[] ReadRemoteBytes(RemoteApp app, bool unmanaged, ulong address, int length)
        {
            if (length <= 0)
                return Array.Empty<byte>();
            // Use RemoteApp marshaler for all reads (follows RemoteNetSpy MemoryViewPanel pattern)
            RemoteMarshal marshal = app.Marshal;
            if (marshal == null)
                throw new Exception("Remote marshaler not available for this target");
            byte[] buffer = new byte[length];
            marshal.Read(new IntPtr(unchecked((long)address)), buffer, 0, length);
            return buffer;
        }

        static void WriteRemoteBytes(RemoteApp app, bool unmanaged, ulong address, byte[] data)
        {
            if (data == null || data.Length == 0)
                throw new ArgumentException("No data to write");
            // Use RemoteApp marshaler for all writes
            RemoteMarshal marshal = app.Marshal;
            if (marshal == null)
                throw new Exception("Remote marshaler not available for this target");
            marshal.Write(data, 0, new IntPtr(unchecked((long)address)), data.Length);
        }

        static int RunRead(ReadOptions opts)
        {
            if (opts.LaunchDebugger)
                Debugger.Launch();

            RemoteApp app;
            try
            {
                app = Connect(opts.TargetProcess, opts.Unmanaged);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("ERROR: " + ex.Message);
                return 1;
            }

            ulong address;
            try { address = ParseAddress(opts.Address); } catch (Exception ex) { Console.Error.WriteLine("ERROR: bad address: " + ex.Message); return 1; }

            int lookBehind = Math.Max(0, opts.LookBehind);
            int length = opts.Length;
            if (length <= 0)
            {
                Console.Error.WriteLine("ERROR: length must be > 0");
                return 1;
            }

            ulong startAddress = address;
            if (lookBehind > 0 && address > (ulong)lookBehind)
                startAddress = address - (ulong)lookBehind;
            else if (lookBehind > 0 && address <= (ulong)lookBehind)
                startAddress = 0;

            int totalToRead = length + (int)(address - startAddress);

            try
            {
                byte[] data = ReadRemoteBytes(app, opts.Unmanaged, startAddress, totalToRead);
                PrintHexDump(startAddress, data);
                app.Dispose();
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("ERROR: " + ex.Message);
                try { app.Dispose(); } catch { }
                return 1;
            }
        }

        static int RunWrite(WriteOptions opts)
        {
            if (opts.LaunchDebugger)
                Debugger.Launch();

            RemoteApp app;
            try
            {
                app = Connect(opts.TargetProcess, opts.Unmanaged);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("ERROR: " + ex.Message);
                return 1;
            }

            ulong address;
            try { address = ParseAddress(opts.Address); } catch (Exception ex) { Console.Error.WriteLine("ERROR: bad address: " + ex.Message); return 1; }

            byte[] data;
            try { data = ParseHexString(opts.BytesHex); } catch (Exception ex) { Console.Error.WriteLine("ERROR: bad hex string: " + ex.Message); return 1; }

            try
            {
                WriteRemoteBytes(app, opts.Unmanaged, address, data);
                // Read back and print hexdump of written region
                byte[] readBack = ReadRemoteBytes(app, opts.Unmanaged, address, data.Length);
                PrintHexDump(address, readBack);
                app.Dispose();
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("ERROR: " + ex.Message);
                try { app.Dispose(); } catch { }
                return 1;
            }
        }
    }
}
