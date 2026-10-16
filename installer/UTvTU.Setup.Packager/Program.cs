using System.Text.Json;
using UTvTU.Setup;

try {
    if (args is ["bundle", string host, string payload, string bundleDestination]) { PayloadContainer.Append(host, payload, bundleDestination); Console.WriteLine("完整单文件封装与校验通过。"); return 0; }
    object result = args switch {
        ["create", string directory, string destination] => PackageBuilder.Create(directory, destination),
        ["verify", string path] => PackageBuilder.Verify(path),
        ["validate", string directory] => PackageBuilder.ValidatePublished(directory),
        _ => throw new ArgumentException("用法：create <发布目录> <载荷ZIP> | verify <载荷ZIP> | validate <发布目录> | bundle <单文件宿主> <ZIP> <安装包>")
    };
    Console.WriteLine(JsonSerializer.Serialize(result, SetupJson.Options)); return 0;
} catch (Exception error) { Console.Error.WriteLine(error.Message); return 1; }
