using System;
using System.Collections.Generic;

namespace Aspose.Pdf.Utils.Clustering.HierarchicalAgglomerativeClustering.Metrics
{
    /// <summary>Shared plumbing for the cluster-validity indices: the sums of squares,
    /// the per-cluster centroids and the separations every index is built from.
    ///
    /// Sign convention: an index whose textbook form is "smaller is better" is returned
    /// NEGATED, so that across the whole family a LARGER evaluation value always means a
    /// better clustering and a caller can simply take the maximum.</summary>
    internal abstract class ClusterValidityIndexBase<T> : IMetricsCriterion<T>
    {
        protected ClusterValidityIndexBase(IDistanceMetric<T> distanceMetric)
        {
            DistanceMetric = distanceMetric ?? throw new ArgumentNullException(nameof(distanceMetric));
        }

        protected ClusterValidityIndexBase(IDistanceMetric<T> distanceMetric, CentroidFunction<T> centroidFunc)
            : this(distanceMetric)
        {
            CentroidFunc = centroidFunc ?? throw new ArgumentNullException(nameof(centroidFunc));
        }

        public IDistanceMetric<T> DistanceMetric { get; }

        protected CentroidFunction<T>? CentroidFunc { get; }

        public abstract double EvaluateMetric(ClusterCollection<T> clusterCollection);

        protected double Distance(T a, T b) => DistanceMetric.CalculateDistance(a, b);

        /// <summary>Total number of instances across every cluster of the level.</summary>
        protected static int InstanceCount(ClusterCollection<T> level)
        {
            var n = 0;
            foreach (var cluster in level) n += cluster.Count;
            return n;
        }

        protected static List<T> Instances(ClusterCollection<T> level)
        {
            var all = new List<T>(InstanceCount(level));
            foreach (var cluster in level)
                foreach (var item in cluster)
                    all.Add(item);
            return all;
        }

        protected T[] Centroids(ClusterCollection<T> level)
        {
            var centroids = new T[level.Count];
            for (var i = 0; i < level.Count; i++) centroids[i] = CentroidFunc!(level[i]);
            return centroids;
        }

        /// <summary>Centroid of every instance in the level, i.e. of the whole data set.</summary>
        protected T GlobalCentroid(ClusterCollection<T> level) =>
            CentroidFunc!(new Cluster<T>(Instances(level)));

        /// <summary>Within-cluster sum of squares: every instance's squared distance to
        /// its own cluster's centroid.</summary>
        protected double WithinSumOfSquares(ClusterCollection<T> level, T[] centroids)
        {
            var ssw = 0d;
            for (var i = 0; i < level.Count; i++)
                foreach (var item in level[i])
                {
                    var d = Distance(item, centroids[i]);
                    ssw += d * d;
                }
            return ssw;
        }

        /// <summary>Total sum of squares: every instance's squared distance to the global
        /// centroid. Independent of how the level is partitioned.</summary>
        protected double TotalSumOfSquares(ClusterCollection<T> level)
        {
            var global = GlobalCentroid(level);
            var sst = 0d;
            foreach (var cluster in level)
                foreach (var item in cluster)
                {
                    var d = Distance(item, global);
                    sst += d * d;
                }
            return sst;
        }

        /// <summary>Smallest distance between any two cluster centroids.</summary>
        protected double MinCentroidSeparation(T[] centroids)
        {
            var min = double.MaxValue;
            for (var i = 0; i < centroids.Length; i++)
                for (var j = i + 1; j < centroids.Length; j++)
                {
                    var d = Distance(centroids[i], centroids[j]);
                    if (d < min) min = d;
                }
            return min == double.MaxValue ? 0 : min;
        }

        /// <summary>Largest distance between any two cluster centroids.</summary>
        protected double MaxCentroidSeparation(T[] centroids)
        {
            var max = 0d;
            for (var i = 0; i < centroids.Length; i++)
                for (var j = i + 1; j < centroids.Length; j++)
                {
                    var d = Distance(centroids[i], centroids[j]);
                    if (d > max) max = d;
                }
            return max;
        }

