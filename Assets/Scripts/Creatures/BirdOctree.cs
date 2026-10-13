using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Reusable point octree. Point indices refer to the current immutable position snapshot.</summary>
public sealed class BirdOctree
{
    public const int LeafCapacity = 8;
    public const int MaximumDepth = 6;

    private sealed class Node
    {
        public Bounds Bounds;
        public int Children = -1;
        public int Head = -1;
        public int Count;
    }

    private readonly List<Node> pool = new List<Node>();
    private Vector3[] positions;
    private int[] next = Array.Empty<int>();
    private int usedNodes;

    public int PointCount { get; private set; }
    public int NodeCount => usedNodes;
    public int LastCandidateChecks { get; private set; }

    public BirdOctree(int initialNodeCapacity = 512)
    {
        for (int i = 0; i < initialNodeCapacity; i++) pool.Add(new Node());
    }

    public void Rebuild(Vector3[] positionsWS, int count)
    {
        if (positionsWS == null || count < 0 || count > positionsWS.Length)
            throw new ArgumentOutOfRangeException(nameof(count));
        positions = positionsWS;
        PointCount = count;
        usedNodes = 0;
        if (next.Length < count) Array.Resize(ref next, Mathf.NextPowerOfTwo(count));
        if (count == 0) return;

        // The search root encloses actual birds, including those released outside flight bounds.
        Vector3 minimum = positionsWS[0], maximum = minimum;
        for (int i = 1; i < count; i++)
        {
            minimum = Vector3.Min(minimum, positionsWS[i]);
            maximum = Vector3.Max(maximum, positionsWS[i]);
        }
        Vector3 size = maximum - minimum;
        float side = Mathf.Max(0.01f, Mathf.Max(size.x, Mathf.Max(size.y, size.z)) + 0.02f);
        Allocate(new Bounds((minimum + maximum) * 0.5f, Vector3.one * side));
        for (int i = 0; i < count; i++) Insert(0, i, 0);
    }

    private int Allocate(Bounds bounds)
    {
        int index = usedNodes++;
        if (index == pool.Count) pool.Add(new Node());
        Node node = pool[index];
        node.Bounds = bounds;
        node.Children = -1;
        node.Head = -1;
        node.Count = 0;
        return index;
    }

    private int ChildIndex(Node node, Vector3 position)
    {
        Vector3 center = node.Bounds.center;
        int octant = (position.x >= center.x ? 1 : 0)
                   | (position.y >= center.y ? 2 : 0)
                   | (position.z >= center.z ? 4 : 0);
        return node.Children + octant;
    }

    private void Insert(int nodeIndex, int point, int depth)
    {
        Node node = pool[nodeIndex];
        if (node.Children >= 0)
        {
            Insert(ChildIndex(node, positions[point]), point, depth + 1);
            return;
        }
        if (node.Count >= LeafCapacity && depth < MaximumDepth)
        {
            Vector3 childSize = node.Bounds.size * 0.5f;
            Vector3 offset = node.Bounds.size * 0.25f;
            node.Children = usedNodes;
            for (int octant = 0; octant < 8; octant++)
            {
                Vector3 signs = new Vector3((octant & 1) == 0 ? -1 : 1,
                    (octant & 2) == 0 ? -1 : 1, (octant & 4) == 0 ? -1 : 1);
                Allocate(new Bounds(node.Bounds.center + Vector3.Scale(offset, signs), childSize));
            }
            int previous = node.Head;
            node.Head = -1;
            node.Count = 0;
            while (previous >= 0)
            {
                int following = next[previous];
                Insert(ChildIndex(node, positions[previous]), previous, depth + 1);
                previous = following;
            }
            Insert(ChildIndex(node, positions[point]), point, depth + 1);
            return;
        }
        next[point] = node.Head;
        node.Head = point;
        node.Count++;
    }

    /// <summary>Exact radius search, inclusive of its boundary; caller excludes its own point index.</summary>
    public void Query(Vector3 centerWS, float radius, List<int> results)
    {
        results.Clear();
        LastCandidateChecks = 0;
        if (usedNodes == 0) return;
        radius = Mathf.Max(0f, radius);
        QueryNode(0, centerWS, radius * radius, results);
    }

    private void QueryNode(int index, Vector3 center, float radiusSquared, List<int> results)
    {
        Node node = pool[index];
        if (node.Children < 0 && node.Head < 0) return;
        // Keep the hot sphere/AABB test managed; Bounds.SqrDistance crosses into native code.
        Vector3 minimum = node.Bounds.min, maximum = node.Bounds.max;
        float dx = center.x < minimum.x ? minimum.x - center.x : center.x > maximum.x ? center.x - maximum.x : 0f;
        float dy = center.y < minimum.y ? minimum.y - center.y : center.y > maximum.y ? center.y - maximum.y : 0f;
        float dz = center.z < minimum.z ? minimum.z - center.z : center.z > maximum.z ? center.z - maximum.z : 0f;
        if (dx * dx + dy * dy + dz * dz > radiusSquared) return;
        if (node.Children >= 0)
        {
            for (int child = 0; child < 8; child++) QueryNode(node.Children + child, center, radiusSquared, results);
            return;
        }
        for (int point = node.Head; point >= 0; point = next[point])
        {
            LastCandidateChecks++;
            if ((positions[point] - center).sqrMagnitude <= radiusSquared) results.Add(point);
        }
    }

    public void DrawGizmos()
    {
        for (int i = 0; i < usedNodes; i++)
            if (pool[i].Children < 0 && pool[i].Count > 0)
                Gizmos.DrawWireCube(pool[i].Bounds.center, pool[i].Bounds.size);
    }
}
