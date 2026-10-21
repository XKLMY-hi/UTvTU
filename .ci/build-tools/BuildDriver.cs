using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;

class BuildDriver {
    static int Main(string[] args) {
        var sdk = Path.GetFullPath(args[0]);
        AssemblyLoadContext.Default.Resolving += (_, name) => {
            var path = Path.Combine(sdk, name.Name + ".dll");
            return File.Exists(path) ? AssemblyLoadContext.Default.LoadFromAssemblyPath(path) : null;
        };
        foreach (var filename in new[] { "Microsoft.Build.dll", "MSBuild.dll", "Microsoft.Build.Framework.dll", "Microsoft.Build.Utilities.Core.dll", "Microsoft.Build.Tasks.Core.dll" }) {
            var build = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(sdk, filename));
            var utilities = build.GetType("Microsoft.Build.Shared.EnvironmentUtilities", false);
            if (utilities == null) continue;
            foreach (var field in utilities.GetFields(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)) {
                if (args[1] == "--inspect") Console.WriteLine(filename + " " + field.Name + " " + field.FieldType);
                if (field.FieldType == typeof(string) && field.Name.Contains("processName", StringComparison.OrdinalIgnoreCase)) field.SetValue(null, "MSBuild");
            }
        }
        var nuget = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(sdk, "NuGet.Common.dll"));
        var migrations = nuget.GetType("NuGet.Common.Migrations.MigrationRunner", true);
        foreach (var field in migrations.GetFields(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)) {
            if (args[1] == "--inspect") Console.WriteLine("MigrationRunner " + field.Name + " " + field.FieldType);
        }
        if (args[1] == "--inspect") return 0;
        Environment.SetEnvironmentVariable("MSBUILD_EXE_PATH", Path.Combine(sdk, "MSBuild.dll"));
        Environment.SetEnvironmentVariable("MSBuildSDKsPath", Path.Combine(sdk, "Sdks"));
        Environment.SetEnvironmentVariable("MSBuildExtensionsPath", sdk);
        var executable = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(sdk, "MSBuild.dll"));
        try { return (int)executable.EntryPoint.Invoke(null, new object[] { args.Skip(1).ToArray() }); }
        catch (TargetInvocationException error) { Console.Error.WriteLine(error.InnerException); return 1; }
    }
}