        /// <summary>Mean distance from a cluster's members to its centroid - the
        /// cluster's scatter.</summary>
        protected double Scatter(Cluster<T> cluster, T centroid)
        {
            if (cluster.Count == 0) return 0;
            var sum = 0d;
            foreach (var item in cluster) sum += Distance(item, centroid);
            return sum / cluster.Count;
        }
    }

    /// <summary>R-squared: the share of the total sum of squares explained by the
    /// partition, 1 - SSW/SST. Runs from 0 (one blob) to 1 (every instance alone).</summary>
    internal class RSquared<T> : ClusterValidityIndexBase<T>
    {
        public RSquared(IDistanceMetric<T> distanceMetric, CentroidFunction<T> centroidFunc)
            : base(distanceMetric, centroidFunc) { }

        public override double EvaluateMetric(ClusterCollection<T> clusterCollection)
        {
            var sst = TotalSumOfSquares(clusterCollection);
            var ssw = WithinSumOfSquares(clusterCollection, Centroids(clusterCollection));
            return 1 - ssw / sst;
        }
    }

    /// <summary>Root mean square standard deviation: sqrt(SSW / N), the typical distance
    /// of an instance from its own cluster's centroid.</summary>
    internal class RootMeanSquareStdDev<T> : ClusterValidityIndexBase<T>
    {
        public RootMeanSquareStdDev(IDistanceMetric<T> distanceMetric, CentroidFunction<T> centroidFunc)
            : base(distanceMetric, centroidFunc) { }

        public override double EvaluateMetric(ClusterCollection<T> clusterCollection)
        {
            var n = InstanceCount(clusterCollection);
            if (n == 0) return 0;
            var ssw = WithinSumOfSquares(clusterCollection, Centroids(clusterCollection));
            return Math.Sqrt(ssw / n);
        }
    }

    /// <summary>Calinski-Harabasz (variance ratio): between-cluster spread per degree of
    /// freedom over within-cluster spread per degree of freedom. Undefined (NaN) once
    /// every instance is alone, where both terms vanish.</summary>
    internal class CalinskiHarabaszIndex<T> : ClusterValidityIndexBase<T>
    {
        public CalinskiHarabaszIndex(IDistanceMetric<T> distanceMetric, CentroidFunction<T> centroidFunc)
            : base(distanceMetric, centroidFunc) { }

        public override double EvaluateMetric(ClusterCollection<T> clusterCollection)
        {
            var k = clusterCollection.Count;
            var n = InstanceCount(clusterCollection);
            var sst = TotalSumOfSquares(clusterCollection);
            var ssw = WithinSumOfSquares(clusterCollection, Centroids(clusterCollection));
            return (sst - ssw) / (k - 1) / (ssw / (n - k));
        }
    }

    /// <summary>Dunn: the closest two clusters get, over the widest any one cluster
    /// spreads. Zero when no cluster has any extent (every instance alone).</summary>
    internal class DunnIndex<T> : ClusterValidityIndexBase<T>
    {
        public DunnIndex(IDistanceMetric<T> distanceMetric) : base(distanceMetric) { }

        public override double EvaluateMetric(ClusterCollection<T> clusterCollection)
        {
            var diameter = 0d;
            foreach (var cluster in clusterCollection)
                for (var i = 0; i < cluster.Count; i++)
                    for (var j = i + 1; j < cluster.Count; j++)
                    {
                        var d = Distance(cluster[i], cluster[j]);
                        if (d > diameter) diameter = d;
                    }
            if (diameter <= 0) return 0;

            var separation = double.MaxValue;
            for (var a = 0; a < clusterCollection.Count; a++)
                for (var b = a + 1; b < clusterCollection.Count; b++)
                    foreach (var x in clusterCollection[a])
                        foreach (var y in clusterCollection[b])
                        {
                            var d = Distance(x, y);
                            if (d < separation) separation = d;
                        }
            return separation == double.MaxValue ? 0 : separation / diameter;
        }
    }

