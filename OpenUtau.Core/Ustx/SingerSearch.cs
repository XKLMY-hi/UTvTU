using System.Globalization;
using System.Linq;

namespace OpenUtau.Core.Ustx;

public static class SingerSearch {
    public static bool Matches(USinger singer, string query) {
        if (string.IsNullOrWhiteSpace(query)) return true;
        var terms = new[] { singer.Name, singer.LocalizedName, singer.Id, singer.Location }
            .Concat(singer.LocalizedNames?.Values ?? Enumerable.Empty<string>())
            .Concat(singer.SearchTerms ?? System.Array.Empty<string>());
        return query.Split((char[])null, System.StringSplitOptions.RemoveEmptyEntries)
            .All(word => terms.Any(term => term != null && CultureInfo.InvariantCulture.CompareInfo.IndexOf(
                term, word, CompareOptions.IgnoreCase | CompareOptions.IgnoreWidth | CompareOptions.IgnoreKanaType) >= 0));
    }
}
