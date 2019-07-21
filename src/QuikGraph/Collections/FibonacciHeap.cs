﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
#if SUPPORTS_CONTRACTS
using System.Diagnostics.Contracts;
#endif
using JetBrains.Annotations;

namespace QuikGraph.Collections
{
    /// <summary>
    /// Fibonacci heap.
    /// </summary>
    /// <typeparam name="TValue">Value type.</typeparam>
    /// <typeparam name="TPriority">Priority metric type.</typeparam>
    [DebuggerDisplay("Count = {" + nameof(Count) + "}")]
    public sealed class FibonacciHeap<TPriority, TValue> : IEnumerable<KeyValuePair<TPriority, TValue>>
    {
        [NotNull]
        private readonly FibonacciHeapLinkedList<TPriority, TValue> _cells;

        // Used to control the direction of the heap, set to 1 if the Heap is increasing,
        // -1 if it's decreasing. We use the approach to avoid unnecessary branches.
        private readonly short _directionMultiplier;

        /// <summary>
        /// Initializes a new instance of the <see cref="FibonacciHeap{TPriority,TValue}"/> class.
        /// </summary>
        public FibonacciHeap()
            : this(HeapDirection.Increasing, Comparer<TPriority>.Default.Compare)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="FibonacciHeap{TPriority,TValue}"/> class.
        /// </summary>
        /// <param name="direction">Heap direction.</param>
        public FibonacciHeap(HeapDirection direction)
            : this(direction, Comparer<TPriority>.Default.Compare)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="FibonacciHeap{TPriority,TValue}"/> class.
        /// </summary>
        /// <param name="direction">Heap direction.</param>
        /// <param name="priorityComparison">Priority comparer.</param>
        public FibonacciHeap(HeapDirection direction, [NotNull] Comparison<TPriority> priorityComparison)
        {
#if SUPPORTS_CONTRACTS
            Contract.Requires(priorityComparison != null);
#endif

            _cells = new FibonacciHeapLinkedList<TPriority, TValue>();
            _degreeToCell = new Dictionary<int, FibonacciHeapCell<TPriority, TValue>>();
            _directionMultiplier = (short)(direction == HeapDirection.Increasing ? 1 : -1);
            Direction = direction;
            PriorityComparison = priorityComparison;
            Count = 0;
        }

        private readonly Dictionary<int, FibonacciHeapCell<TPriority, TValue>> _degreeToCell;

        /// <summary>
        /// Priority comparer.
        /// </summary>
        [NotNull]
        public Comparison<TPriority> PriorityComparison { get; }

        /// <summary>
        /// Heap direction.
        /// </summary>
#if SUPPORTS_CONTRACTS
        [System.Diagnostics.Contracts.Pure]
#endif
        public HeapDirection Direction { get; }

        /// <summary>
        /// Checks if the heap is empty.
        /// </summary>
#if SUPPORTS_CONTRACTS
        [System.Diagnostics.Contracts.Pure]
#endif
        public bool IsEmpty => _cells.First is null;

        /// <summary>
        /// Number of element.
        /// </summary>
#if SUPPORTS_CONTRACTS
        [System.Diagnostics.Contracts.Pure]
#endif
        public int Count { get; private set; }

        /// <summary>
        /// Top element of the heap.
        /// </summary>
#if SUPPORTS_CONTRACTS
        [System.Diagnostics.Contracts.Pure]
#endif
        public FibonacciHeapCell<TPriority, TValue> Top { get; private set; }

        /// <summary>
        /// Enqueues an element in the queue.
        /// </summary>
        /// <param name="priority">Value priority.</param>
        /// <param name="value">Value to add.</param>
        public FibonacciHeapCell<TPriority, TValue> Enqueue(TPriority priority, TValue value)
        {
            var newCell = new FibonacciHeapCell<TPriority, TValue>
            {
                Priority = priority,
                Value = value,
                Marked = false,
                Children = new FibonacciHeapLinkedList<TPriority, TValue>(),
                Degree = 1,
                Next = null,
                Previous = null,
                Parent = null,
                Removed = false
            };

            // We don't do any book keeping or maintenance of the heap on Enqueue,
            // We just add this cell to the end of the list of Heaps, updating the Next if required
            _cells.AddLast(newCell);
            if (Top is null || PriorityComparison(newCell.Priority, Top.Priority) * _directionMultiplier < 0)
            {
                Top = newCell;
            }

            Count++;

            return newCell;
        }

