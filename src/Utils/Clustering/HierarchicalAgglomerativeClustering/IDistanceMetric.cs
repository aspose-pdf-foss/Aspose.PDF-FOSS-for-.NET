namespace Aspose.Pdf.Utils.Clustering.HierarchicalAgglomerativeClustering;

/// <summary>Distance metric between two instances of <typeparamref name="T"/>
/// for hierarchical agglomerative clustering.</summary>
internal interface IDistanceMetric<in T>
{
    /// <summary>The distance between <paramref name="instance1"/> and <paramref name="instance2"/>.</summary>
    double CalculateDistance(T instance1, T instance2);
}
