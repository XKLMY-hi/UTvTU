using System;
using System.Threading;
using OpenUtau.Core.Ustx;

namespace OpenUtau.Core.Render {
    /// <summary>Check ownership when a queued notification actually reaches the UI.</summary>
    internal static class RenderNotificationPublisher {
        public static void Queue(UProject project, CancellationToken cancellation,
            UNotification notification, Func<bool>? isCurrent = null) {
            var doc = DocManager.Inst;
            if (cancellation.IsCancellationRequested || !ReferenceEquals(project, doc.Project)) return;
            void Publish() {
                if (!cancellation.IsCancellationRequested && ReferenceEquals(project, doc.Project) &&
                    (isCurrent == null || isCurrent())) doc.ExecuteCmd(notification);
            }
            if (doc.PostOnUIThread != null) doc.PostOnUIThread(Publish);
            else Publish();
        }
    }
}
