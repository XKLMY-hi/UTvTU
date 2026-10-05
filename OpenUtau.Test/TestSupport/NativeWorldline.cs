using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace OpenUtau.Test.TestSupport {
    /// <summary>
    /// 本机原生 worldline 库的定位与预加载（W7 验收装置的基础设施）。
    ///
    /// 背景：<c>OpenUtau.Core</c> 通过 <c>DllImport("worldline")</c> 调用 C++ 库
    /// （经典 resampler、WORLDLINE-R 渲染器、音频设备）。仓库里
    /// <c>runtimes/&lt;rid&gt;/native/worldline.*</c> 是**已提交的原生二进制**
    /// （无需 Bazel、无需联网）；但 Debug 构建只把它复制到
    /// <c>&lt;out&gt;/runtimes/&lt;rid&gt;/native/</c>，只有
    /// <c>-p:RuntimeIdentifier=win-x64</c> 时才复制到应用根目录 —— 而 .NET 的
    /// DllImport 默认只在应用目录探测，故测试宿主里直接调用会 DllNotFoundException。
    ///
    /// 这里显式 <see cref="NativeLibrary.Load(string)"/> 一个绝对路径：模块一旦按基名
    /// 载入进程，后续 <c>DllImport("worldline")</c> 就能解析到它（Windows 加载器语义；
    /// Linux/macOS dlopen 同理）。**不修改产品代码**，只让测试装置可用。
    /// </summary>
    internal static class NativeWorldline {
        static readonly object gate = new object();
        static bool attempted;
        static bool loaded;
        static string detail = string.Empty;
        static IntPtr handle;

        public static string FileName { get; } = PlatformFileName();
        public static string RidFolder { get; } = PlatformRidFolder();

        static string PlatformFileName() =>
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "worldline.dll"
            : RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "libworldline.dylib"
            : "libworldline.so";

        static string PlatformRidFolder() {
            string arch = RuntimeInformation.ProcessArchitecture switch {
                Architecture.X64 => "x64",
                Architecture.X86 => "x86",
                Architecture.Arm64 => "arm64",
                _ => "x64",
            };
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) {
                return "osx";
            }
            return RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? $"win-{arch}" : $"linux-{arch}";
        }

        /// <summary>候选路径：应用目录 → 逐级上溯的 runtimes/&lt;rid&gt;/native（覆盖 bin/Debug/netX → 仓库根）。</summary>
        public static IEnumerable<string> CandidatePaths() {
            string baseDir = AppContext.BaseDirectory;
            yield return Path.Combine(baseDir, FileName);
            DirectoryInfo? dir = new DirectoryInfo(baseDir);
            for (int i = 0; i < 8 && dir != null; i++, dir = dir.Parent) {
                yield return Path.Combine(dir.FullName, "runtimes", RidFolder, "native", FileName);
            }
        }

        public static string? FindLibraryPath() => CandidatePaths().FirstOrDefault(File.Exists);

        public static string SearchReport() =>
            $"未找到原生库 {FileName}（候选：{string.Join(" | ", CandidatePaths().Take(4))} …）";

        /// <summary>幂等的加载尝试；返回本机原生调用是否可用。</summary>
        public static bool EnsureLoaded(out string reason) {
            lock (gate) {
                if (attempted) {
                    reason = detail;
                    return loaded;
                }
                attempted = true;
                // 1) 交给默认探测（应用目录 / PATH / 已加载模块）
                if (NativeLibrary.TryLoad(FileName, out handle)) {
                    loaded = true;
                    detail = $"加载器已能解析 {FileName}（无需显式预加载）";
                    reason = detail;
                    return loaded;
                }
                // 2) 显式按绝对路径预加载
                string? path = FindLibraryPath();
                if (path == null) {
                    loaded = false;
                    detail = SearchReport();
                    reason = detail;
                    return loaded;
                }
                try {
                    handle = NativeLibrary.Load(path);
                    loaded = true;
                    detail = $"NativeLibrary.Load 成功：{path}";
                } catch (Exception e) {
                    loaded = false;
                    detail = $"NativeLibrary.Load 失败：{path} → {e.GetType().Name}: {e.Message}";
                }
                reason = detail;
                return loaded;
            }
        }

        /// <summary>供报告/日志用的诊断行。</summary>
        public static string Diagnostics() {
            string found = FindLibraryPath() ?? "(未找到)";
            EnsureLoaded(out string reason);
            return $"platform={RidFolder} file={FileName} found={found} -> {reason}";
        }
    }
}
