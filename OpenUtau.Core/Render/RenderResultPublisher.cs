using System.Linq;
using System.Threading;
using OpenUtau.Core.Ustx;

namespace OpenUtau.Core.Render {
    /// <summary>Background samples fill their request's signal sources. Publishing
    /// a document projection is a separate, version-checked operation on its owner thread.</summary>
    internal static class RenderResultPublisher {
        public static void Queue(UProject project, RenderPartRequest request, CancellationToken token) {
            if (token.IsCancellationRequested || !request.sources.All(s => s.HasSamples) ||
                Interlocked.CompareExchange(ref request.publicationScheduled, 1, 0) != 0) return;
            var doc = DocManager.Inst;
            void Complete() {
                if (token.IsCancellationRequested || !ReferenceEquals(project, doc.Project)) return;
                lock (project) {
                    if (!project.parts.Contains(request.part) || !request.part.TrySetMix(request.mix, request.revision)) return;
                }
                doc.ExecuteCmd(new PartRenderedNotification(request.part));
            }
            if (doc.PostOnUIThread != null) doc.PostOnUIThread(Complete);
            else Complete(); // Deterministic model-only hosts may have no UI dispatcher.
        }
    }
}
