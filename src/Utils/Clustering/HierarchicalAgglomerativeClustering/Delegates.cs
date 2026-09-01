namespace Aspose.Pdf.Utils.Clustering.HierarchicalAgglomerativeClustering;

/// <summary>Computes the centroid instance of a cluster (used by centroid-based
/// linkage criteria).</summary>
internal delegate T CentroidFunction<T>(Cluster<T> cluster);