        /// <summary>
        /// Deletes the given <paramref name="cell"/> from this heap.
        /// </summary>
        /// <param name="cell">Cell to delete.</param>
        public void Delete([NotNull] FibonacciHeapCell<TPriority, TValue> cell)
        {
#if SUPPORTS_CONTRACTS
            Contract.Requires(cell != null);
#endif

            ChangeKeyInternal(cell, default(TPriority), true);
            Dequeue();
        }

        /// <summary>
        /// Changes the priority of the given <paramref name="cell"/>.
        /// </summary>
        /// <param name="cell">Cell to update the priority.</param>
        /// <param name="newPriority">New priority.</param>
        public void ChangeKey([NotNull] FibonacciHeapCell<TPriority, TValue> cell, [NotNull] TPriority newPriority)
        {
#if SUPPORTS_CONTRACTS
            Contract.Requires(cell != null);
#endif

            ChangeKeyInternal(cell, newPriority, false);
        }

        private void ChangeKeyInternal(
            [NotNull] FibonacciHeapCell<TPriority, TValue> cell,
            [CanBeNull] TPriority newKey, // Null authorized if deleting the cell
            bool deletingCell)
        {
#if SUPPORTS_CONTRACTS
            Contract.Requires(cell != null);
#endif

            int delta = Math.Sign(PriorityComparison(cell.Priority, newKey));
            if (delta == 0)
                return;

            if (delta == _directionMultiplier || deletingCell)
            {
                // New value is in the same direction as the heap
                UpdateCellSameDirection(cell, newKey, deletingCell);
            }
            else
            {
#if SUPPORTS_CONTRACTS
                // Not removing a cell so the newKey must not be null
                Contract.Assert(newKey != null);
#endif

                // New value is in opposite direction of Heap, cut all children violating heap condition
                UpdateCellOppositeDirection(cell, newKey);
            }
        }

        private void UpdateCellSameDirection(
            [NotNull] FibonacciHeapCell<TPriority, TValue> cell, 
            [CanBeNull] TPriority newKey, 
            bool deletingCell)
        {
            cell.Priority = newKey;
            FibonacciHeapCell<TPriority, TValue> parentCell = cell.Parent;
            if (parentCell != null
                && (PriorityComparison(newKey, parentCell.Priority) * _directionMultiplier < 0 || deletingCell))
            {
                cell.Marked = false;

                // ReSharper disable once PossibleNullReferenceException
                // Justification: parentCell must have children because child has parentCell as Parent.
                parentCell.Children.Remove(cell);
                UpdateCellsDegree(parentCell);
                cell.Parent = null;
                _cells.AddLast(cell);

                // This loop is the cascading cut, we continue to cut
                // ancestors of the cell reduced until we hit a root 
                // or we found an unmarked ancestor
                while (parentCell.Marked && parentCell.Parent != null)
                {
                    // ReSharper disable once PossibleNullReferenceException
                    // Justification: parentCell must have children because child has parentCell as Parent.
                    parentCell.Parent.Children.Remove(parentCell);
                    UpdateCellsDegree(parentCell);
                    parentCell.Marked = false;
                    _cells.AddLast(parentCell);

                    FibonacciHeapCell<TPriority, TValue> currentParent = parentCell;
                    parentCell = parentCell.Parent;
                    currentParent.Parent = null;
                }

                if (parentCell.Parent != null)
                {
                    // We mark this cell to note that it had a child
                    // cut from it before
                    parentCell.Marked = true;
                }
            }

            // Update next
            if (deletingCell || PriorityComparison(newKey, Top.Priority) * _directionMultiplier < 0)
            {
                Top = cell;
            }
        }

        private void UpdateCellOppositeDirection([NotNull] FibonacciHeapCell<TPriority, TValue> cell, [NotNull] TPriority newKey)
        {
            cell.Priority = newKey;
            if (cell.Children != null)
            {
                List<FibonacciHeapCell<TPriority, TValue>> toUpdate = null;
                foreach (FibonacciHeapCell<TPriority, TValue> child in cell.Children)
                {
                    if (PriorityComparison(cell.Priority, child.Priority) * _directionMultiplier > 0)
                    {
                        if (toUpdate is null)
                            toUpdate = new List<FibonacciHeapCell<TPriority, TValue>>();
                        toUpdate.Add(child);
                    }
                }

                if (toUpdate != null)
                {
                    foreach (FibonacciHeapCell<TPriority, TValue> child in toUpdate)
                    {
                        cell.Marked = true;
                        cell.Children.Remove(child);
                        child.Parent = null;
                        child.Marked = false;
                        _cells.AddLast(child);

                        UpdateCellsDegree(cell);
                    }
                }
            }

            UpdateNext();
        }

