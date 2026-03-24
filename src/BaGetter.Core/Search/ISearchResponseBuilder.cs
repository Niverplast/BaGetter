using System.Collections.Generic;
using BaGetter.Core.Metadata;
using BaGetter.Protocol.Models;

namespace BaGetter.Core.Search;

public interface ISearchResponseBuilder
{
    SearchResponse BuildSearch(IReadOnlyList<PackageRegistration> results);
    AutocompleteResponse BuildAutocomplete(IReadOnlyList<string> data);
    DependentsResponse BuildDependents(IReadOnlyList<PackageDependent> results);
}
