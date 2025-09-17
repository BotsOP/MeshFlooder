using System;
using System.Collections.Generic;
using System.IO;
using EditorAttributes;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.InputSystem;

public class MeshFlooderPrep : MonoBehaviour
{
    private const float FLOAT_TOLERANCE = 0.0001f;
    public Mesh mesh;
    public TextAsset neighbouringTrianglesTextFile;
    
    [Button]
    public void BakeNeighbouringTris()
    {
        int amountTriangles = mesh.triangles.Length / 3;
        NativeArray<float3> vertices = new NativeArray<float3>(mesh.vertices.Length, Allocator.Persistent);
        for (int i = 0; i < mesh.vertices.Length; i++)
        {
            vertices[i] = mesh.vertices[i];
        }
        
        List<int> indices = new List<int>();
        mesh.GetIndices(indices, 0);
        
        NativeArray<int3> neighbouringTriangles = new NativeArray<int3>(amountTriangles, Allocator.Persistent);
        
        FindNeighbouringTriangles findNeighbouringTriangles = new FindNeighbouringTriangles(
            neighbouringTriangles,
            indices.ToNativeArray(Allocator.Persistent),
            vertices,
            amountTriangles
        );
        JobHandle jobHandle = new JobHandle();
        jobHandle = findNeighbouringTriangles.ScheduleParallel(amountTriangles, 16, jobHandle);
        jobHandle.Complete();
        
        using (StreamWriter writer = new StreamWriter(Application.dataPath + "/" + mesh.name + "_NeighbouringTriangles.txt"))
        {
            for (int i = 0; i < findNeighbouringTriangles.neighbouringTriangles.Length; i++)
            {
                int3 v = findNeighbouringTriangles.neighbouringTriangles[i];
                writer.WriteLine($"{v.x} {v.y} {v.z}");
            }
        }
        
        findNeighbouringTriangles.Dispose();
    }

    private void Update()
    {
        if (Keyboard.current.spaceKey.isPressed)
        {
            RaycastHit hit;
            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        
            if (Physics.Raycast(ray, out hit)) 
            {
                SetFloodVertices(hit.triangleIndex);
            }
            
        }
    }
    public void SetFloodVertices(int triangleIndex)
    {
        NativeArray<int3> neighbouringTriangles = new NativeArray<int3>((int)mesh.GetIndexCount(0) / 3, Allocator.TempJob);
        string[] neighbouringText = neighbouringTrianglesTextFile.text.Split("\n");
        string[] int3Values = new string[3];
        for (int i = 0; i < (int)mesh.GetIndexCount(0) / 3; i++)
        {
            int3Values = neighbouringText[i].Split(" ");
            neighbouringTriangles[i] = new int3(int.Parse(int3Values[0]), int.Parse(int3Values[1]), int.Parse(int3Values[2]));
        }
        
        NativeArray<int> indices = new NativeArray<int>();
        int[] indicesList = mesh.GetIndices(0);
        for (int i = 0; i < indices.Length; i++)
        {
            indices[i] = indicesList[i];
        }

        FloodNeighbours floodNeighbours = new FloodNeighbours(triangleIndex, neighbouringTriangles, indices, mesh.vertexCount);
        floodNeighbours.Schedule().Complete();

        Vector2[] newUvs = new Vector2[mesh.vertexCount];
        for (int i = 0; i < mesh.vertexCount; i++)
        {
            newUvs[i] = new Vector2(floodNeighbours.newWaveValue[i], 0);
        }
        floodNeighbours.newWaveValue.Dispose();

        mesh.SetUVs(0, newUvs);
    }
    
    [BurstCompile]
    private struct FloodNeighbours : IJob
    {
        public NativeArray<float> newWaveValue;
        
        [DeallocateOnJobCompletion] private NativeArray<int3> neighbouringTriangles;
        [DeallocateOnJobCompletion] private NativeQueue<int> nextWaveTriangles;
        [DeallocateOnJobCompletion] private NativeArray<bool> checkedVertices;
        [DeallocateOnJobCompletion] private NativeArray<bool> checkedTriangles;
        [DeallocateOnJobCompletion] private NativeArray<int> indices;
        
