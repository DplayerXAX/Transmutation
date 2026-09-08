using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Tutorial604
{
    public partial class Grass : MonoBehaviour
    {
        [SerializeField]
        private ComputeShader computeShader;
        [SerializeField]
        private Material material;
        public Camera cam;

        public int tileResolution = 32;
        public int tileCount = 10;



        [SerializeField, Range(0, 2)]
        public float jitterStrength;

        [Header("Culling")]
        public float distanceCullStartDisLOD0;
        public float distanceCullEndDisLOD0;
        public float distanceCullStartDisLOD1;
        public float distanceCullEndDisLOD1;
        //[Range(0f, 1f)]
        //public float distanceCullMinimumGrassAmount;
        public float frustumCullNearOffset;
        public float frustumCullEdgeOffset;
        [Tooltip("Grass within this horizontal distance from the camera skips frustum culling (fixes missing grass at your feet when looking forward).")]
        public float frustumCullBypassDistance = 15f;
        [Tooltip("Skip spawning grass when the terrain slope from world up exceeds this angle (degrees).")]
        [Range(0f, 90f)]
        public float maxSlopeAngle = 45f;

        [Header("Noise Mask")]
        [Tooltip("Only spawn grass where FBM Perlin noise is above this threshold (0 = densest, 1 = almost none).")]
        [Range(0f, 1f)]
        public float noiseThreshold = 0.4f;
        [Tooltip("World-space frequency of the base Perlin octave. Smaller = larger patches.")]
        public float noiseScale = 0.05f;
        [Tooltip("Shifts the noise pattern in world XZ.")]
        public Vector2 noiseOffset = Vector2.zero;
        [Tooltip("Number of Perlin layers. More octaves = finer detail / more natural edges.")]
        [Range(1, 8)]
        public int noiseOctaves = 4;
        [Tooltip("Amplitude falloff per octave (typically ~0.5).")]
        [Range(0.1f, 1f)]
        public float noisePersistence = 0.5f;
        [Tooltip("Frequency multiplier per octave (typically ~2).")]
        [Range(1.1f, 4f)]
        public float noiseLacunarity = 2f;

        [Header("Clumping")]
        public int clumpTextureHeight;
        public int clumpTextureWidth;
        public Material clumpingVoronoiMaterial;
        public float clumpScale;
        public List<ClumpParameters> clumpParameters;

        [Header("Wind")]
        [SerializeField] private Texture2D localWindTex;
        [Range(0.0f, 1.0f)]
        [SerializeField] private float localWindStrength = 0.5f;
        [SerializeField] private float localWindScale = 0.01f;
        [SerializeField] private float localWindSpeed = 0.1f;
        [Range(0.0f, 1.0f)]
        [SerializeField] private float localWindRotateAmount = 0.3f;


        private static readonly int
            grassBladesBufferID = Shader.PropertyToID("_GrassBlades"),
            resolutionXID = Shader.PropertyToID("_ResolutionX"),
            resolutionYID = Shader.PropertyToID("_ResolutionY"),
            grassSpacingID = Shader.PropertyToID("_GrassSpacing"),
            jitterStrengthID = Shader.PropertyToID("_JitterStrength"),
            heightMapID = Shader.PropertyToID("_HeightMap"),
            detailMapID = Shader.PropertyToID("_DetailMap"),
            terrainPositionID = Shader.PropertyToID("_TerrainPosition"),
            tilePositionID = Shader.PropertyToID("_TilePosition"),
            heightMapScaleID = Shader.PropertyToID("_HeightMapScale"),
            heightMapMultiplierID = Shader.PropertyToID("_HeightMapMultiplier"),
            distanceCullStartDistID = Shader.PropertyToID("_DistanceCullStartDist"),
            distanceCullEndDistID = Shader.PropertyToID("_DistanceCullEndDist"),
            distanceCullMinimumGrassAmountlID = Shader.PropertyToID("_DistanceCullMinimumGrassAmount"),
            worldSpaceCameraPositionID = Shader.PropertyToID("_WSpaceCameraPos"),
            vpMatrixID = Shader.PropertyToID("_VP_MATRIX"),
            frustumCullNearOffsetID = Shader.PropertyToID("_FrustumCullNearOffset"),
            frustumCullEdgeOffsetID = Shader.PropertyToID("_FrustumCullEdgeOffset"),
            frustumCullBypassDistanceID = Shader.PropertyToID("_FrustumCullBypassDistance"),
            maxSlopeAngleID = Shader.PropertyToID("_MaxSlopeAngle"),
            noiseThresholdID = Shader.PropertyToID("_NoiseThreshold"),
            noiseScaleID = Shader.PropertyToID("_NoiseScale"),
            noiseOffsetID = Shader.PropertyToID("_NoiseOffset"),
            noiseOctavesID = Shader.PropertyToID("_NoiseOctaves"),
            noisePersistenceID = Shader.PropertyToID("_NoisePersistence"),
            noiseLacunarityID = Shader.PropertyToID("_NoiseLacunarity"),
            clumpParametersID = Shader.PropertyToID("_ClumpParameters"),
            numClumpParametersID = Shader.PropertyToID("_NumClumpParameters"),
            clumpTexID = Shader.PropertyToID("ClumpTex"),
            clumpScaleID = Shader.PropertyToID("_ClumpScale"),
            LocalWindTexID = Shader.PropertyToID("_LocalWindTex"),
            LocalWindScaleID = Shader.PropertyToID("_LocalWindScale"),
            LocalWindSpeedID = Shader.PropertyToID("_LocalWindSpeed"),
            LocalWindStrengthID = Shader.PropertyToID("_LocalWindStrength"),
            LocalWindRotateAmountID = Shader.PropertyToID("_LocalWindRotateAmount"),
            TimeID = Shader.PropertyToID("_Time");

        private ComputeBuffer grassBladesBuffer;
        private ComputeBuffer meshTrianglesBuffer;
        private ComputeBuffer meshColorsBuffer;
        private ComputeBuffer meshUvsBuffer;
        private ComputeBuffer argsBuffer;
        private ComputeBuffer clumpParametersBuffer;
        private const int ARGS_STRIDE = sizeof(int) * 5;
        private Mesh clonedMesh;
        private Bounds bounds;
        private ClumpParameters[] clumpParametersArray;
        private Texture2D clumpTexture;
        private float grassSpacing = 0.1f;
        private List<Tile> visibleTiles = new List<Tile>();
        private List<Tile> tilesToRender = new List<Tile>();
        private float tileSizeX = 0, tileSizeZ = 0;
        private List<Terrain> terrains = new List<Terrain>();

        void Awake()
        {
            if (cam == null)
            {
                cam = Camera.main;
            }

            Initialize();

            bounds = new Bounds(Vector3.zero, Vector3.one * 10000f);
        }

        void Start()
        {
            if (cam == null)
            {
                cam = Camera.main;
                if (cam == null)
                {
                    Debug.LogError("[Grass] No camera assigned and Camera.main was not found.", this);
                }
            }
        }

        void Update()
        {
            if (cam == null || material == null || computeShader == null)
            {
                return;
            }

            UpdateGrassTiles();
            UpdateGpuParameters();
        }

        void LateUpdate()
        {
            if (cam == null || material == null || computeShader == null)
            {
                return;
            }

            RenderGrass();
        }

        void OnDestroy()
        {
            DisposeBuffers();
            DestroyClumpTexture();
        }

        private void Initialize()
        {
            CollectTerrains();
            InitializeComputeBuffers();
            SetupMeshBuffers();
            CreateClumpTexture();
            CalculateGrassSpacing();
        }

        private void CollectTerrains()
        {
            terrains.Clear();

            Terrain[] allTerrains = FindObjectsOfType<Terrain>();
            foreach (Terrain t in allTerrains)
            {
                if (t.enabled)
                {
                    terrains.Add(t);
                }
            }

            if (terrains.Count == 0)
            {
                Debug.LogWarning("Terrain count = 0");
            }
        }

        private void CalculateGrassSpacing()
        {
            if (terrains.Count > 0)
            {
                grassSpacing = terrains[0].terrainData.size.x / (tileCount * tileResolution);
            }
        }

        private void InitializeComputeBuffers()
        {
            int tileMax = 16;
            //14 floats: position, rotAngle, hash, height, width, tilt, bend, surfaceNorm, windForce, sideBend
            grassBladesBuffer = new ComputeBuffer(tileResolution * tileResolution * tileMax, sizeof(float) * 14, ComputeBufferType.Append);
            grassBladesBuffer.SetCounterValue(0);

            argsBuffer = new ComputeBuffer(1, ARGS_STRIDE, ComputeBufferType.IndirectArguments);

            if (clumpParameters == null || clumpParameters.Count == 0)
            {
                Debug.LogError("[Grass] Clump Parameters list is empty. Assign at least one clump profile on the Grass component.", this);
                return;
            }

            clumpParametersBuffer = new ComputeBuffer(clumpParameters.Count, sizeof(float) * 10);
            UpdateClumpParametersBuffer();
        }

        private void SetupMeshBuffers()
        {
            clonedMesh = GrassMesh.CreateHighLODMesh();
            clonedMesh.name = "Grass Instance Mesh";

            CreateComputeBuffersForMesh();

            // Initialize args buffer with mesh triangle count. Instance count will be updated later in GPU.
            argsBuffer.SetData(new int[] { meshTrianglesBuffer.count, 0, 0, 0, 0 });
        }

        private ComputeBuffer CreateBuffer<T>(T[] data, int stride) where T : struct
        {
            ComputeBuffer buffer = new ComputeBuffer(data.Length, stride);
            buffer.SetData(data);
            return buffer;
        }

        private void CreateComputeBuffersForMesh()
        {
            int[] triangles = clonedMesh.triangles;
            Color[] colors = clonedMesh.colors;
            Vector2[] uvs = clonedMesh.uv;

            meshTrianglesBuffer = CreateBuffer<int>(triangles, sizeof(int));
            meshColorsBuffer = CreateBuffer<Color>(colors, sizeof(float) * 4);
            meshUvsBuffer = CreateBuffer<Vector2>(uvs, sizeof(float) * 2);

            material.SetBuffer("Triangles", meshTrianglesBuffer);
            material.SetBuffer("Colors", meshColorsBuffer);
            material.SetBuffer("Uvs", meshUvsBuffer);
            material.SetBuffer(grassBladesBufferID, grassBladesBuffer);
        }

        private void CreateClumpTexture()
        {
            clumpingVoronoiMaterial.SetFloat("_NumClumpTypes", clumpParameters.Count);
            RenderTexture clumpVoronoiRenderTexture = RenderTexture.GetTemporary(clumpTextureWidth, clumpTextureHeight, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
            Graphics.Blit(null, clumpVoronoiRenderTexture, clumpingVoronoiMaterial, 0);

            RenderTexture.active = clumpVoronoiRenderTexture;
            clumpTexture = new Texture2D(clumpTextureWidth, clumpTextureHeight, TextureFormat.RGBAHalf, false, true);
            clumpTexture.filterMode = FilterMode.Point;
            clumpTexture.ReadPixels(new Rect(0, 0, clumpTextureWidth, clumpTextureHeight), 0, 0, true);
            clumpTexture.Apply();
            RenderTexture.active = null;

            RenderTexture.ReleaseTemporary(clumpVoronoiRenderTexture);
        }

        private void UpdateGrassTiles()
        {
            tilesToRender.Clear();

            foreach (Terrain terrain in terrains)
            {
                if (terrain != null)
                {
                    UpdateSurroundingTilesForTerrain(terrain);
                }
            }
                

            UpdateVisibleTiles();
        }

        private void UpdateSurroundingTilesForTerrain(Terrain terrain)
        {
            Vector3 terrainSize = terrain.terrainData.size;
            tileSizeZ = tileSizeX = terrainSize.x / tileCount;

            Vector3 cameraPositionInTerrainSpace = cam.transform.position - terrain.transform.position;
            int cameraTileXIndex = Mathf.FloorToInt(cameraPositionInTerrainSpace.x / tileSizeX);
            int cameraTileZIndex = Mathf.FloorToInt(cameraPositionInTerrainSpace.z / tileSizeZ);

            if (cameraTileXIndex >= -4 && cameraTileXIndex < tileCount + 3 &&
                cameraTileZIndex >= -4 && cameraTileZIndex < tileCount + 3)
            {
                HashSet<Vector2Int> mergedTileGridPositions = new HashSet<Vector2Int>();

                for (int xIndex = cameraTileXIndex - 3; xIndex <= cameraTileXIndex + 4; xIndex++)
                {
                    for (int zIndex = cameraTileZIndex - 3; zIndex <= cameraTileZIndex + 4; zIndex++)
                    {
                        Vector2Int currentGridPosition = new Vector2Int(xIndex, zIndex);

                        if (IsStandardTile(xIndex, cameraTileXIndex) && IsStandardTile(zIndex, cameraTileZIndex) && IsTileWithinTerrainBounds(xIndex, zIndex))
                        {
                            AddStandardTile(terrain, currentGridPosition);
                        }
                        else
                        {
                            (Vector2Int mergedTileStartPosition, bool isMerged) = CalculateMergedTileStartPosition(xIndex, zIndex, cameraTileXIndex, cameraTileZIndex);
                            if (isMerged)
                            {
                                mergedTileGridPositions.Add(mergedTileStartPosition);
                            }
                        }
                    }
                }

                AddMergedTiles(terrain, mergedTileGridPositions);
            }
        }

        private bool IsStandardTile(int tileIndex, int cameraTileIndex)
        {
            return tileIndex >= cameraTileIndex - 1 && tileIndex <= cameraTileIndex + 2;
        }

        private bool IsTileWithinTerrainBounds(int xIndex, int zIndex)
        {
            return xIndex >= 0 && xIndex < tileCount && zIndex >= 0 && zIndex < tileCount;
        }

        private void AddStandardTile(Terrain terrain, Vector2Int gridPosition)
        {
            Bounds tileBounds = CalculateTileBounds(terrain, gridPosition.x, gridPosition.y);
            tilesToRender.Add(new Tile(terrain, tileBounds, gridPosition, 1f, 1, 1));
        }

        private void AddMergedTiles(Terrain terrain, HashSet<Vector2Int> mergedTileGridPositions)
        {
            foreach (Vector2Int gridPosition in mergedTileGridPositions)
            {
                if (gridPosition.x <= -2 || gridPosition.x >= tileCount || gridPosition.y <= -2 || gridPosition.y >= tileCount) continue;

                int xResolutionDivisor = 1;
                int zResolutionDivisor = 1;
                int posX = gridPosition.x;
                int posY = gridPosition.y;

                if (gridPosition.x == -1) { xResolutionDivisor = 2; posX = 0; }
                if (gridPosition.x == tileCount - 1) { xResolutionDivisor = 2; }
                if (gridPosition.y == -1) { zResolutionDivisor = 2; posY = 0; }
                if (gridPosition.y == tileCount - 1) { zResolutionDivisor = 2; }

                Bounds mergedBounds = CalculateTileBounds(terrain, posX, posY, 2f / xResolutionDivisor, 2f / zResolutionDivisor);
                tilesToRender.Add(new Tile(terrain, mergedBounds, new Vector2Int(posX, posY), 2f, xResolutionDivisor, zResolutionDivisor));
            }
        }

        private Bounds CalculateTileBounds(Terrain terrain, int tileXIndex, int tileZIndex, float tileScaleX = 1.0f, float tileScaleZ = 1.0f)
        {
            Vector3 terrainPos = terrain.transform.position;
            // Terrain pivot is at the bottom-left corner; surface spans [0, size.y] above it.
            // The old ±10 slab at the pivot caused gizmos and frustum tests to sit far below the mesh.
            float verticalExtent = terrain.terrainData.size.y + 5f;

            Vector3 min = terrainPos + new Vector3(tileXIndex * tileSizeX, 0f, tileZIndex * tileSizeZ);
            Vector3 max = min + new Vector3(tileSizeX * tileScaleX, verticalExtent, tileSizeZ * tileScaleZ);

            Bounds bounds = new Bounds();
            bounds.SetMinMax(min, max);
            return bounds;
        }

        private (Vector2Int, bool) CalculateMergedTileStartPosition(int xIndex, int zIndex, int cameraTileXIndex, int cameraTileZIndex)
        {
            Vector2Int mergedStartPos = Vector2Int.zero;
            bool isMerged = false;

            // 左侧两列 (cx-3, cx-2)
            if (xIndex <= cameraTileXIndex - 2)
            {
                int startZIndex = cameraTileZIndex - 3;
                int groupZIndex = (zIndex - startZIndex) / 2;
                mergedStartPos = new Vector2Int(cameraTileXIndex - 3, startZIndex + groupZIndex * 2);
                isMerged = true;
            }
            // 右侧两列 (cx+3, cx+4)
            else if (xIndex >= cameraTileXIndex + 3)
            {
                int startZIndex = cameraTileZIndex - 3;
                int groupZIndex = (zIndex - startZIndex) / 2;
                mergedStartPos = new Vector2Int(cameraTileXIndex + 3, startZIndex + groupZIndex * 2);
                isMerged = true;
            }
            // 上方两行且在中间列 (cy-3, cy-2)
            else if (zIndex <= cameraTileZIndex - 2 && IsStandardTile(xIndex, cameraTileXIndex))
            {
                int startXIndex = cameraTileXIndex - 1;
                int groupXIndex = (xIndex - startXIndex) / 2;
                mergedStartPos = new Vector2Int(startXIndex + groupXIndex * 2, cameraTileZIndex - 3);
                isMerged = true;
            }
            // 下方两行且在中间列 (cy+3, cy+4)
            else if (zIndex >= cameraTileZIndex + 3 && IsStandardTile(xIndex, cameraTileXIndex))
            {
                int startXIndex = cameraTileXIndex - 1;
                int groupXIndex = (xIndex - startXIndex) / 2;
                mergedStartPos = new Vector2Int(startXIndex + groupXIndex * 2, cameraTileZIndex + 3);
                isMerged = true;
            }

            return (mergedStartPos, isMerged);
        }

        private void UpdateVisibleTiles()
        {
            visibleTiles.Clear();
            Plane[] frustumPlanes = GeometryUtility.CalculateFrustumPlanes(cam);

            foreach (Tile tile in tilesToRender)
            {
                if (IsVisibleInFrustum(frustumPlanes, tile.bounds))
                {
                    visibleTiles.Add(tile);
                }
            }
        }

        private bool IsVisibleInFrustum(Plane[] planes, Bounds bounds)
        {
            return GeometryUtility.TestPlanesAABB(planes, bounds);
        }

        private void UpdateGpuParameters()
        {
            grassBladesBuffer.SetCounterValue(0);

            computeShader.SetVector(worldSpaceCameraPositionID, cam.transform.position);

            Matrix4x4 projectionMatrix = GL.GetGPUProjectionMatrix(cam.projectionMatrix, false);
            Matrix4x4 viewProjectionMatrix = projectionMatrix * cam.worldToCameraMatrix;
            computeShader.SetMatrix(vpMatrixID, viewProjectionMatrix);
            computeShader.SetFloat(frustumCullBypassDistanceID, frustumCullBypassDistance);
            computeShader.SetFloat(TimeID, Time.time);

            foreach (Tile tile in visibleTiles)
            {
                //if (tile.spaceMultiplier == 2) continue;

                SetupComputeShaderForTile(tile);

                int threadGroupsX = Mathf.CeilToInt(tileResolution / (8f * tile.xResolutionDivisor));
                int threadGroupsZ = Mathf.CeilToInt(tileResolution / (8f * tile.zResolutionDivisor));

                computeShader.Dispatch(0, threadGroupsX, threadGroupsZ, 1);
            }
        }

        private void UpdateClumpParametersBuffer()
        {
            if (clumpParameters.Count > 0)
            {
                if (clumpParametersArray == null || clumpParametersArray.Length != clumpParameters.Count)
                {
                    clumpParametersArray = new ClumpParameters[clumpParameters.Count];
                }
                clumpParameters.CopyTo(clumpParametersArray);
                clumpParametersBuffer.SetData(clumpParametersArray);
            }
        }

        private void SetupComputeShaderForTile(Tile tile)
        {
            Terrain terrain = tile.terrain;

            if (tile.spaceMultiplier == 1)
            {
                computeShader.SetFloat(distanceCullStartDistID, distanceCullStartDisLOD0);
                computeShader.SetFloat(distanceCullEndDistID, distanceCullEndDisLOD0);
                computeShader.SetFloat(distanceCullMinimumGrassAmountlID, 0.25f);
            }
            else
            {
                computeShader.SetFloat(distanceCullStartDistID, distanceCullStartDisLOD1);
                computeShader.SetFloat(distanceCullEndDistID, distanceCullEndDisLOD1);
                computeShader.SetFloat(distanceCullMinimumGrassAmountlID, 0);
            }

            computeShader.SetInt(resolutionXID, tileResolution / tile.xResolutionDivisor);
            computeShader.SetInt(resolutionYID, tileResolution / tile.zResolutionDivisor);
            computeShader.SetBuffer(0, grassBladesBufferID, grassBladesBuffer);

            float adjustedGrassSpacing = grassSpacing * tile.spaceMultiplier;
            computeShader.SetFloat(grassSpacingID, adjustedGrassSpacing);
            computeShader.SetFloat(jitterStrengthID, jitterStrength);
            computeShader.SetVector(tilePositionID, tile.bounds.min);

            computeShader.SetVector(terrainPositionID, terrain.transform.position);
            computeShader.SetTexture(0, heightMapID, terrain.terrainData.heightmapTexture);
            if (terrain.terrainData.alphamapTextures.Length > 0)
            {
                computeShader.SetTexture(0, detailMapID, terrain.terrainData.alphamapTextures[0]);
            }

            computeShader.SetFloat(heightMapScaleID, terrain.terrainData.size.x);
            computeShader.SetFloat(heightMapMultiplierID, terrain.terrainData.size.y);

            
            computeShader.SetFloat(frustumCullNearOffsetID, frustumCullNearOffset);
            computeShader.SetFloat(frustumCullEdgeOffsetID, frustumCullEdgeOffset);
            computeShader.SetFloat(maxSlopeAngleID, maxSlopeAngle);
            computeShader.SetFloat(noiseThresholdID, noiseThreshold);
            computeShader.SetFloat(noiseScaleID, noiseScale);
            computeShader.SetVector(noiseOffsetID, noiseOffset);
            computeShader.SetInt(noiseOctavesID, noiseOctaves);
            computeShader.SetFloat(noisePersistenceID, noisePersistence);
            computeShader.SetFloat(noiseLacunarityID, noiseLacunarity);

            UpdateClumpParametersBuffer();
            computeShader.SetBuffer(0, clumpParametersID, clumpParametersBuffer);
            computeShader.SetTexture(0, clumpTexID, clumpTexture);
            computeShader.SetFloat(clumpScaleID, clumpScale);
            computeShader.SetFloat(numClumpParametersID, clumpParameters.Count);

            computeShader.SetTexture(0, LocalWindTexID, localWindTex);
            computeShader.SetFloat(LocalWindScaleID, localWindScale);
            computeShader.SetFloat(LocalWindSpeedID, localWindSpeed);
            computeShader.SetFloat(LocalWindStrengthID, localWindStrength);
            computeShader.SetFloat(LocalWindRotateAmountID, localWindRotateAmount);
        }

        private void RenderGrass()
        {
            ComputeBuffer.CopyCount(grassBladesBuffer, argsBuffer, sizeof(int)); // Get grass blade count from compute shader.

            // Render grass using procedural indirect draw, utilizing compute buffer for instances.
            Graphics.DrawProceduralIndirect(material, bounds, MeshTopology.Triangles, argsBuffer,
                0, null, null, UnityEngine.Rendering.ShadowCastingMode.Off, true, gameObject.layer);
        }

        private void DisposeBuffers()
        {
            DisposeBuffer(grassBladesBuffer);
            DisposeBuffer(meshTrianglesBuffer);
            DisposeBuffer(meshColorsBuffer);
            DisposeBuffer(meshUvsBuffer);
            DisposeBuffer(argsBuffer);
            DisposeBuffer(clumpParametersBuffer);
        }

        private void DisposeBuffer(ComputeBuffer buffer)
        {
            if (buffer != null)
            {
                buffer.Dispose();
                buffer = null;
            }
        }

        private void DestroyClumpTexture()
        {
            if (clumpTexture != null)
            {
                Destroy(clumpTexture);
                clumpTexture = null;
            }
        }

        private void OnDrawGizmos()
        {
            if (visibleTiles == null) return;

            Color gizmoColor = Color.cyan;
            gizmoColor.a = 0.5f;
            Gizmos.color = gizmoColor;

            foreach (Tile tile in visibleTiles)
            {
                Gizmos.DrawWireCube(tile.bounds.center, tile.bounds.size);
            }
        }
    }
}