        /// <summary>
        /// Updates the degree of a cell, cascading to update the degree of the
        /// parents if necessary.
        /// </summary>
        /// <param name="cell">Cell to update.</param>
        private void UpdateCellsDegree([NotNull] FibonacciHeapCell<TPriority, TValue> cell)
        {
#if SUPPORTS_CONTRACTS
            Contract.Requires(cell != null);
            Contract.Requires(cell.Children != null);
#endif

            int oldDegree = cell.Degree;
            // ReSharper disable once PossibleNullReferenceException, Justification: Checked by contract.
            cell.Degree = cell.Children.First is null
                ? 1
                : cell.Children.Max(x => x.Degree) + 1;

            if (oldDegree != cell.Degree)
            {
                if (_degreeToCell.TryGetValue(oldDegree, out FibonacciHeapCell<TPriority, TValue> degreeMapValue)
                    && degreeMapValue == cell)
                {
                    _degreeToCell.Remove(oldDegree);
                }
                else if (cell.Parent != null)
                {
                    UpdateCellsDegree(cell.Parent);
                }
            }
        }

        /// <summary>
        /// Dequeues an element from the queue.
        /// </summary>
        /// <returns>Removed element.</returns>
        public KeyValuePair<TPriority, TValue> Dequeue()
        {
            if (Count == 0)
                throw new InvalidOperationException("Heap is empty.");

            var result = new KeyValuePair<TPriority, TValue>(Top.Priority, Top.Value);

            _cells.Remove(Top);
            Top.Next = null;
            Top.Parent = null;
            Top.Previous = null;
            Top.Removed = true;

            if (_degreeToCell.TryGetValue(Top.Degree, out FibonacciHeapCell<TPriority, TValue> currentDegreeCell)
                && currentDegreeCell == Top)
            {
                _degreeToCell.Remove(Top.Degree);
            }

#if SUPPORTS_CONTRACTS
            Contract.Assert(Top.Children != null);
#else
            if (Top.Children is null)
                throw new InvalidOperationException("Heap top item has a null children collection.");
#endif

            foreach (FibonacciHeapCell<TPriority, TValue> child in Top.Children)
            {
                child.Parent = null;
            }

            _cells.MergeLists(Top.Children);
            Top.Children = null;

            Count--;
            UpdateNext();

            return result;
        }

        /// <summary>
        /// Updates the Next pointer, maintaining the heap
        /// by folding duplicate heap degrees into each other.
        /// Takes O(log(N)) time amortized.
        /// </summary>
        private void UpdateNext()
        {
            CompressHeap();
            FibonacciHeapCell<TPriority, TValue> cell = _cells.First;
            Top = cell;
            while (cell != null)
            {
                if (PriorityComparison(cell.Priority, Top.Priority) * _directionMultiplier < 0)
                {
                    Top = cell;
                }

                cell = cell.Next;
            }
        }

        private void CompressHeap()
        {
            FibonacciHeapCell<TPriority, TValue> cell = _cells.First;
            while (cell != null)
            {
                var nextCell = ReduceCell(ref cell);

                _degreeToCell[cell.Degree] = cell;
                cell = nextCell;
            }
        }

        [CanBeNull]
        private FibonacciHeapCell<TPriority, TValue> ReduceCell([NotNull] ref FibonacciHeapCell<TPriority, TValue> cell)
        {
            FibonacciHeapCell<TPriority, TValue> nextCell = cell.Next;
            while (_degreeToCell.TryGetValue(cell.Degree, out FibonacciHeapCell<TPriority, TValue> currentDegreeCell)
                   && currentDegreeCell != cell)
            {
                _degreeToCell.Remove(cell.Degree);
                if (PriorityComparison(currentDegreeCell.Priority, cell.Priority) * _directionMultiplier <= 0)
                {
                    if (cell == nextCell)
                    {
                        nextCell = cell.Next;
                    }

                    ReduceCells(currentDegreeCell, cell);
                    cell = currentDegreeCell;
                }
                else
                {
                    if (currentDegreeCell == nextCell)
                    {
                        nextCell = currentDegreeCell.Next;
                    }

                    ReduceCells(cell, currentDegreeCell);
                }
            }

            return nextCell;
        }

