using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using CommandLine;
using Pastel;
using RemoteNET;
using ScubaDiver.API.Hooking;
using Color = System.Drawing.Color;

namespace QuickStart
{
    class Program
    {
        static void Main(string[] args)
        {
            string path = typeof(Program).Assembly.Location;
            if (args.Length < 1)
            {
                Console.WriteLine($"USAGE: {Path.GetFileName(path)}");
                return;
            }
            string subexecutable = args[0];
            var executionDir = Path.GetDirectoryName(path);

            string[] matches = Directory.GetFileSystemEntries(executionDir).Where(file => file.Contains(subexecutable) && file.EndsWith("dll")).ToArray();
            if (matches.Length != 1)
            {
                Console.WriteLine($"Expected 1 match for '{subexecutable}' but found {matches.Length}");
                return;
            }

            string match = matches.Single();
            var matchingAssembly = Assembly.LoadFile(match);
            var programClass = matchingAssembly.GetTypes().Single(t => t.Name.Contains("Program"));
            var main = programClass.GetMethod("Main", (BindingFlags)0xffffff);
            main.Invoke(null, new object[1] {args.Skip(1).ToArray()});
        }
    }
}