        private int startTriangle;

        public FloodNeighbours(int startTriangle, NativeArray<int3> neighbouringTriangles, NativeArray<int> indices, int amountVertices) : this()
        {
            this.startTriangle = startTriangle;
            this.neighbouringTriangles = neighbouringTriangles;
            this.indices = indices;

            newWaveValue = new NativeArray<float>(amountVertices, Allocator.TempJob);
            nextWaveTriangles = new NativeQueue<int>(Allocator.TempJob);
            checkedTriangles = new NativeArray<bool>(indices.Length / 3, Allocator.TempJob);
            checkedTriangles[startTriangle] = true;
        }

        public void Execute()
        {
            nextWaveTriangles.Enqueue(startTriangle);
            int wave = 0;
            int maxWhile = 0;
            while (!nextWaveTriangles.IsEmpty() && maxWhile < 10000)
            {
                maxWhile++;
                
                int amountTriangles = nextWaveTriangles.Count;
                for (int i = 0; i < amountTriangles; i++)
                {
                    int3 neighbourTriangle = nextWaveTriangles.Dequeue();
                    if (!checkedTriangles[neighbourTriangle.x])
                    {
                        checkedTriangles[neighbourTriangle.x] = true;
                            
                        int vertexIndex1 = indices[neighbourTriangle.x * 3 + 0];
                        int vertexIndex2 = indices[neighbourTriangle.x * 3 + 1];
                        int vertexIndex3 = indices[neighbourTriangle.x * 3 + 2];
                        newWaveValue[vertexIndex1] = wave;
                        newWaveValue[vertexIndex2] = wave;
                        newWaveValue[vertexIndex3] = wave;

                        int3 neighbourTriangles1 = neighbouringTriangles[neighbourTriangle.x];
                        if (!checkedTriangles[neighbourTriangles1.x])
                            nextWaveTriangles.Enqueue(neighbourTriangles1.x);
                        if (!checkedTriangles[neighbourTriangles1.y])
                            nextWaveTriangles.Enqueue(neighbourTriangles1.y);
                        if (!checkedTriangles[neighbourTriangles1.z])
                            nextWaveTriangles.Enqueue(neighbourTriangles1.z);
                    }
                    
                    if (!checkedTriangles[neighbourTriangle.y])
                    {
                        checkedTriangles[neighbourTriangle.y] = true;
                        
                        int vertexIndex1 = indices[neighbourTriangle.y * 3 + 0];
                        int vertexIndex2 = indices[neighbourTriangle.y * 3 + 1];
                        int vertexIndex3 = indices[neighbourTriangle.y * 3 + 2];
                        newWaveValue[vertexIndex1] = wave;
                        newWaveValue[vertexIndex2] = wave;
                        newWaveValue[vertexIndex3] = wave;

                        int3 neighbourTriangles1 = neighbouringTriangles[neighbourTriangle.y];
                        if (!checkedTriangles[neighbourTriangles1.x])
                            nextWaveTriangles.Enqueue(neighbourTriangles1.x);
                        if (!checkedTriangles[neighbourTriangles1.y])
                            nextWaveTriangles.Enqueue(neighbourTriangles1.y);
                        if (!checkedTriangles[neighbourTriangles1.z])
                            nextWaveTriangles.Enqueue(neighbourTriangles1.z);
                    }
                    
                    if (!checkedTriangles[neighbourTriangle.z])
                    {
                        checkedTriangles[neighbourTriangle.z] = true;
                        
                        int vertexIndex1 = indices[neighbourTriangle.z * 3 + 0];
                        int vertexIndex2 = indices[neighbourTriangle.z * 3 + 1];
                        int vertexIndex3 = indices[neighbourTriangle.z * 3 + 2];
                        newWaveValue[vertexIndex1] = wave;
                        newWaveValue[vertexIndex2] = wave;
                        newWaveValue[vertexIndex3] = wave;

                        int3 neighbourTriangles1 = neighbouringTriangles[neighbourTriangle.z];
                        if (!checkedTriangles[neighbourTriangles1.x])
                            nextWaveTriangles.Enqueue(neighbourTriangles1.x);
                        if (!checkedTriangles[neighbourTriangles1.y])
                            nextWaveTriangles.Enqueue(neighbourTriangles1.y);
                        if (!checkedTriangles[neighbourTriangles1.z])
                            nextWaveTriangles.Enqueue(neighbourTriangles1.z);
                    }
                }
                wave++;
            }
        }
    }

