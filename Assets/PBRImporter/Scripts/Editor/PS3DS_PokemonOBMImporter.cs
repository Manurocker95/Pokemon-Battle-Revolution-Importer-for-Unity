#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.IO;

namespace VirtualPhenix.PokemonSnap3DS
{
    /// <summary>
    /// Made for Pokémon Snap 3DS to import more stuff :D
    /// </summary>
    public class PS3DS_PokemonOBMImporter : EditorWindow
    {
        private string m_obmFilePath = "";
        private string m_obmFolderPath = "";
        private string m_tgaFolderPath = "";
        private bool m_importFolder = false;
        private bool m_flipV = false;
        private float m_boundExpand = 20f;
        private bool m_convertTgaToPng = true;
        private string m_outputFolder = "Assets/PS3DS_OBM_Imported";

        [MenuItem("PS3DS/Battle Revolution/Import Pokemon OBM")]
        public static void Open()
        {
            GetWindow<PS3DS_PokemonOBMImporter>("PS3DS OBM Importer");
        }

        private void OnGUI()
        {
            GUILayout.Label("Pokémon Battle Revolution - OBM Importer", EditorStyles.boldLabel);

            m_importFolder = EditorGUILayout.Toggle("Import Folder", m_importFolder);

            if (m_importFolder)
            {
                EditorGUILayout.BeginHorizontal();
                m_obmFolderPath = EditorGUILayout.TextField("OBM Folder", m_obmFolderPath);
                if (GUILayout.Button("...", GUILayout.Width(30)))
                    m_obmFolderPath = EditorUtility.OpenFolderPanel("Select OBM Folder", "", "");
                EditorGUILayout.EndHorizontal();
            }
            else
            {
                EditorGUILayout.BeginHorizontal();
                m_obmFilePath = EditorGUILayout.TextField("OBM File", m_obmFilePath);
                if (GUILayout.Button("...", GUILayout.Width(30)))
                    m_obmFilePath = EditorUtility.OpenFilePanel("Select OBM File", "", "obm");
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.BeginHorizontal();
            m_tgaFolderPath = EditorGUILayout.TextField("TGA Folder", m_tgaFolderPath);
            if (GUILayout.Button("...", GUILayout.Width(30)))
                m_tgaFolderPath = EditorUtility.OpenFolderPanel("Select TGA Folder", "", "");
            EditorGUILayout.EndHorizontal();
            m_convertTgaToPng = EditorGUILayout.Toggle("Convert TGA To PNG", m_convertTgaToPng);
            m_flipV = EditorGUILayout.Toggle("Flip V UV", m_flipV);
            m_outputFolder = EditorGUILayout.TextField("Output Folder", m_outputFolder);

            GUILayout.Space(10);
            m_boundExpand = EditorGUILayout.FloatField("Bound Expand", m_boundExpand);
            GUILayout.Space(10);

            if (GUILayout.Button("Import OBM"))
                Import();
        }

        private void Import()
        {
            EnsureFolder(m_outputFolder);

            if (m_importFolder)
            {
                if (string.IsNullOrEmpty(m_obmFolderPath) || !Directory.Exists(m_obmFolderPath))
                {
                    Debug.LogError("Select a valid OBM folder.");
                    return;
                }

                string[] files = Directory.GetFiles(m_obmFolderPath, "*.obm", SearchOption.TopDirectoryOnly);

                for (int i = 0; i < files.Length; i++)
                    ImportOBM(files[i], m_tgaFolderPath);
            }
            else
            {
                if (string.IsNullOrEmpty(m_obmFilePath) || !File.Exists(m_obmFilePath))
                {
                    Debug.LogError("Select a valid OBM file.");
                    return;
                }

                ImportOBM(m_obmFilePath, m_tgaFolderPath);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        private void ImportOBM(string obmPath, string tgaFolderPath)
        {
            PS3DS_OBMData data = ParseOBM(obmPath);
            if (data == null || data.Meshes.Count == 0)
            {
                Debug.LogError("Invalid OBM: " + obmPath);
                return;
            }

            string baseName = Path.GetFileNameWithoutExtension(obmPath);
            GameObject root = new GameObject(baseName);

            List<Transform> bones = CreateBones(data, root.transform);
            Matrix4x4[] bindposes = CreateBindposes(bones);

            for (int i = 0; i < data.Meshes.Count; i++)
                CreateMeshObject(data.Meshes[i], data, root.transform, bones, bindposes, tgaFolderPath, baseName, i);

            Selection.activeGameObject = root;
            Debug.Log("Imported OBM: " + obmPath);
        }

        private PS3DS_OBMData ParseOBM(string path)
        {
            PS3DS_OBMData data = new PS3DS_OBMData();
            PS3DS_OBMMesh currentMesh = null;
            PS3DS_OBMMaterialGroup currentGroup = null;
            PS3DS_OBMBone currentBone = null;

            string[] lines = File.ReadAllLines(path);

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.Length < 1 || line.StartsWith("#"))
                    continue;

                string[] parts = Split(line);
                if (parts.Length == 0)
                    continue;

                string cmd = parts[0];

                if (cmd == "o")
                {
                    currentMesh = new PS3DS_OBMMesh();
                    currentMesh.Name = GetName(parts, 1, "Mesh");
                    data.Meshes.Add(currentMesh);
                }
                else if (cmd == "v" && currentMesh != null)
                {
                    currentMesh.Vertices.Add(new Vector3(ParseF(parts, 1), ParseF(parts, 2), ParseF(parts, 3)));
                }
                else if (cmd == "vn" && currentMesh != null)
                {
                    currentMesh.Normals.Add(new Vector3(ParseF(parts, 1), ParseF(parts, 2), ParseF(parts, 3)));
                }
                else if (cmd == "vt" && currentMesh != null)
                {
                    float u = ParseF(parts, 1);
                    float v = ParseF(parts, 2);
                    if (m_flipV)
                        v = 1f - v;

                    currentMesh.UVs.Add(new Vector2(u, v));
                }
                else if (cmd == "vw" && currentMesh != null)
                {
                    List<PS3DS_OBMWeight> weights = new List<PS3DS_OBMWeight>();

                    for (int w = 1; w < parts.Length; w++)
                    {
                        string[] wp = parts[w].Split('/');
                        if (wp.Length >= 2)
                        {
                            PS3DS_OBMWeight weight = new PS3DS_OBMWeight();
                            weight.BoneIndex = ParseI(wp[0]);
                            weight.Weight = ParseF(wp[1]);
                            weights.Add(weight);
                        }
                    }

                    currentMesh.Weights.Add(weights);
                }
                else if (cmd == "g" && currentMesh != null)
                {
                    currentGroup = new PS3DS_OBMMaterialGroup();
                    currentGroup.Name = GetName(parts, 1, "Material");
                    currentMesh.MaterialGroups.Add(currentGroup);
                }
                else if (cmd == "fc" && currentGroup != null)
                {
                    currentGroup.Color = new Color(
                        ParseF(parts, 1) / 255f,
                        ParseF(parts, 2) / 255f,
                        ParseF(parts, 3) / 255f,
                        ParseF(parts, 4) / 255f);
                }
                else if (cmd == "ft" && currentGroup != null)
                {
                    string texName = GetName(parts, 1, "");
                    if (!string.IsNullOrEmpty(texName))
                        currentGroup.TextureNames.Add(texName);
                }
                else if (cmd == "f" && currentMesh != null)
                {
                    PS3DS_OBMFace face = ParseFace(parts);
                    if (face != null)
                    {
                        face.MaterialGroupIndex = Mathf.Max(0, currentMesh.MaterialGroups.Count - 1);
                        currentMesh.Faces.Add(face);

                        if (currentGroup != null)
                            currentGroup.FaceIndices.Add(currentMesh.Faces.Count - 1);
                    }
                }
                else if (cmd == "r")
                {
                    data.ArmatureName = GetName(parts, 1, "Armature");
                }
                else if (cmd == "rb")
                {
                    currentBone = new PS3DS_OBMBone();
                    currentBone.Name = GetName(parts, 1, "Bone_" + data.Bones.Count);
                    currentBone.ParentName = "";
                    currentBone.Rotation = Quaternion.identity;
                    currentBone.Position = Vector3.zero;
                    data.Bones.Add(currentBone);
                }
                else if (cmd == "rx" && currentBone != null)
                {
                    currentBone.ParentName = GetName(parts, 1, "");
                }
                else if (cmd == "rp" && currentBone != null)
                {
                    currentBone.Position = new Vector3(ParseF(parts, 1), ParseF(parts, 2), ParseF(parts, 3));
                }
                else if (cmd == "rq" && currentBone != null)
                {
                    currentBone.Rotation = new Quaternion(ParseF(parts, 1), ParseF(parts, 2), ParseF(parts, 3), ParseF(parts, 4));
                }
                else if (cmd == "rm" && currentBone != null)
                {
                    currentBone.HasMatrix = true;
                    currentBone.Matrix = new float[9];

                    for (int m = 0; m < 9; m++)
                        currentBone.Matrix[m] = ParseF(parts, m + 1);
                }
            }

            return data;
        }

        private PS3DS_OBMFace ParseFace(string[] parts)
        {
            if (parts.Length < 4)
                return null;

            PS3DS_OBMFace face = new PS3DS_OBMFace();

            for (int i = 1; i <= 3; i++)
            {
                string[] idx = parts[i].Split('/');

                face.V[i - 1] = idx.Length > 0 ? ParseI(idx[0]) : 0;
                face.N[i - 1] = idx.Length > 1 ? ParseI(idx[1]) : -1;
                face.T[i - 1] = idx.Length > 2 ? ParseI(idx[2]) : -1;
                face.W[i - 1] = idx.Length > 3 ? ParseI(idx[3]) : -1;
            }

            return face;
        }

        private List<Transform> CreateBones(PS3DS_OBMData data, Transform root)
        {
            List<Transform> result = new List<Transform>();
            Dictionary<string, Transform> map = new Dictionary<string, Transform>();

            GameObject armatureGO = new GameObject(string.IsNullOrEmpty(data.ArmatureName) ? "Armature" : data.ArmatureName);
            armatureGO.transform.parent = root;
            armatureGO.transform.localPosition = Vector3.zero;
            armatureGO.transform.localRotation = Quaternion.identity;
            armatureGO.transform.localScale = Vector3.one;

            for (int i = 0; i < data.Bones.Count; i++)
            {
                PS3DS_OBMBone bone = data.Bones[i];

                GameObject boneGO = new GameObject(bone.Name);
                Transform tr = boneGO.transform;

                tr.parent = armatureGO.transform;
                tr.localPosition = bone.Position;
                tr.localRotation = bone.Rotation;
                tr.localScale = Vector3.one;

                result.Add(tr);
                map[bone.Name] = tr;
            }

            for (int i = 0; i < data.Bones.Count; i++)
            {
                PS3DS_OBMBone bone = data.Bones[i];

                if (!string.IsNullOrEmpty(bone.ParentName) && map.ContainsKey(bone.ParentName))
                    result[i].parent = map[bone.ParentName];
            }

            return result;
        }

        private Matrix4x4[] CreateBindposes(List<Transform> bones)
        {
            Matrix4x4[] bindposes = new Matrix4x4[bones.Count];

            for (int i = 0; i < bones.Count; i++)
                bindposes[i] = bones[i].worldToLocalMatrix;

            return bindposes;
        }

        private void CreateMeshObject(
            PS3DS_OBMMesh src,
            PS3DS_OBMData data,
            Transform root,
            List<Transform> bones,
            Matrix4x4[] bindposes,
            string tgaFolderPath,
            string baseName,
            int meshIndex)
        {
            GameObject go = new GameObject(src.Name);
            go.transform.parent = root;
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;

            Mesh mesh = BuildUnityMesh(src, bones.Count);
            mesh.bindposes = bindposes;

            string meshFolder = m_outputFolder + "/" + baseName;
            EnsureFolder(meshFolder);

            string meshPath = meshFolder + "/" + src.Name + "_" + meshIndex + ".asset";
            AssetDatabase.CreateAsset(mesh, AssetDatabase.GenerateUniqueAssetPath(meshPath));

            SkinnedMeshRenderer smr = go.AddComponent<SkinnedMeshRenderer>();
            smr.sharedMesh = mesh;

            if (bones.Count > 0)
            {
                smr.bones = bones.ToArray();
                smr.rootBone = bones[0];
            }

            Bounds bounds = mesh.bounds;
            bounds.Expand(m_boundExpand);
            smr.localBounds = bounds;
            smr.updateWhenOffscreen = true;

            smr.sharedMaterials = CreateMaterials(src, tgaFolderPath, meshFolder, baseName);
        }

        private Mesh BuildUnityMesh(PS3DS_OBMMesh src, int boneCount)
        {
            Mesh mesh = new Mesh();
            mesh.name = src.Name;

            List<Vector3> vertices = new List<Vector3>();
            List<Vector3> normals = new List<Vector3>();
            List<Vector2> uvs = new List<Vector2>();
            List<BoneWeight> boneWeights = new List<BoneWeight>();

            List<int>[] submeshTriangles = new List<int>[Mathf.Max(1, src.MaterialGroups.Count)];
            for (int i = 0; i < submeshTriangles.Length; i++)
                submeshTriangles[i] = new List<int>();

            for (int f = 0; f < src.Faces.Count; f++)
            {
                PS3DS_OBMFace face = src.Faces[f];
                int submesh = Mathf.Clamp(face.MaterialGroupIndex, 0, submeshTriangles.Length - 1);

                for (int c = 0; c < 3; c++)
                {
                    int vertexIndex = face.V[c];
                    int normalIndex = face.N[c];
                    int uvIndex = face.T[c];
                    int weightIndex = face.W[c];

                    vertices.Add(GetVector3(src.Vertices, vertexIndex));
                    normals.Add(normalIndex >= 0 ? GetVector3(src.Normals, normalIndex) : Vector3.up);
                    uvs.Add(uvIndex >= 0 ? GetVector2(src.UVs, uvIndex) : Vector2.zero);
                    boneWeights.Add(CreateBoneWeight(src, weightIndex, boneCount));

                    submeshTriangles[submesh].Add(vertices.Count - 1);
                }
            }

            mesh.vertices = vertices.ToArray();
            mesh.normals = normals.ToArray();
            mesh.uv = uvs.ToArray();
            mesh.boneWeights = boneWeights.ToArray();

            mesh.subMeshCount = submeshTriangles.Length;
            for (int i = 0; i < submeshTriangles.Length; i++)
                mesh.SetTriangles(submeshTriangles[i].ToArray(), i);

            if (normals.Count == 0)
                mesh.RecalculateNormals();

            mesh.RecalculateBounds();
            return mesh;
        }

        private BoneWeight CreateBoneWeight(PS3DS_OBMMesh src, int weightIndex, int boneCount)
        {
            BoneWeight bw = new BoneWeight();
            bw.boneIndex0 = 0;
            bw.weight0 = 1f;

            if (weightIndex < 0 || weightIndex >= src.Weights.Count || boneCount <= 0)
                return bw;

            List<PS3DS_OBMWeight> weights = src.Weights[weightIndex];
            weights.Sort(SortWeights);

            if (weights.Count > 0)
            {
                bw.boneIndex0 = Mathf.Clamp(weights[0].BoneIndex, 0, boneCount - 1);
                bw.weight0 = weights[0].Weight;
            }

            if (weights.Count > 1)
            {
                bw.boneIndex1 = Mathf.Clamp(weights[1].BoneIndex, 0, boneCount - 1);
                bw.weight1 = weights[1].Weight;
            }

            if (weights.Count > 2)
            {
                bw.boneIndex2 = Mathf.Clamp(weights[2].BoneIndex, 0, boneCount - 1);
                bw.weight2 = weights[2].Weight;
            }

            if (weights.Count > 3)
            {
                bw.boneIndex3 = Mathf.Clamp(weights[3].BoneIndex, 0, boneCount - 1);
                bw.weight3 = weights[3].Weight;
            }

            float total = bw.weight0 + bw.weight1 + bw.weight2 + bw.weight3;
            if (total > 0.0001f)
            {
                bw.weight0 /= total;
                bw.weight1 /= total;
                bw.weight2 /= total;
                bw.weight3 /= total;
            }

            return bw;
        }

        private static int SortWeights(PS3DS_OBMWeight a, PS3DS_OBMWeight b)
        {
            if (a.Weight < b.Weight)
                return 1;

            if (a.Weight > b.Weight)
                return -1;

            return 0;
        }

        private Material[] CreateMaterials(PS3DS_OBMMesh src, string tgaFolderPath, string meshFolder, string baseName)
        {
            int count = Mathf.Max(1, src.MaterialGroups.Count);
            Material[] mats = new Material[count];

            for (int i = 0; i < count; i++)
            {
                PS3DS_OBMMaterialGroup group = i < src.MaterialGroups.Count ? src.MaterialGroups[i] : null;

                string groupName = group != null ? group.Name : "Material";
                if (string.IsNullOrEmpty(groupName))
                    groupName = "Material";

                Texture2D tex = null;

                if (group != null && group.TextureNames != null && group.TextureNames.Count > 0)
                    tex = LoadOrConvertTexture(group.TextureNames[0], tgaFolderPath, meshFolder);

                Material mat = new Material(Shader.Find("Standard"));
                mat.name = groupName;

                if (group != null)
                    mat.color = group.Color;

                if (tex != null)
                    mat.mainTexture = tex;

                string safeMatName = baseName + "_" + src.Name + "_" + i + "_" + groupName;
                string matPath = meshFolder + "/" + SanitizeFileName(safeMatName) + ".mat";
                matPath = AssetDatabase.GenerateUniqueAssetPath(matPath);

                AssetDatabase.CreateAsset(mat, matPath);

                Material savedMat = AssetDatabase.LoadAssetAtPath(matPath, typeof(Material)) as Material;

                if (savedMat != null)
                    mats[i] = savedMat;
                else
                    mats[i] = mat;
            }

            return mats;
        }

        private static string SanitizeFileName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return "unnamed";

            char[] invalid = Path.GetInvalidFileNameChars();

            for (int i = 0; i < invalid.Length; i++)
                name = name.Replace(invalid[i].ToString(), "_");

            name = name.Replace(" ", "_");
            name = name.Replace("/", "_");
            name = name.Replace("\\", "_");
            name = name.Replace(":", "_");

            return name;
        }

