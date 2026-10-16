using UTvTU.Setup;
using Xunit;

public class SetupOperationLeaseTests {
    [Fact] public void WholeOperationGateRejectsAnotherTaskForTheSameInstall() {
        string directory = Path.Combine(Path.GetTempPath(), "UTvTU-lease-" + Guid.NewGuid());
        using (var first = new SetupOperationLease(directory, "user")) {
            Assert.Throws<IOException>(() => new SetupOperationLease(directory, "user"));
            using var independent = new SetupOperationLease(directory + "-other", "user");
        }
        using var after = new SetupOperationLease(directory, "user");
    }
    [Fact] public async Task OperationGateCanBeReleasedByAnAsyncContinuation() {
        string directory = Path.Combine(Path.GetTempPath(), "UTvTU-lease-" + Guid.NewGuid());
        var first = new SetupOperationLease(directory, "user");
        await Task.Run(first.Dispose);
        using var next = new SetupOperationLease(directory, "user");
    }
}