    /// <summary>Silhouette coefficient: per instance, how much closer it sits to its own
    /// cluster than to the nearest other one, averaged over the data set. An instance
    /// alone in its cluster has no within-distance and contributes 0.</summary>
    internal class SilhouetteCoefficient<T> : ClusterValidityIndexBase<T>
    {
        public SilhouetteCoefficient(IDistanceMetric<T> distanceMetric) : base(distanceMetric) { }

        public override double EvaluateMetric(ClusterCollection<T> clusterCollection)
        {
            var n = InstanceCount(clusterCollection);
            if (n == 0) return 0;

            var total = 0d;
            for (var i = 0; i < clusterCollection.Count; i++)
            {
                var own = clusterCollection[i];

                // An instance alone in its cluster has nothing to be closer to: its
                // silhouette is not a perfect 1 but undefined, and counts as 0.
                if (own.Count < 2) continue;

                for (var m = 0; m < own.Count; m++)
                {
                    var item = own[m];

                    var within = 0d;
                    for (var other = 0; other < own.Count; other++)
                        if (other != m) within += Distance(item, own[other]);
                    within /= own.Count - 1;

                    var nearest = double.MaxValue;
                    for (var j = 0; j < clusterCollection.Count; j++)
                    {
                        if (j == i) continue;
                        var foreign = clusterCollection[j];
                        if (foreign.Count == 0) continue;
                        var mean = 0d;
                        foreach (var other in foreign) mean += Distance(item, other);
                        mean /= foreign.Count;
                        if (mean < nearest) nearest = mean;
                    }
                    if (nearest == double.MaxValue) continue;

                    var scale = Math.Max(within, nearest);
                    if (scale > 0) total += (nearest - within) / scale;
                }
            }
            return total / n;
        }
    }

    /// <summary>Xie-Beni: within-cluster spread per instance measured against the
    /// tightest pair of centroids. Negated, so larger is better.</summary>
    internal class XieBeniIndex<T> : ClusterValidityIndexBase<T>
    {
        public XieBeniIndex(IDistanceMetric<T> distanceMetric, CentroidFunction<T> centroidFunc)
            : base(distanceMetric, centroidFunc) { }

        public override double EvaluateMetric(ClusterCollection<T> clusterCollection)
        {
            var centroids = Centroids(clusterCollection);
            var n = InstanceCount(clusterCollection);
            var separation = MinCentroidSeparation(centroids);
            var ssw = WithinSumOfSquares(clusterCollection, centroids);
            return -(ssw / (n * separation * separation));
        }
    }

    /// <summary>Within-between ratio: the within-cluster sum of squares against the
    /// between-cluster sum of squares, charged per cluster. Negated, so larger is
    /// better.</summary>
    internal class WithinBetweenRatio<T> : ClusterValidityIndexBase<T>
    {
        public WithinBetweenRatio(IDistanceMetric<T> distanceMetric, CentroidFunction<T> centroidFunc)
            : base(distanceMetric, centroidFunc) { }

        public override double EvaluateMetric(ClusterCollection<T> clusterCollection)
        {
            var centroids = Centroids(clusterCollection);
            var ssw = WithinSumOfSquares(clusterCollection, centroids);
            var ssb = TotalSumOfSquares(clusterCollection) - ssw;
            return -(clusterCollection.Count * ssw / ssb);
        }
    }

    /// <summary>Davies-Bouldin: each cluster's worst pairing - the two scatters over the
    /// distance between their centroids - averaged over the clusters, negated so larger
    /// is better.
    ///
    /// A cluster holding a single instance has no dispersion to measure, and the index is
    /// reported as undefined (NaN) for any level that contains one rather than reading
    /// that missing dispersion as a true zero.</summary>
    internal class DaviesBouldinIndex<T> : ClusterValidityIndexBase<T>
    {
        public DaviesBouldinIndex(IDistanceMetric<T> distanceMetric, CentroidFunction<T> centroidFunc)
            : base(distanceMetric, centroidFunc) { }