        private Texture2D LoadOrConvertTexture(string texName, string tgaFolderPath, string outputFolder)
        {
            if (string.IsNullOrEmpty(texName) || string.IsNullOrEmpty(tgaFolderPath))
                return null;

            string cleanName = Path.GetFileNameWithoutExtension(texName);
            string tgaPath = FindExternalFileInsensitive(tgaFolderPath, cleanName, ".tga");

            if (string.IsNullOrEmpty(tgaPath))
            {
                Debug.LogWarning("TGA not found: " + texName);
                return null;
            }

            string copiedTgaPath = outputFolder + "/" + cleanName + ".tga";
            copiedTgaPath = AssetDatabase.GenerateUniqueAssetPath(copiedTgaPath);

            File.Copy(tgaPath, copiedTgaPath, true);
            AssetDatabase.ImportAsset(copiedTgaPath, ImportAssetOptions.ForceUpdate);

            TextureImporter importer = AssetImporter.GetAtPath(copiedTgaPath) as TextureImporter;
            if (importer != null)
            {
                importer.textureType = TextureImporterType.Default;
                importer.isReadable = true;
                importer.mipmapEnabled = false;
                importer.npotScale = TextureImporterNPOTScale.None;
                importer.alphaIsTransparency = true;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.filterMode = FilterMode.Point;

                AssetDatabase.ImportAsset(copiedTgaPath, ImportAssetOptions.ForceUpdate);
                AssetDatabase.Refresh();
            }

            Texture2D tga = AssetDatabase.LoadAssetAtPath(copiedTgaPath, typeof(Texture2D)) as Texture2D;

            if (tga == null)
            {
                Debug.LogWarning("Unity could not import TGA: " + copiedTgaPath);
                return null;
            }

            if (!m_convertTgaToPng)
                return tga;

            Texture2D readableCopy = MakeReadableCopy(tga);

            if (readableCopy == null)
            {
                Debug.LogWarning("Could not make readable copy, using TGA directly: " + copiedTgaPath);
                return tga;
            }

            string pngPath = outputFolder + "/" + cleanName + ".png";
            pngPath = AssetDatabase.GenerateUniqueAssetPath(pngPath);

            byte[] pngBytes = readableCopy.EncodeToPNG();

            if (pngBytes == null || pngBytes.Length == 0)
            {
                Debug.LogWarning("Could not encode PNG, using TGA directly: " + copiedTgaPath);
                return tga;
            }

            File.WriteAllBytes(pngPath, pngBytes);

            AssetDatabase.ImportAsset(pngPath, ImportAssetOptions.ForceUpdate);

            TextureImporter pngImporter = AssetImporter.GetAtPath(pngPath) as TextureImporter;
            if (pngImporter != null)
            {
                pngImporter.textureType = TextureImporterType.Default;
                pngImporter.isReadable = true;
                pngImporter.mipmapEnabled = false;
                pngImporter.npotScale = TextureImporterNPOTScale.None;
                pngImporter.alphaIsTransparency = true;
                pngImporter.wrapMode = TextureWrapMode.Clamp;
                pngImporter.filterMode = FilterMode.Point;

                AssetDatabase.ImportAsset(pngPath, ImportAssetOptions.ForceUpdate);
            }

            Texture2D pngTex = AssetDatabase.LoadAssetAtPath(pngPath, typeof(Texture2D)) as Texture2D;

            if (pngTex != null)
                return pngTex;

            return tga;
        }

