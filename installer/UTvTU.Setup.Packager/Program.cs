using System.Text.Json;
using UTvTU.Setup;

try {
    object result = args switch {
        ["create", string directory, string destination] => PackageBuilder.Create(directory, destination),
        ["verify", string path] => PackageBuilder.Verify(path),
        ["validate", string directory] => PackageBuilder.ValidatePublished(directory),
        _ => throw new ArgumentException("用法：create <发布目录> <载荷ZIP> | verify <载荷ZIP> | validate <发布目录>")
    };
    Console.WriteLine(JsonSerializer.Serialize(result, SetupJson.Options)); return 0;
} catch (Exception error) { Console.Error.WriteLine(error.Message); return 1; }
