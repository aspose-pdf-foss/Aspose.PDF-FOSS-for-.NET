using System;
using System.Collections;
using System.Collections.Generic;

namespace Aspose.Pdf.Utils.Clustering.HierarchicalAgglomerativeClustering
{
    /// <summary>How far apart two clusters are, for a given distance metric — the rule
    /// an agglomerative run uses to pick the next pair to merge.</summary>
    internal partial interface ILinkageCriterion<T>
    {
        IDistanceMetric<T> DistanceMetric { get; }

        double Calculate(Cluster<T> cluster1, Cluster<T> cluster2);
    }

    /// <summary>Scores a whole clustering (one level of a run), so a caller can pick the
    /// level a validity index likes best.</summary>
    internal partial interface IMetricsCriterion<T>
    {
        IDistanceMetric<T> DistanceMetric { get; }

        double EvaluateMetric(ClusterCollection<T> clusterCollection);
    }

    /// <summary>One clustering with the score a metric gave it.</summary>
    internal partial struct ClusterSetEvaluation<T>
    {
        public ClusterSetEvaluation(ClusterCollection<T> clusterCollection, double evaluationValue)
        {
            ClusterCollection = clusterCollection;
            EvaluationValue = evaluationValue;
        }

        public ClusterCollection<T> ClusterCollection { get; }

        public double EvaluationValue { get; }