        private Texture2D MakeReadableCopy(Texture2D source)
        {
            if (source == null)
                return null;

            RenderTexture rt = RenderTexture.GetTemporary(
                source.width,
                source.height,
                0,
                RenderTextureFormat.ARGB32);

            RenderTexture previous = RenderTexture.active;

            Graphics.Blit(source, rt);
            RenderTexture.active = rt;

            Texture2D readable = new Texture2D(source.width, source.height, TextureFormat.ARGB32, false);
            readable.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
            readable.Apply(false, false);

            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(rt);

            return readable;
        }

        private string FindExternalFileInsensitive(string folder, string fileNameNoExt, string ext)
        {
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
                return "";

            string[] files = Directory.GetFiles(folder, "*" + ext, SearchOption.TopDirectoryOnly);

            for (int i = 0; i < files.Length; i++)
            {
                string n = Path.GetFileNameWithoutExtension(files[i]);

                if (string.Equals(n, fileNameNoExt, System.StringComparison.OrdinalIgnoreCase))
                    return files[i].Replace("\\", "/");
            }

            return "";
        }

        private string FindFileInsensitive(string folder, string fileNameNoExt, string ext)
        {
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
                return "";

            string[] files = Directory.GetFiles(folder, "*" + ext, SearchOption.TopDirectoryOnly);

            for (int i = 0; i < files.Length; i++)
            {
                string n = Path.GetFileNameWithoutExtension(files[i]);
                if (string.Equals(n, fileNameNoExt, System.StringComparison.OrdinalIgnoreCase))
                    return files[i].Replace("\\", "/");
            }

            return "";
        }

