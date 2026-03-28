using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace ErenshorSkills.Stations
{
    /// <summary>
    /// Runtime OBJ file loader. Parses .obj files and creates Unity Mesh objects.
    /// Supports vertices, normals, UVs, and textured materials via .png files.
    /// </summary>
    public static class ObjLoader
    {
        /// <summary>
        /// Load an OBJ file and create a GameObject with mesh and texture.
        /// </summary>
        public static GameObject Load(string objPath, string texturePath = null)
        {
            if (!File.Exists(objPath))
            {
                SkillsPlugin.Log.LogWarning($"ObjLoader: File not found: {objPath}");
                return null;
            }

            try
            {
                var lines = File.ReadAllLines(objPath);
                var positions = new List<Vector3>();
                var normals = new List<Vector3>();
                var uvs = new List<Vector2>();
                var triVerts = new List<Vector3>();
                var triNorms = new List<Vector3>();
                var triUVs = new List<Vector2>();
                var triIndices = new List<int>();

                foreach (var rawLine in lines)
                {
                    var line = rawLine.Trim();
                    if (line.Length == 0 || line[0] == '#') continue;

                    var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length < 2) continue;

                    if (parts[0] == "v" && parts.Length >= 4)
                    {
                        positions.Add(new Vector3(
                            P(parts[1]), P(parts[2]), P(parts[3])));
                    }
                    else if (parts[0] == "vn" && parts.Length >= 4)
                    {
                        normals.Add(new Vector3(
                            P(parts[1]), P(parts[2]), P(parts[3])));
                    }
                    else if (parts[0] == "vt" && parts.Length >= 3)
                    {
                        uvs.Add(new Vector2(P(parts[1]), P(parts[2])));
                    }
                    else if (parts[0] == "f")
                    {
                        // Triangulate face (supports quads and n-gons)
                        var faceVerts = new List<int[]>();
                        for (int i = 1; i < parts.Length; i++)
                        {
                            faceVerts.Add(ParseFaceVertex(parts[i]));
                        }
                        // Fan triangulation
                        for (int i = 1; i < faceVerts.Count - 1; i++)
                        {
                            AddVertex(faceVerts[0], positions, normals, uvs,
                                triVerts, triNorms, triUVs, triIndices);
                            AddVertex(faceVerts[i], positions, normals, uvs,
                                triVerts, triNorms, triUVs, triIndices);
                            AddVertex(faceVerts[i + 1], positions, normals, uvs,
                                triVerts, triNorms, triUVs, triIndices);
                        }
                    }
                }

                // Build Unity mesh
                var mesh = new Mesh();
                mesh.name = Path.GetFileNameWithoutExtension(objPath);
                if (triVerts.Count > 65535)
                    mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                mesh.SetVertices(triVerts);
                if (triNorms.Count == triVerts.Count) mesh.SetNormals(triNorms);
                if (triUVs.Count == triVerts.Count) mesh.SetUVs(0, triUVs);

                // Reverse triangle winding for Unity's left-handed coords
                // OBJ files use right-handed winding — without this, faces are inside-out
                for (int i = 0; i < triIndices.Count; i += 3)
                {
                    int tmp = triIndices[i];
                    triIndices[i] = triIndices[i + 2];
                    triIndices[i + 2] = tmp;
                }

                mesh.SetTriangles(triIndices, 0);
                if (triNorms.Count != triVerts.Count) mesh.RecalculateNormals();
                mesh.RecalculateBounds();

                // Create GameObject
                var go = new GameObject(mesh.name);
                var mf = go.AddComponent<MeshFilter>();
                mf.mesh = mesh;
                var mr = go.AddComponent<MeshRenderer>();

                // Load texture if provided — use double-sided rendering
                var mat = new Material(Shader.Find("Standard"));
                mat.SetFloat("_Cull", 0); // Cull Off = render both sides
                if (!string.IsNullOrEmpty(texturePath) && File.Exists(texturePath))
                {
                    var texData = File.ReadAllBytes(texturePath);
                    var tex = new Texture2D(2, 2);
                    tex.LoadImage(texData);
                    tex.filterMode = FilterMode.Bilinear;
                    mat.mainTexture = tex;
                }
                mr.material = mat;

                // Add collider for interaction
                var bc = go.AddComponent<BoxCollider>();
                bc.center = mesh.bounds.center;
                bc.size = mesh.bounds.size;

                SkillsPlugin.Log.LogInfo(
                    $"ObjLoader: Loaded {mesh.name} ({triVerts.Count} verts, {triIndices.Count / 3} tris)");
                return go;
            }
            catch (Exception ex)
            {
                SkillsPlugin.Log.LogError($"ObjLoader: Failed to load {objPath}: {ex.Message}");
                return null;
            }
        }

        static float P(string s) => float.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);

        static int[] ParseFaceVertex(string s)
        {
            // Formats: v, v/vt, v/vt/vn, v//vn
            var parts = s.Split('/');
            int v = int.Parse(parts[0]) - 1; // OBJ is 1-indexed
            int vt = parts.Length > 1 && parts[1].Length > 0 ? int.Parse(parts[1]) - 1 : -1;
            int vn = parts.Length > 2 && parts[2].Length > 0 ? int.Parse(parts[2]) - 1 : -1;
            return new[] { v, vt, vn };
        }

        static void AddVertex(int[] face, List<Vector3> positions,
            List<Vector3> normals, List<Vector2> uvs,
            List<Vector3> triVerts, List<Vector3> triNorms,
            List<Vector2> triUVs, List<int> triIndices)
        {
            int idx = triVerts.Count;
            triIndices.Add(idx);

            // Position (flip X for Unity's left-handed coords)
            var p = positions[face[0]];
            triVerts.Add(new Vector3(-p.x, p.y, p.z));

            // UV
            if (face[1] >= 0 && face[1] < uvs.Count)
                triUVs.Add(uvs[face[1]]);
            else
                triUVs.Add(Vector2.zero);

            // Normal
            if (face[2] >= 0 && face[2] < normals.Count)
            {
                var n = normals[face[2]];
                triNorms.Add(new Vector3(-n.x, n.y, n.z));
            }
        }
    }
}
