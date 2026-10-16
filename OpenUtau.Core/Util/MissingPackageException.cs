using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace OpenUtau.Core;

public sealed class MissingPackageException : MessageCustomizableException {
    public string PackageId { get; }
    public MissingPackageException(string packageId, Exception inner = null) : base(
        $"Package {packageId} not found", "<translate:packages.errors.missing>",
        inner ?? new System.IO.FileNotFoundException(packageId), false, new object[] { packageId }) {
        PackageId = packageId;
    }
    // Match catalogue identifiers only; voicebank paths never become install requests.
    public static bool IsRegistryId(string id) => !string.IsNullOrWhiteSpace(id) &&
        Regex.IsMatch(id, @"\A[A-Za-z0-9][A-Za-z0-9._-]*\z", RegexOptions.CultureInvariant) && id != "." && id != "..";

    public static IReadOnlyList<string> Find(Exception exception) {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var visited = new HashSet<Exception>();
        void Walk(Exception error) {
            if (error == null || !visited.Add(error)) return;
            if (error is MissingPackageException missing && IsRegistryId(missing.PackageId)) result.Add(missing.PackageId);
            if (error is MessageCustomizableException custom) {
                // Its constructor flattens nested customizable exceptions, preserving the template/arguments.
                if (custom.TranslatableMessage == "<translate:packages.errors.missing>" &&
                    custom.Replaces is { Length: 1 } && custom.Replaces[0] is string id && IsRegistryId(id)) result.Add(id);
                Walk(custom.SubstanceException);
            }
            if (error is AggregateException aggregate) foreach (var inner in aggregate.InnerExceptions) Walk(inner);
            Walk(error.InnerException);
        }
        Walk(exception);
        return result.OrderBy(id => id, StringComparer.OrdinalIgnoreCase).ToArray();
    }
}