        private static string[] Split(string line)
        {
            return line.Split(new char[] { ' ', '\t' }, System.StringSplitOptions.RemoveEmptyEntries);
        }

        private static string GetName(string[] parts, int index, string fallback)
        {
            if (parts.Length > index)
                return parts[index];

            return fallback;
        }

        private static float ParseF(string[] parts, int index)
        {
            if (parts.Length <= index)
                return 0f;

            return ParseF(parts[index]);
        }

        private static float ParseF(string value)
        {
            float f = 0f;
            float.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out f);
            return f;
        }

        private static int ParseI(string value)
        {
            int i = 0;
            int.TryParse(value, out i);
            return i;
        }

        private static Vector3 GetVector3(List<Vector3> list, int index)
        {
            if (index >= 0 && index < list.Count)
                return list[index];

            return Vector3.zero;
        }

        private static Vector2 GetVector2(List<Vector2> list, int index)
        {
            if (index >= 0 && index < list.Count)
                return list[index];

            return Vector2.zero;
        }

        private static void EnsureFolder(string path)
        {
            path = path.Replace("\\", "/");

            if (AssetDatabase.IsValidFolder(path))
                return;

            string[] parts = path.Split('/');
            string current = parts[0];

            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];

                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[i]);

                current = next;
            }
        }

        private class PS3DS_OBMData
        {
            public string ArmatureName = "Armature";
            public List<PS3DS_OBMMesh> Meshes = new List<PS3DS_OBMMesh>();
            public List<PS3DS_OBMBone> Bones = new List<PS3DS_OBMBone>();
        }

        private class PS3DS_OBMMesh
        {
            public string Name;
            public List<Vector3> Vertices = new List<Vector3>();
            public List<Vector3> Normals = new List<Vector3>();
            public List<Vector2> UVs = new List<Vector2>();
            public List<List<PS3DS_OBMWeight>> Weights = new List<List<PS3DS_OBMWeight>>();
            public List<PS3DS_OBMFace> Faces = new List<PS3DS_OBMFace>();
            public List<PS3DS_OBMMaterialGroup> MaterialGroups = new List<PS3DS_OBMMaterialGroup>();
        }

        private class PS3DS_OBMBone
        {
            public string Name;
            public string ParentName;
            public Vector3 Position;
            public Quaternion Rotation;
            public bool HasMatrix;
            public float[] Matrix;
        }

        private class PS3DS_OBMWeight
        {
            public int BoneIndex;
            public float Weight;
        }

        private class PS3DS_OBMFace
        {
            public int[] V = new int[3];
            public int[] N = new int[3];
            public int[] T = new int[3];
            public int[] W = new int[3];
            public int MaterialGroupIndex;
        }

        private class PS3DS_OBMMaterialGroup
        {
            public string Name;
            public List<string> TextureNames = new List<string>();
            public Color Color = Color.white;
            public List<int> FaceIndices = new List<int>();
        }
    }
}
#endif