        public override string ToString() =>
            string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "{0} clusters, evaluation {1}",
                ClusterCollection?.Count ?? 0, EvaluationValue);
    }

    /// <summary>Every level an agglomerative run passed through: index 0 is the
    /// singletons, each later level has one cluster fewer, and the last is everything in
    /// one cluster. A run over N instances therefore yields N levels.</summary>
    internal partial class ClusteringResult<T> : IEnumerable<ClusterCollection<T>>
    {
        private readonly List<ClusterCollection<T>> _levels;

        public ClusteringResult() => _levels = new List<ClusterCollection<T>>();

        public ClusteringResult(int size) => _levels = new List<ClusterCollection<T>>(Math.Max(0, size));

        public int Count => _levels.Count;

        public ClusterCollection<T> this[int index]
        {
            get => _levels[index];
            set
            {
                while (_levels.Count <= index) _levels.Add(new ClusterCollection<T>(Array.Empty<Cluster<T>>()));
                _levels[index] = value;
            }
        }

        /// <summary>The one cluster the run ended with (the last level's only cluster);
        /// <see cref="Cluster{T}.Empty"/> when nothing was clustered.</summary>
        public Cluster<T> SingleCluster
        {
            get
            {
                if (_levels.Count == 0) return Cluster<T>.Empty;
                var last = _levels[_levels.Count - 1];
                return last is not null && last.Count > 0 ? last[0] : Cluster<T>.Empty;
            }
        }

        /// <summary>Score every level of the run with the given validity criterion.</summary>
        public IList<ClusterSetEvaluation<T>> EvaluateClustering(IMetricsCriterion<T> criterion) =>
            EvaluateClustering(criterion, uint.MaxValue);

        /// <summary>Score the levels of the run that hold at most <paramref name="maxClusters"/>
        /// clusters, from the fewest clusters upwards. The level where everything ended up
        /// in one cluster has no partition to judge and is left out, so a run over N
        /// instances yields N-1 evaluations.</summary>
        public IList<ClusterSetEvaluation<T>> EvaluateClustering(IMetricsCriterion<T> criterion, uint maxClusters)
        {
            if (criterion is null) throw new ArgumentNullException(nameof(criterion));

            var evaluations = new List<ClusterSetEvaluation<T>>();
            for (var i = _levels.Count - 1; i >= 0; i--)
            {
                var level = _levels[i];
                if (level is null || level.Count < 2 || level.Count > maxClusters) continue;
                evaluations.Add(new ClusterSetEvaluation<T>(level, criterion.EvaluateMetric(level)));
            }
            return evaluations;
        }

        internal void AddLevel(ClusterCollection<T> level) => _levels.Add(level);

        public IEnumerator<ClusterCollection<T>> GetEnumerator() => _levels.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    /// <summary>Hierarchical agglomerative clustering: start with one cluster per
    /// instance and repeatedly merge the closest pair under the linkage criterion,
    /// recording every level. Stops early when the closest pair is farther apart than a
    /// caller-supplied cut-off.</summary>
    internal partial class AgglomerativeClusteringAlgorithm<T>
    {
        public AgglomerativeClusteringAlgorithm(ILinkageCriterion<T> linkageCriterion)
        {
            LinkageCriterion = linkageCriterion ?? throw new ArgumentNullException(nameof(linkageCriterion));
        }

        public ILinkageCriterion<T> LinkageCriterion { get; }

        public ClusteringResult<T> Execute(ICollection<T> instances) => Execute(instances, double.MaxValue);

        public ClusteringResult<T> Execute(ICollection<T> instances, double distance)
        {
            var result = new ClusteringResult<T>(instances?.Count ?? 0);
            if (instances is null || instances.Count == 0) return result;

            // level 0: every instance alone
            var current = new List<Cluster<T>>(instances.Count);
            foreach (var instance in instances) current.Add(new Cluster<T>(instance));
            result.AddLevel(new ClusterCollection<T>(current.ToArray()));

            while (current.Count > 1)
            {
                var best = double.MaxValue;
                int bestI = -1, bestJ = -1;
                for (var i = 0; i < current.Count; i++)
                    for (var j = i + 1; j < current.Count; j++)
                    {
                        var d = LinkageCriterion.Calculate(current[i], current[j]);
                        if (d < best)
                        {
                            best = d; bestI = i; bestJ = j;
                        }
                    }

                if (bestI < 0 || best > distance) break;

                var merged = new Cluster<T>(current[bestI], current[bestJ], best);
                var next = new List<Cluster<T>>(current.Count - 1);
                for (var k = 0; k < current.Count; k++)
                    if (k != bestI && k != bestJ) next.Add(current[k]);
                next.Insert(Math.Min(bestI, next.Count), merged);

                current = next;
                result.AddLevel(new ClusterCollection<T>(current.ToArray(), best));
            }

            return result;
        }
    }

    /// <summary>Shared plumbing for the pairwise linkage criteria.</summary>
    internal abstract class PairwiseLinkageBase<T> : ILinkageCriterion<T>
    {
        protected PairwiseLinkageBase(IDistanceMetric<T> metric) =>
            DistanceMetric = metric ?? throw new ArgumentNullException(nameof(metric));

        public IDistanceMetric<T> DistanceMetric { get; }

        public abstract double Calculate(Cluster<T> cluster1, Cluster<T> cluster2);

        protected IEnumerable<double> Pairs(Cluster<T> a, Cluster<T> b)
        {
            foreach (var x in a)
                foreach (var y in b)
                    yield return DistanceMetric.CalculateDistance(x, y);
        }
    }

    /// <summary>Nearest members decide (min pairwise distance).</summary>
    internal partial class SingleLinkage<T> : PairwiseLinkageBase<T>
    {
        public SingleLinkage(IDistanceMetric<T> metric) : base(metric) { }

        public override double Calculate(Cluster<T> cluster1, Cluster<T> cluster2)
        {
            var min = double.MaxValue;
            foreach (var d in Pairs(cluster1, cluster2)) if (d < min) min = d;
            return min;
        }
    }

    /// <summary>Farthest members decide (max pairwise distance).</summary>
    internal partial class CompleteLinkage<T> : PairwiseLinkageBase<T>
    {
        public CompleteLinkage(IDistanceMetric<T> metric) : base(metric) { }

        public override double Calculate(Cluster<T> cluster1, Cluster<T> cluster2)
        {
            var max = double.MinValue;
            foreach (var d in Pairs(cluster1, cluster2)) if (d > max) max = d;
            return max;
        }
    }

    /// <summary>Mean of every cross-cluster pair (UPGMA).</summary>
    internal partial class AverageLinkage<T> : PairwiseLinkageBase<T>
    {
        public AverageLinkage(IDistanceMetric<T> metric) : base(metric) { }

        public override double Calculate(Cluster<T> cluster1, Cluster<T> cluster2)
        {
            double sum = 0; var n = 0;
            foreach (var d in Pairs(cluster1, cluster2)) { sum += d; n++; }
            return n == 0 ? 0 : sum / n;
        }
    }

    /// <summary>Distance between the two clusters' centroids.</summary>
    internal partial class CentroidLinkage<T> : ILinkageCriterion<T>
    {
        private readonly Func<Cluster<T>, T> _centroid;

        public CentroidLinkage(IDistanceMetric<T> metric, Func<Cluster<T>, T> centroidFunc)
        {
            DistanceMetric = metric ?? throw new ArgumentNullException(nameof(metric));
            _centroid = centroidFunc ?? throw new ArgumentNullException(nameof(centroidFunc));
        }

        public IDistanceMetric<T> DistanceMetric { get; }

        public double Calculate(Cluster<T> cluster1, Cluster<T> cluster2) =>
            DistanceMetric.CalculateDistance(_centroid(cluster1), _centroid(cluster2));
    }

    /// <summary>Ward's criterion: the variance the merge would add —
    /// (|a|·|b| / (|a|+|b|)) · d(centroid a, centroid b)².</summary>
    internal partial class WardsMinimumVarianceLinkage<T> : ILinkageCriterion<T>
    {
        private readonly CentroidFunction<T> _centroid;

        public WardsMinimumVarianceLinkage(IDistanceMetric<T> metric, CentroidFunction<T> centroidFunc)
        {
            DistanceMetric = metric ?? throw new ArgumentNullException(nameof(metric));
            _centroid = centroidFunc ?? throw new ArgumentNullException(nameof(centroidFunc));
        }

        public IDistanceMetric<T> DistanceMetric { get; }

        public double Calculate(Cluster<T> cluster1, Cluster<T> cluster2)
        {
            double n1 = cluster1.Count, n2 = cluster2.Count;
            if (n1 == 0 || n2 == 0) return 0;
            var d = DistanceMetric.CalculateDistance(_centroid(cluster1), _centroid(cluster2));
            return n1 * n2 / (n1 + n2) * d * d;
        }
    }

    /// <summary>Energy distance: 2·mean(cross) − mean(within a) − mean(within b),
    /// weighted by the merge's size product.
    ///
    /// The weight is what separates this from the textbook Székely–Rizzo e-distance
    /// (which normalises harmonically, n1·n2/(n1+n2)). Calibrated against the
    /// expected merge order: at the level where a lone point sits 1.41 from a
    /// 2-member cluster and two lone points sit 1.95 apart, the expected order merges the
    /// FAR PAIR first — only a weight that grows with cluster size does that. The
    /// harmonic form picks the near point instead.</summary>
    internal partial class MinimumEnergyLinkage<T> : PairwiseLinkageBase<T>
    {
        public MinimumEnergyLinkage(IDistanceMetric<T> metric) : base(metric) { }

        public override double Calculate(Cluster<T> cluster1, Cluster<T> cluster2)
        {
            double n1 = cluster1.Count, n2 = cluster2.Count;
            if (n1 == 0 || n2 == 0) return 0;

            double cross = 0;
            foreach (var d in Pairs(cluster1, cluster2)) cross += d;
            cross /= n1 * n2;

            return n1 * n2 * (2 * cross - Within(cluster1) - Within(cluster2));
        }

        private double Within(Cluster<T> cluster)
        {
            double sum = 0; var n = 0;
            foreach (var x in cluster)
                foreach (var y in cluster)
                {
                    sum += DistanceMetric.CalculateDistance(x, y); n++;
                }
            return n == 0 ? 0 : sum / n;
        }
    }
}
