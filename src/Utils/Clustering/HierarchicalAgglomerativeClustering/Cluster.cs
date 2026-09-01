using System.Collections;
using System.Text;

namespace Aspose.Pdf.Utils.Clustering.HierarchicalAgglomerativeClustering;

/// <summary>
/// An immutable set of instances grouped by hierarchical agglomerative
/// clustering. A merged cluster remembers the two parents it was formed from
/// and the linkage distance at which they merged.
/// </summary>
internal class Cluster<T> : IEnumerable<T>
{
    private readonly T[] _items;

    /// <summary>The single shared empty cluster.</summary>
    public static readonly Cluster<T> Empty = new(Array.Empty<T>(), 0);

    /// <summary>Merge two parent clusters at the given linkage distance.</summary>
    public Cluster(Cluster<T> parent1, Cluster<T> parent2, double distance)
    {
        Parent1 = parent1;
        Parent2 = parent2;
        Distance = distance;
        _items = new T[parent1.Count + parent2.Count];
        parent1._items.CopyTo(_items, 0);
        parent2._items.CopyTo(_items, parent1.Count);
    }

    /// <summary>A singleton cluster holding one instance.</summary>
    public Cluster(T instance, double distance = 0) : this(new[] { instance }, distance) { }

    /// <summary>A cluster over the given instances with the given distance.</summary>
    public Cluster(ICollection<T> instances, double distance)
    {
        if (instances is null) throw new ArgumentNullException(nameof(instances));
        _items = new T[instances.Count];
        instances.CopyTo(_items, 0);
        Distance = distance;
    }

    /// <summary>A cluster over the given instances at distance 0.</summary>
    public Cluster(ICollection<T> instances) : this(instances, 0) { }

    /// <summary>Copy another cluster (items, parents and distance).</summary>
    public Cluster(Cluster<T> cluster)
    {
        if (cluster is null) throw new ArgumentNullException(nameof(cluster));
        _items = (T[])cluster._items.Clone();
        Distance = cluster.Distance;
        Parent1 = cluster.Parent1;
        Parent2 = cluster.Parent2;
    }

    /// <summary>The instance at the given 0-based index.</summary>
    public T this[int index] => _items[index];

    /// <summary>Number of instances in the cluster.</summary>
    public int Count => _items.Length;

    /// <summary>The linkage distance at which this cluster was formed.</summary>
    public double Distance { get; }

    /// <summary>First parent this cluster was merged from (null for a leaf cluster).</summary>
    public Cluster<T>? Parent1 { get; }

    /// <summary>Second parent this cluster was merged from (null for a leaf cluster).</summary>
    public Cluster<T>? Parent2 { get; }

    /// <summary>A copy of this cluster.</summary>
    public Cluster<T> Clone() => new(this);

    /// <summary>Whether the cluster contains the given instance.</summary>
    public bool Contains(T item) => Array.IndexOf(_items, item) >= 0;

    /// <summary>The cluster's instances as a list.</summary>
    public List<T> ToList() => new(_items);

    public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)_items).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public override string ToString()
    {
        var sb = new StringBuilder("{");
        for (var i = 0; i < _items.Length; i++)
        {
            if (i > 0) sb.Append(", ");
            sb.Append(_items[i]);
        }
        return sb.Append('}').ToString();
    }
}