    [BurstCompile]
    private struct FindNeighbouringTriangles : IJobFor
    {
        [WriteOnly, NativeDisableParallelForRestriction] public NativeArray<int3> neighbouringTriangles;
        [WriteOnly, NativeDisableParallelForRestriction] public NativeArray<ushort> neighbouringTrianglesShort;
        
        [Unity.Collections.ReadOnly, DeallocateOnJobCompletion] private NativeArray<int> indices;
        [Unity.Collections.ReadOnly, DeallocateOnJobCompletion] private NativeArray<ushort> indicesShort;
        [Unity.Collections.ReadOnly, DeallocateOnJobCompletion] private NativeArray<float3> vertices;
        private readonly int amountTriangles;
        private readonly bool useShort;

        public FindNeighbouringTriangles(NativeArray<int3> neighbouringTriangles, NativeArray<int> indices, NativeArray<float3> vertices, int amountTriangles)
        {
            this.neighbouringTriangles = neighbouringTriangles;
            this.indices = indices;
            this.vertices = vertices;
            this.amountTriangles = amountTriangles;
            indicesShort = new NativeArray<ushort>(0, Allocator.Persistent);
            neighbouringTrianglesShort = new NativeArray<ushort>(0, Allocator.Persistent);
            
            useShort = false;
        }
        public FindNeighbouringTriangles(NativeArray<ushort> neighbouringTrianglesShort, NativeArray<ushort> indicesShort, NativeArray<float3> vertices, int amountTriangles)
        {
            this.neighbouringTrianglesShort = neighbouringTrianglesShort;
            this.indicesShort = indicesShort;
            this.vertices = vertices;
            this.amountTriangles = amountTriangles;
            indices = new NativeArray<int>(0, Allocator.Persistent);
            neighbouringTriangles = new NativeArray<int3>(0, Allocator.Persistent);
            
            useShort = true;
        }

        public void Dispose()
        {
            vertices.Dispose();
            indices.Dispose();
            indicesShort.Dispose();
            neighbouringTriangles.Dispose();
            neighbouringTrianglesShort.Dispose();
        }

        public void Execute(int index)
        {
            if (useShort)
            {
                FindNeighbouringTrianglesShort(index);
            }
            else
            {
                FindNeighbouringTrianglesInt(index);
            }
        }
        
