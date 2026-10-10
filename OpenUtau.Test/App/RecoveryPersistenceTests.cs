using System;
using System.IO;
using System.Reflection;
using OpenUtau.Core;
using OpenUtau.Core.Format;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Util;
using OpenUtau.Test.TestSupport;
using Xunit;

namespace OpenUtau.Test.App {
    [Collection("WorkspaceSession")]
    public class RecoveryPersistenceTests : IDisposable {
        readonly DocManagerTestSetup.ScopedDispatcher dispatcher =
            DocManagerTestSetup.EnterScopedDispatcher(nullChannel: true, installScheduler: false);
        readonly string directory = Path.Combine(Path.GetTempPath(), "utvtu-recovery-" + Guid.NewGuid().ToString("N"));
        readonly PropertyInfo dataPath = typeof(PathManager).GetProperty(nameof(PathManager.DataPath))!;
        readonly string previousDataPath = PathManager.Inst.DataPath;
        readonly Preferences.SerializablePreferences previousPreferences = Preferences.Default;
        readonly UProject previousProject = DocManager.Inst.Project;

        public RecoveryPersistenceTests() {
            Directory.CreateDirectory(directory);
            dataPath.SetValue(PathManager.Inst, directory);
            Preferences.Default = new Preferences.SerializablePreferences { RecoveryPath = "previous.ustxp" };
        }

        UProject Project() => new UProject {
            name = "Recovery", FilePath = Path.Combine(directory, "song.ustxp"), Saved = false,
        };

        [Fact]
        public void RecoverySave_PublishesCompleteFileWithoutChangingDocumentIdentity() {
            var project = Project();
            project.tracks[0].Volume = -7;
            string path = Path.Combine(directory, "recovery.ustxp");
            Assert.True(Ustxp.TryAutoSave(path, project));
            Assert.Equal(project.FilePath, Path.Combine(directory, "song.ustxp"));
            Assert.False(project.Saved);
            Assert.Equal(path, Preferences.Default.RecoveryPath);
            var loaded = Ustxp.Load(path);
            Assert.Equal("Recovery", loaded.name);
            Assert.Equal(-7, loaded.tracks[0].Volume);
            Assert.Null(project.voiceParts);
            Assert.Null(project.waveParts);
            Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
        }

        [Fact]
        public void FailedReplacement_PreservesLastRecoveryAndCleansSerializationState() {
            var project = Project();
            string path = Path.Combine(directory, "recovery.ustxp");
            File.WriteAllText(path, "last valid recovery");
            using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None)) {
                Assert.False(Ustxp.TryAutoSave(path, project));
            }
            Assert.Equal("last valid recovery", File.ReadAllText(path));
            Assert.Equal("previous.ustxp", Preferences.Default.RecoveryPath);
            Assert.Null(project.voiceParts);
            Assert.Null(project.waveParts);
            Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
        }

        [Fact]
        public void FailedRecoveryLocator_IsReportedAndRemainsEligibleForRetry() {
            var project = Project();
            DocManager.Inst.ExecuteCmd(new LoadProjectNotification(project));
            DocManager.Inst.MarkProjectModified();
            Directory.CreateDirectory(PathManager.Inst.PrefsFilePath); // Block preferences publication.
            DocManager.Inst.AutoSave();
            Assert.Equal("previous.ustxp", Preferences.Default.RecoveryPath);
            project.name = "Latest edit";
            Directory.Delete(PathManager.Inst.PrefsFilePath); // This owned directory is empty.
            DocManager.Inst.AutoSave();
            string path = Path.Combine(directory, "song-autosave.ustxp");
            Assert.Equal(path, Preferences.Default.RecoveryPath);
            Assert.Equal("Latest edit", Ustxp.Load(path).name); // Failed checkpoint did not suppress retry.
            Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
        }

        [Fact]
        public void FailedPreferencesWrite_PreservesPreviouslyPersistedSettings() {
            Assert.True(Preferences.TrySave());
            string path = PathManager.Inst.PrefsFilePath;
            string previous = File.ReadAllText(path);
            Preferences.Default.RecoveryPath = "new.ustxp";
            using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None)) {
                Assert.False(Preferences.TrySave());
            }
            Assert.Equal(previous, File.ReadAllText(path));
            Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
        }

        public void Dispose() {
            DocManager.Inst.ExecuteCmd(new LoadProjectNotification(previousProject));
            Preferences.Default = previousPreferences;
            dataPath.SetValue(PathManager.Inst, previousDataPath);
            dispatcher.Dispose();
            Directory.Delete(directory, recursive: true);
        }
    }
}
