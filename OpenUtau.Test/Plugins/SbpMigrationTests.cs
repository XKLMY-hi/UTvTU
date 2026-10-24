using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using OpenUtau.Plugin.Builtin;
using Xunit;

namespace OpenUtau.Plugins {
    public class SbpMigrationTests : IDisposable {
        readonly string folder = Path.Combine(Path.GetTempPath(), "utvtu-sbp-" + Guid.NewGuid().ToString("N"));
        sealed class Rules : SyllableBasedPhonemizer {
            protected override byte[] YamlTemplate => Encoding.UTF8.GetBytes("version: '2.0'\nsymbols: []\n");
            protected override string YamlVersion => "1.0";
            protected override string[] GetVowels() => new[] { "a", "A" };
            protected override List<string> ProcessSyllable(Syllable syllable) => new();
            protected override List<string> ProcessEnding(Ending ending) => new();
        }
        public SbpMigrationTests() { Directory.CreateDirectory(folder); }
        static void Migrate(string path) => typeof(SyllableBasedPhonemizer)
            .GetMethod("UpdateYamlIfNeeded", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(new Rules(), new object[] { path, "2.0", false });

        [Fact]
        public void InvalidRulesAreNeverReplacedByTheTemplate() {
            string path = Path.Combine(folder, "rules.yaml");
            const string original = "version: '1.0'\nreplacements: [broken\n";
            File.WriteAllText(path, original);
            Migrate(path);
            Assert.Equal(original, File.ReadAllText(path));
            Assert.Empty(Directory.GetFiles(folder, "*.tmp"));
        }

        [Fact]
        public void CustomFieldsSurviveAndTheOriginalGetsAUniqueBackup() {
            string path = Path.Combine(folder, "rules.yaml");
            const string original = "version: '1.0'\nuser_marker: keep_me\n";
            File.WriteAllText(path, original);
            Migrate(path);
            string updated = File.ReadAllText(path);
            Assert.Contains("2.0", updated);
            Assert.Contains("keep_me", updated);
            Assert.Equal(original, File.ReadAllText(Assert.Single(Directory.GetFiles(folder, "*.bak"))));
            Migrate(path);
            Assert.Single(Directory.GetFiles(folder, "*.bak"));
            Assert.Empty(Directory.GetFiles(folder, "*.tmp"));
        }

        [Fact]
        public void ReadOnlyRulesRemainUnchangedAfterAWriteFailure() {
            string path = Path.Combine(folder, "rules.yaml");
            const string original = "version: '1.0'\nuser_marker: keep_me\n";
            File.WriteAllText(path, original);
            File.SetAttributes(path, FileAttributes.ReadOnly);
            try { Migrate(path); Assert.Equal(original, File.ReadAllText(path)); }
            finally { File.SetAttributes(path, FileAttributes.Normal); }
            Assert.Empty(Directory.GetFiles(folder, "*.tmp"));
        }
        public void Dispose() {
            foreach (var file in Directory.GetFiles(folder)) {
                File.SetAttributes(file, FileAttributes.Normal);
                File.Delete(file);
            }
            Directory.Delete(folder);
        }
    }
}