        private void FindNeighbouringTrianglesShort(int index)
        {
            ushort baseIndex1 = indicesShort[index * 3];
            ushort baseIndex2 = indicesShort[index * 3 + 1];
            ushort baseIndex3 = indicesShort[index * 3 + 2];
            float3 baseVertex1 = vertices[baseIndex1];
            float3 baseVertex2 = vertices[baseIndex2];
            float3 baseVertex3 = vertices[baseIndex3];

            int amountTrianglesFound = 0;
            for (ushort i = 0; i < amountTriangles; i++)
            {
                if (amountTrianglesFound == 3)
                {
                    break;
                }
                
                ushort index1 = indicesShort[i * 3];
                ushort index2 = indicesShort[i * 3 + 1];
                ushort index3 = indicesShort[i * 3 + 2];
                float3 vertex1 = vertices[index1];
                float3 vertex2 = vertices[index2];
                float3 vertex3 = vertices[index3];

                int matchingVertices = 0;
                if (math.all(math.abs(baseVertex1 - vertex1) < FLOAT_TOLERANCE))
                    matchingVertices++;
                if (math.all(math.abs(baseVertex2 - vertex1) < FLOAT_TOLERANCE))
                    matchingVertices++;
                if (math.all(math.abs(baseVertex3 - vertex1) < FLOAT_TOLERANCE))
                    matchingVertices++;
                
                if (math.all(math.abs(baseVertex1 - vertex2) < FLOAT_TOLERANCE))
                    matchingVertices++;
                if (math.all(math.abs(baseVertex2 - vertex2) < FLOAT_TOLERANCE))
                    matchingVertices++;
                if (math.all(math.abs(baseVertex3 - vertex2) < FLOAT_TOLERANCE))
                    matchingVertices++;
                
                if (math.all(math.abs(baseVertex1 - vertex3) < FLOAT_TOLERANCE))
                    matchingVertices++;
                if (math.all(math.abs(baseVertex2 - vertex3) < FLOAT_TOLERANCE))
                    matchingVertices++;
                if (math.all(math.abs(baseVertex3 - vertex3) < FLOAT_TOLERANCE))
                    matchingVertices++;

                if (matchingVertices != 2)
                    continue;

                neighbouringTrianglesShort[index * 3 + amountTrianglesFound] = i;
                
                amountTrianglesFound++;
            }
        }
        private void FindNeighbouringTrianglesInt(int index)
        {
            int baseIndex1 = indices[index * 3];
            int baseIndex2 = indices[index * 3 + 1];
            int baseIndex3 = indices[index * 3 + 2];
            float3 baseVertex1 = vertices[baseIndex1];
            float3 baseVertex2 = vertices[baseIndex2];
            float3 baseVertex3 = vertices[baseIndex3];

            int amountTrianglesFound = 0;
            int3 neighbouringTriangle = new int3();
            for (int i = 0; i < amountTriangles; i++)
            {
                int index1 = indices[i * 3];
                int index2 = indices[i * 3 + 1];
                int index3 = indices[i * 3 + 2];
                float3 vertex1 = vertices[index1];
                float3 vertex2 = vertices[index2];
                float3 vertex3 = vertices[index3];

                int matchingVertices = 0;
                if (math.all(math.abs(baseVertex1 - vertex1) < FLOAT_TOLERANCE))
                    matchingVertices++;
                if (math.all(math.abs(baseVertex2 - vertex1) < FLOAT_TOLERANCE))
                    matchingVertices++;
                if (math.all(math.abs(baseVertex3 - vertex1) < FLOAT_TOLERANCE))
                    matchingVertices++;
                
                if (math.all(math.abs(baseVertex1 - vertex2) < FLOAT_TOLERANCE))
                    matchingVertices++;
                if (math.all(math.abs(baseVertex2 - vertex2) < FLOAT_TOLERANCE))
                    matchingVertices++;
                if (math.all(math.abs(baseVertex3 - vertex2) < FLOAT_TOLERANCE))
                    matchingVertices++;
                
                if (math.all(math.abs(baseVertex1 - vertex3) < FLOAT_TOLERANCE))
                    matchingVertices++;
                if (math.all(math.abs(baseVertex2 - vertex3) < FLOAT_TOLERANCE))
                    matchingVertices++;
                if (math.all(math.abs(baseVertex3 - vertex3) < FLOAT_TOLERANCE))
                    matchingVertices++;

                if (matchingVertices != 2)
                    continue;
                
                switch (amountTrianglesFound)
                {
                    case 0:
                        neighbouringTriangle.x = i;
                        break;
                    case 1:
                        neighbouringTriangle.y = i;
                        break;
                    case 2:
                        neighbouringTriangle.z = i;
                        break;
                }
                amountTrianglesFound++;
            }
            neighbouringTriangles[index] = neighbouringTriangle;
        }
    }
}

