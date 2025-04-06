using System;
using System.Collections.Generic;

namespace Lidarr.Plugin.Tidal.Services.CircuitBreaker
{
    /// <summary>
    /// A fixed-size buffer that automatically overwrites the oldest items when full.
    /// This provides better performance than a Queue for the circuit breaker's failure tracking.
    /// </summary>
    /// <typeparam name="T">The type of items stored in the buffer.</typeparam>
    internal class CircularBuffer<T>
    {
        private readonly T[] _buffer;
        private int _start;
        private int _count;

        /// <summary>
        /// Initializes a new instance of the CircularBuffer class.
        /// </summary>
        /// <param name="capacity">The maximum number of items the buffer can hold.</param>
        public CircularBuffer(int capacity)
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity), "Capacity must be greater than zero.");
            }

            _buffer = new T[capacity];
            _start = 0;
            _count = 0;
        }

        /// <summary>
        /// Gets the number of items currently in the buffer.
        /// </summary>
        public int Count => _count;

        /// <summary>
        /// Gets the maximum number of items the buffer can hold.
        /// </summary>
        public int Capacity => _buffer.Length;

        /// <summary>
        /// Adds an item to the buffer, overwriting the oldest item if the buffer is full.
        /// </summary>
        /// <param name="item">The item to add to the buffer.</param>
        public void Enqueue(T item)
        {
            if (_count == _buffer.Length)
            {
                // Buffer is full, overwrite the oldest item
                _buffer[_start] = item;
                _start = (_start + 1) % _buffer.Length;
            }
            else
            {
                // Buffer has space, add the item
                _buffer[(_start + _count) % _buffer.Length] = item;
                _count++;
            }
        }

        /// <summary>
        /// Returns the oldest item in the buffer without removing it.
        /// </summary>
        /// <returns>The oldest item in the buffer.</returns>
        /// <exception cref="InvalidOperationException">Thrown if the buffer is empty.</exception>
        public T Peek()
        {
            if (_count == 0)
            {
                throw new InvalidOperationException("Buffer is empty");
            }

            return _buffer[_start];
        }

        /// <summary>
        /// Removes and returns the oldest item in the buffer.
        /// </summary>
        /// <returns>The oldest item in the buffer.</returns>
        /// <exception cref="InvalidOperationException">Thrown if the buffer is empty.</exception>
        public T Dequeue()
        {
            if (_count == 0)
            {
                throw new InvalidOperationException("Buffer is empty");
            }

            T item = _buffer[_start];
            _start = (_start + 1) % _buffer.Length;
            _count--;
            return item;
        }

        /// <summary>
        /// Removes all items from the buffer.
        /// </summary>
        public void Clear()
        {
            _start = 0;
            _count = 0;
        }

        /// <summary>
        /// Returns an enumerable collection of all items in the buffer.
        /// </summary>
        /// <returns>An enumerable collection of all items in the buffer.</returns>
        public IEnumerable<T> GetItems()
        {
            for (int i = 0; i < _count; i++)
            {
                yield return _buffer[(_start + i) % _buffer.Length];
            }
        }
    }
}
