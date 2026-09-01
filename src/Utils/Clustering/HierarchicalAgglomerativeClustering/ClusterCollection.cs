using System.Collections;
using System.Text;

namespace Aspose.Pdf.Utils.Clustering.HierarchicalAgglomerativeClustering;

/// <summary>
/// The set of clusters present at one level of an agglomerative clustering
/// run, tagged with the linkage distance of the merge that produced the level.
/// </summary>
internal class ClusterCollection<T> : IEnumerable<Cluster<T>>
{
    private readonly Cluster<T>[] _clusters;

    /// <summary>A collection over the given clusters.</summary>
    public ClusterCollection(Cluster<T>[] clusters, double distance = 0)
    {
        _clusters = clusters ?? throw new ArgumentNullException(nameof(clusters));
        Distance = distance;
    }

    /// <summary>Number of clusters at this level.</summary>
    public int Count => _clusters.Length;

    /// <summary>The linkage distance of the merge that produced this level.</summary>
    public double Distance { get; }

    /// <summary>The cluster at the given 0-based index.</summary>
    public Cluster<T> this[int index] => _clusters[index];

    public IEnumerator<Cluster<T>> GetEnumerator() => ((IEnumerable<Cluster<T>>)_clusters).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public override string ToString()
    {
        var sb = new StringBuilder("[");
        for (var i = 0; i < _clusters.Length; i++)
        {
            if (i > 0) sb.Append(", ");
            sb.Append(_clusters[i]);
        }
        return sb.Append(']').ToString();
    }
}
