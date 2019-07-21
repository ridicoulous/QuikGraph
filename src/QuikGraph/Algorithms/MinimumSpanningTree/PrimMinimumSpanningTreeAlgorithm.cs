﻿using System;
using System.Collections.Generic;
using System.Linq;
#if SUPPORTS_CONTRACTS
using System.Diagnostics.Contracts;
#endif
using JetBrains.Annotations;
using QuikGraph.Algorithms.Services;
using QuikGraph.Collections;

namespace QuikGraph.Algorithms.MinimumSpanningTree
{
    /// <summary>
    /// Prim minimum spanning tree algorithm implementation.
    /// </summary>
    /// <typeparam name="TVertex">Vertex type.</typeparam>
    /// <typeparam name="TEdge">Edge type.</typeparam>
#if SUPPORTS_SERIALIZATION
    [Serializable]
#endif
    public sealed class PrimMinimumSpanningTreeAlgorithm<TVertex, TEdge>
        : AlgorithmBase<IUndirectedGraph<TVertex, TEdge>>
            , IMinimumSpanningTreeAlgorithm<TVertex, TEdge>
        where TEdge : IEdge<TVertex>
    {
        [NotNull]
        private readonly Func<TEdge, double> _edgeWeights;

        /// <summary>
        /// Initializes a new instance of the <see cref="PrimMinimumSpanningTreeAlgorithm{TVertex,TEdge}"/> class.
        /// </summary>
        /// <param name="visitedGraph">Graph to visit.</param>
        /// <param name="edgeWeights">Function that computes the weight for a given edge.</param>
        public PrimMinimumSpanningTreeAlgorithm(
            [NotNull] IUndirectedGraph<TVertex, TEdge> visitedGraph,
            [NotNull] Func<TEdge, double> edgeWeights)
            : this(null, visitedGraph, edgeWeights)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="PrimMinimumSpanningTreeAlgorithm{TVertex,TEdge}"/> class.
        /// </summary>
        /// <param name="host">Host to use if set, otherwise use this reference.</param>
        /// <param name="visitedGraph">Graph to visit.</param>
        /// <param name="edgeWeights">Function that computes the weight for a given edge.</param>
        public PrimMinimumSpanningTreeAlgorithm(
            [CanBeNull] IAlgorithmComponent host,
            [NotNull] IUndirectedGraph<TVertex, TEdge> visitedGraph,
            [NotNull] Func<TEdge, double> edgeWeights)
            : base(host, visitedGraph)
        {
#if SUPPORTS_CONTRACTS
            Contract.Requires(edgeWeights != null);
#endif

            _edgeWeights = edgeWeights;
        }

        /// <summary>
        /// Fired when an edge is going to be analyzed.
        /// </summary>
        public event EdgeAction<TVertex, TEdge> ExamineEdge;

        private void OnExamineEdge([NotNull] TEdge edge)
        {
#if SUPPORTS_CONTRACTS
            Contract.Requires(edge != null);
#endif

            ExamineEdge?.Invoke(edge);
        }

        #region ITreeBuilderAlgorithm<TVertex,TEdge>

        /// <inheritdoc />
        public event EdgeAction<TVertex, TEdge> TreeEdge;

        private void OnTreeEdge([NotNull] TEdge edge)
        {
#if SUPPORTS_CONTRACTS
            Contract.Requires(edge != null);
#endif

            TreeEdge?.Invoke(edge);
        }

        #endregion

        #region AlgorithmBase<TGraph>

        private Dictionary<TVertex, HashSet<TEdge>> _verticesEdges;
        private HashSet<TVertex> _visitedVertices;
        private ForestDisjointSet<TVertex> _sets;

        private HashSet<TEdge> _edges;
        private BinaryQueue<TEdge, double> _queue;

        private void InitializeVerticesToEdges()
        {
            _verticesEdges = new Dictionary<TVertex, HashSet<TEdge>>();
            _visitedVertices = new HashSet<TVertex>();
            _sets = new ForestDisjointSet<TVertex>(VisitedGraph.VertexCount);

            foreach (TVertex vertex in VisitedGraph.Vertices)
            {
                if (_visitedVertices.Count == 0)
                {
                    _visitedVertices.Add(vertex);
                }

                _sets.MakeSet(vertex);
                _verticesEdges.Add(vertex, new HashSet<TEdge>());
            }

            foreach (TEdge edge in VisitedGraph.Edges)
            {
                _verticesEdges[edge.Source].Add(edge);
                _verticesEdges[edge.Target].Add(edge);
            }
        }

        private void InitializeQueue()
        {
            _edges = new HashSet<TEdge>();
            _queue = new BinaryQueue<TEdge, double>(_edgeWeights);
            TVertex lastVertex = _visitedVertices.First();
            foreach (TEdge edge in _verticesEdges[lastVertex])
            {
                if (!_edges.Contains(edge))
                {
                    _edges.Add(edge);
                    _queue.Enqueue(edge);
                }
            }
        }

        /// <inheritdoc />
        protected override void Initialize()
        {
            base.Initialize();

            InitializeVerticesToEdges();

            if (Services.CancelManager.IsCancelling)
                return;

            InitializeQueue();
        }

        /// <inheritdoc />
        protected override void InternalCompute()
        {
            if (Services.CancelManager.IsCancelling)
                return;

            while (_edges.Count > 0 && _visitedVertices.Count < VisitedGraph.VertexCount)
            {
                TEdge minEdge = _queue.Dequeue();
                OnExamineEdge(minEdge);

                if (!_sets.AreInSameSet(minEdge.Source, minEdge.Target))
                {
                    OnTreeEdge(minEdge);
                    _sets.Union(minEdge.Source, minEdge.Target);

                    TVertex lastVertex;
                    if (_visitedVertices.Contains(minEdge.Source))
                    {
                        lastVertex = minEdge.Target;
                        _visitedVertices.Add(minEdge.Target);
                    }
                    else
                    {
                        lastVertex = minEdge.Source;
                        _visitedVertices.Add(minEdge.Source);
                    }

                    EnqueueEdgesFrom(lastVertex);
                }
            }
        }

        private void EnqueueEdgesFrom([NotNull] TVertex vertex)
        {
            foreach (TEdge edge in _verticesEdges[vertex])
            {
                if (_edges.Contains(edge))
                    continue;

                _edges.Add(edge);
                _queue.Enqueue(edge);
            }
        }

        #endregion
    }
}