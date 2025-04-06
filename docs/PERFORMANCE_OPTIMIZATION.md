# Performance Optimization Guide

## Table of Contents
- [Overview](#overview)
- [Memory Management](#memory-management)
- [Download Pipeline Optimization](#download-pipeline-optimization)
- [Concurrency Control](#concurrency-control)
- [Rate Limiting](#rate-limiting)
- [Caching Strategy](#caching-strategy)
- [Monitoring and Profiling](#monitoring-and-profiling)

## Overview

This document outlines performance optimization strategies for the Lidarr Tidal Plugin, focusing on efficient resource utilization, throughput maximization, and stability under load.

## Memory Management

### Buffer Pooling

The plugin implements buffer pooling to reduce memory allocation and garbage collection pressure:

```csharp
public class BufferPool
{
    private readonly ConcurrentBag<byte[]> _buffers = new ConcurrentBag<byte[]>();
    private readonly int _bufferSize;
    private readonly int _maxBuffers;
    private int _currentCount;

    public BufferPool(int bufferSize = 81920, int maxBuffers = 20)
    {
        _bufferSize = bufferSize;
        _maxBuffers = maxBuffers;
    }

    public byte[] Rent()
    {
        if (_buffers.TryTake(out byte[] buffer))
        {
            Interlocked.Decrement(ref _currentCount);
            return buffer;
        }
        
        return new byte[_bufferSize];
    }

    public void Return(byte[] buffer)
    {
        if (buffer == null || buffer.Length != _bufferSize)
            return;
            
        if (Interlocked.Increment(ref _currentCount) <= _maxBuffers)
        {
            _buffers.Add(buffer);
        }
        else
        {
            Interlocked.Decrement(ref _currentCount);
        }
    }
}
```

### Memory Pressure Detection

The plugin monitors memory pressure to adapt behavior under constrained conditions:

```csharp
public class MemoryMonitor
{
    private const long HighPressureThreshold = 85L * 1024L * 1024L * 1024L; // 85% of 2GB
    private const long LowPressureThreshold = 70L * 1024L * 1024L * 1024L;  // 70% of 2GB
    
    public MemoryPressure GetCurrentPressure()
    {
        var memoryInfo = GC.GetGCMemoryInfo();
        var totalAvailable = memoryInfo.TotalAvailableMemoryBytes;
        
        if (totalAvailable < HighPressureThreshold)
            return MemoryPressure.High;
        else if (totalAvailable < LowPressureThreshold)
            return MemoryPressure.Medium;
        else
            return MemoryPressure.Low;
    }
    
    public bool ShouldThrottleOperations()
    {
        return GetCurrentPressure() == MemoryPressure.High;
    }
}

public enum MemoryPressure
{
    Low,
    Medium,
    High
}
```

## Rate Limiting

### Token Bucket Implementation

The plugin uses a token bucket algorithm for sophisticated rate limiting:

```csharp
public class TokenBucketRateLimiter
{
    private double _tokenBucket;
    private readonly double _tokensPerSecond;
    private readonly double _maxTokens;
    private DateTime _lastTokenUpdate = DateTime.MinValue;
    private readonly object _lock = new object();

    public TokenBucketRateLimiter(double tokensPerHour, double maxBurst = 0)
    {
        _tokensPerSecond = tokensPerHour / 3600.0;
        _maxTokens = maxBurst <= 0 ? tokensPerHour / 60.0 : maxBurst;
        _tokenBucket = _maxTokens / 2.0; // Start with half capacity
    }

    public bool TryAcquire(double tokens = 1.0)
    {
        lock (_lock)
        {
            RefillTokens();
            
            if (_tokenBucket < tokens)
                return false;
                
            _tokenBucket -= tokens;
            return true;
        }
    }

    public TimeSpan GetWaitTime(double tokens = 1.0)
    {
        lock (_lock)
        {
            RefillTokens();
            
            if (_tokenBucket >= tokens)
                return TimeSpan.Zero;
                
            double tokensNeeded = tokens - _tokenBucket;
            return TimeSpan.FromSeconds(tokensNeeded / _tokensPerSecond);
        }
    }

    private void RefillTokens()
    {
        var now = DateTime.UtcNow;
        
        if (_lastTokenUpdate == DateTime.MinValue)
        {
            _lastTokenUpdate = now;
            return;
        }
        
        var elapsed = now - _lastTokenUpdate;
        var tokensToAdd = elapsed.TotalSeconds * _tokensPerSecond;
        
        _tokenBucket = Math.Min(_maxTokens, _tokenBucket + tokensToAdd);
        _lastTokenUpdate = now;
    }
}
```

### Adaptive Jitter

To prevent thundering herd problems, the plugin implements adaptive jitter:

```csharp
public static class JitterCalculator
{
    private static readonly Random _random = new Random();
    
    public static TimeSpan CalculateJitter(double utilizationPercentage, 
                                          TimeSpan baseDelay,
                                          TimeSpan maxJitter)
    {
        // Higher utilization = more jitter
        double jitterFactor = Math.Min(1.0, utilizationPercentage / 100.0);
        double maxJitterMs = maxJitter.TotalMilliseconds * jitterFactor;
        
        // Add random jitter between 0 and calculated max
        double jitterMs = _random.NextDouble() * maxJitterMs;
        
        return baseDelay.Add(TimeSpan.FromMilliseconds(jitterMs));
    }
}
```