        public override double EvaluateMetric(ClusterCollection<T> clusterCollection)
        {
            var k = clusterCollection.Count;
            if (k < 2) return double.NaN;

            var centroids = Centroids(clusterCollection);
            var scatter = new double[k];
            for (var i = 0; i < k; i++)
            {
                if (clusterCollection[i].Count < 2) return double.NaN;
                scatter[i] = Scatter(clusterCollection[i], centroids[i]);
            }

            var total = 0d;
            for (var i = 0; i < k; i++)
            {
                var worst = double.MinValue;
                for (var j = 0; j < k; j++)
                {
                    if (j == i) continue;
                    var ratio = (scatter[i] + scatter[j]) / Distance(centroids[i], centroids[j]);
                    if (ratio > worst) worst = ratio;
                }
                total += worst;
            }
            return -(total / k);
        }
    }

    /// <summary>I-index: how much tighter the partition is than the undivided data set,
    /// spread over the clusters and stretched by the widest centroid separation.
    /// Infinite once every instance sits on its own centroid.</summary>
    internal class IIndex<T> : ClusterValidityIndexBase<T>
    {
        public IIndex(IDistanceMetric<T> distanceMetric, CentroidFunction<T> centroidFunc)
            : base(distanceMetric, centroidFunc) { }

        public override double EvaluateMetric(ClusterCollection<T> clusterCollection)
        {
            var centroids = Centroids(clusterCollection);
            var global = GlobalCentroid(clusterCollection);

            var whole = 0d;
            var parts = 0d;
            for (var i = 0; i < clusterCollection.Count; i++)
                foreach (var item in clusterCollection[i])
                {
                    whole += Distance(item, global);
                    parts += Distance(item, centroids[i]);
                }

            return whole / parts * MaxCentroidSeparation(centroids) / clusterCollection.Count;
        }
    }

    /// <summary>Xu index: the log of the per-instance scatter charged against the cluster
    /// count. Negated, so larger is better; infinite once the scatter vanishes.</summary>
    internal class XuIndex<T> : ClusterValidityIndexBase<T>
    {
        public XuIndex(IDistanceMetric<T> distanceMetric, CentroidFunction<T> centroidFunc)
            : base(distanceMetric, centroidFunc) { }

        public override double EvaluateMetric(ClusterCollection<T> clusterCollection)
        {
            var n = InstanceCount(clusterCollection);
            var ssw = WithinSumOfSquares(clusterCollection, Centroids(clusterCollection));
            return -(Math.Log(Math.Sqrt(ssw) / n, 2) + Math.Log(clusterCollection.Count));
        }
    }

    /// <summary>Modified Hubert statistic: the mean over all instance pairs of the
    /// instance distance times the distance between the centroids of the clusters they
    /// landed in, scaled by the tightest centroid pair. Pairs inside one cluster
    /// contribute nothing, so the statistic measures how well the partition's centroid
    /// geometry tracks the raw distances.</summary>
    internal class ModifiedGammaStatistic<T> : ClusterValidityIndexBase<T>
    {
        public ModifiedGammaStatistic(IDistanceMetric<T> distanceMetric, CentroidFunction<T> centroidFunc)
            : base(distanceMetric, centroidFunc) { }

        public override double EvaluateMetric(ClusterCollection<T> clusterCollection)
        {
            var centroids = Centroids(clusterCollection);
            var n = InstanceCount(clusterCollection);
            if (n < 2) return 0;

            var items = new List<T>(n);
            var owner = new List<int>(n);
            for (var i = 0; i < clusterCollection.Count; i++)
                foreach (var item in clusterCollection[i])
                {
                    items.Add(item);
                    owner.Add(i);
                }

            var sum = 0d;
            for (var i = 0; i < items.Count; i++)
                for (var j = i + 1; j < items.Count; j++)
                {
                    if (owner[i] == owner[j]) continue;
                    sum += Distance(items[i], items[j]) * Distance(centroids[owner[i]], centroids[owner[j]]);
                }

            var pairs = n * (n - 1) / 2.0;
            return sum / pairs * MinCentroidSeparation(centroids);
        }
    }
}