        /// <summary>
        /// Given two cells, adds the child cell as a child of the parent cell.
        /// </summary>
        /// <param name="parentCell">Parent cell.</param>
        /// <param name="childCell">Child cell.</param>
        private void ReduceCells(
            [NotNull] FibonacciHeapCell<TPriority, TValue> parentCell,
            [NotNull] FibonacciHeapCell<TPriority, TValue> childCell)
        {
#if SUPPORTS_CONTRACTS
            Contract.Requires(parentCell != null);
            Contract.Requires(parentCell.Children != null);
            Contract.Requires(childCell != null);
#endif

            _cells.Remove(childCell);
            // ReSharper disable once PossibleNullReferenceException, Justification: Checked by contract.
            parentCell.Children.AddLast(childCell);
            childCell.Parent = parentCell;
            childCell.Marked = false;

            if (parentCell.Degree == childCell.Degree)
            {
                parentCell.Degree += 1;
            }
        }

        /// <summary>
        /// Merges the given <paramref name="heap"/> into this heap.
        /// </summary>
        /// <param name="heap">Heap to merge.</param>
        /// <exception cref="Exception">If the heap is not in the same direction.</exception>
        public void Merge([NotNull] FibonacciHeap<TPriority, TValue> heap)
        {
#if SUPPORTS_CONTRACTS
            Contract.Requires(heap != null);
#endif

            if (heap.Direction != Direction)
                throw new InvalidOperationException("Error: Heaps must go in the same direction when merging.");

            _cells.MergeLists(heap._cells);
            if (PriorityComparison(heap.Top.Priority, Top.Priority) * _directionMultiplier < 0)
            {
                Top = heap.Top;
            }

            Count += heap.Count;
        }

#region IEnumerable

        /// <inheritdoc />
        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

#endregion

#region IEnumerable<KeyValuePair<TKey,TValue>>

        /// <inheritdoc />
        public IEnumerator<KeyValuePair<TPriority, TValue>> GetEnumerator()
        {
            var tempHeap = new FibonacciHeap<TPriority, TValue>(Direction, PriorityComparison);
            var cellsStack = new Stack<FibonacciHeapCell<TPriority, TValue>>();
            _cells.ForEach(x => cellsStack.Push(x));
            while (cellsStack.Count > 0)
            {
                FibonacciHeapCell<TPriority, TValue> topCell = cellsStack.Peek();
                tempHeap.Enqueue(topCell.Priority, topCell.Value);
                cellsStack.Pop();
                topCell.Children?.ForEach(x => cellsStack.Push(x));
            }

            while (!tempHeap.IsEmpty)
            {
                yield return tempHeap.Top.ToKeyValuePair();
                tempHeap.Dequeue();
            }
        }

#endregion

        /// <summary>
        /// Enumerator for this heap that <see cref="Dequeue"/> elements in the same time.
        /// </summary>
        /// <returns>Heap elements.</returns>
        public IEnumerable<KeyValuePair<TPriority, TValue>> GetDestructiveEnumerator()
        {
            while (!IsEmpty)
            {
                yield return Top.ToKeyValuePair();
                Dequeue();
            }
        }

#region String representation

        private struct CellLevel
        {
            [NotNull]
            public FibonacciHeapCell<TPriority, TValue> Cell { get; }

            public int Level { get; }

            public CellLevel([NotNull] FibonacciHeapCell<TPriority, TValue> cell, int level)
            {
                Cell = cell;
                Level = level;
            }
        }

        /// <summary>
        /// Draws the current heap in a string. Marked cells have an * next to them.
        /// </summary>
        /// <returns>Heap string representation.</returns>
        public string DrawHeap()
        {
            var lines = new List<string>();
            int columnPosition = 0;
            var list = new List<CellLevel>();
            foreach (FibonacciHeapCell<TPriority, TValue> cell in _cells)
                list.Add(new CellLevel(cell, 0));
            list.Reverse();

            var stack = new Stack<CellLevel>(list);
            while (stack.Count > 0)
            {
                CellLevel currentCell = stack.Pop();
                int lineNum = currentCell.Level;
                if (lines.Count <= lineNum)
                    lines.Add(string.Empty);

                string currentLine = lines[lineNum];
                currentLine = currentLine.PadRight(columnPosition, ' ');
                string cellString = $"{currentCell.Cell.Priority}{(currentCell.Cell.Marked ? "*" : string.Empty)} ";
                currentLine += cellString;

                if (currentCell.Cell.Children?.First != null)
                {
                    var children = new List<FibonacciHeapCell<TPriority, TValue>>(currentCell.Cell.Children);
                    children.Reverse();
                    children.ForEach(x => stack.Push(new CellLevel(x, currentCell.Level + 1)));
                }
                else
                {
                    columnPosition += cellString.Length;
                }

                lines[lineNum] = currentLine;
            }

            return string.Join(Environment.NewLine, lines.ToArray());
        }

#endregion
    }
}