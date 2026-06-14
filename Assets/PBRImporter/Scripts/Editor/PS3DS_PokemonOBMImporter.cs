#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using System.Collections.Generic;
using System.IO;
using System.Text;

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
        private string m_sdrFilePath = "";
        private bool m_importFolder = false;
        private bool m_importMatchingSDR = false;
        private bool m_createMecanimController = true;
        private bool m_useVertexIndexAsWeightFallback = true;
        private bool m_debugSkinningStats = true;
        private bool m_exportSkinningDebugCsv = true;
        private bool m_applyDebugBoneColors = true;
        private int m_debugLogWeightSamples = 32;
        private float m_sdrFrameRate = 30f;
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
            GUILayout.Label("SDR / OUT Animation Import", EditorStyles.boldLabel);
            m_importMatchingSDR = EditorGUILayout.Toggle("Auto Import Matching SDR", m_importMatchingSDR);
            m_createMecanimController = EditorGUILayout.Toggle("Create Mecanim Controller", m_createMecanimController);
            m_useVertexIndexAsWeightFallback = EditorGUILayout.Toggle("VW Fallback Uses Vertex Index", m_useVertexIndexAsWeightFallback);
            m_debugSkinningStats = EditorGUILayout.Toggle("Debug Skinning Stats", m_debugSkinningStats);
            m_exportSkinningDebugCsv = EditorGUILayout.Toggle("Export Skinning CSV", m_exportSkinningDebugCsv);
            m_applyDebugBoneColors = EditorGUILayout.Toggle("Apply Debug Bone Colors", m_applyDebugBoneColors);
            m_debugLogWeightSamples = EditorGUILayout.IntField("Debug VW Samples", m_debugLogWeightSamples);
            m_sdrFrameRate = EditorGUILayout.FloatField("SDR Frame Rate", m_sdrFrameRate);

            EditorGUILayout.BeginHorizontal();
            m_sdrFilePath = EditorGUILayout.TextField("SDR/OUT File", m_sdrFilePath);
            if (GUILayout.Button("...", GUILayout.Width(30)))
                m_sdrFilePath = EditorUtility.OpenFilePanel("Select SDR/OUT File", "", "sdr,out");
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(10);

            if (GUILayout.Button("Import All (OBM + SDR/OUT + TGA)"))
                ImportAll();

            if (GUILayout.Button("Import OBM"))
                Import();

            if (GUILayout.Button("Import SDR To Selected OBM Root"))
                ImportSDRToSelected();

            if (GUILayout.Button("Import SDR Only / Create Armature"))
                ImportSDROnly();
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

        private void ImportAll()
        {
            EnsureFolder(m_outputFolder);

            if (string.IsNullOrEmpty(m_obmFilePath) || !File.Exists(m_obmFilePath))
            {
                Debug.LogError("Select a valid OBM file.");
                return;
            }

            if (string.IsNullOrEmpty(m_sdrFilePath) || !File.Exists(m_sdrFilePath))
            {
                Debug.LogError("Select a valid SDR/OUT file.");
                return;
            }

            PS3DS_SDRData sdr = ParseSDR(m_sdrFilePath);
            GameObject root = ImportOBMInternal(m_obmFilePath, m_tgaFolderPath, sdr);

            if (root != null)
            {
                ImportSDRAnimationsFromData(sdr, m_sdrFilePath, root);
                Selection.activeGameObject = root;
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        private GameObject ImportOBM(string obmPath, string tgaFolderPath)
        {
            PS3DS_SDRData sdr = null;

            if (m_importMatchingSDR)
            {
                string matchingSDR = FindMatchingSDR(obmPath);
                if (!string.IsNullOrEmpty(matchingSDR))
                    sdr = ParseSDR(matchingSDR);
                else
                    Debug.LogWarning("Matching SDR/OUT not found for: " + obmPath);
            }

            GameObject root = ImportOBMInternal(obmPath, tgaFolderPath, sdr);

            if (root != null && sdr != null)
            {
                string matchingSDR = FindMatchingSDR(obmPath);
                ImportSDRAnimationsFromData(sdr, matchingSDR, root);
            }

            return root;
        }

        private GameObject ImportOBMInternal(string obmPath, string tgaFolderPath, PS3DS_SDRData sdr)
        {
            PS3DS_OBMData data = ParseOBM(obmPath);
            if (data == null || data.Meshes.Count == 0)
            {
                Debug.LogError("Invalid OBM: " + obmPath);
                return null;
            }

            if ((data.Bones == null || data.Bones.Count == 0) && sdr != null && sdr.Bones.Count > 0)
            {
                data.ArmatureName = string.IsNullOrEmpty(sdr.SkeletonName) ? "Armature" : sdr.SkeletonName;
                data.Bones = BuildOBMBonesFromSDR(sdr);
                Debug.Log("OBM has no bones. Using SDR skeleton: " + data.Bones.Count + " bones.");
            }

            string baseName = Path.GetFileNameWithoutExtension(obmPath);
            GameObject root = new GameObject(baseName);

            List<Transform> bones = CreateBones(data, root.transform);
            Matrix4x4[] bindposes = CreateBindposes(bones);

            for (int i = 0; i < data.Meshes.Count; i++)
                CreateMeshObject(data.Meshes[i], data, root.transform, bones, bindposes, tgaFolderPath, baseName, i);

            if (sdr != null && sdr.Bones.Count > 0)
                RebindSkinnedMeshesFromSDR(root, sdr);

            Selection.activeGameObject = root;
            Debug.Log("Imported OBM: " + obmPath);
            return root;
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
                    ParseVWLine(currentMesh, parts);
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
                    currentBone.Scale = Vector3.one;
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

        private void ParseVWLine(PS3DS_OBMMesh mesh, string[] parts)
        {
            if (mesh == null || parts == null || parts.Length < 2)
                return;

            int explicitVertexIndex = -1;
            int start = 1;

            // Supported formats:
            //   vw bone/weight bone/weight
            //   vw bone weight bone weight
            //   vw vertexIndex bone/weight bone/weight
            //   vw vertexIndex bone weight bone weight
            // Some OBM exporters write one vw per source vertex without referencing it from faces.
            // Others prefix the source vertex index explicitly. The old importer only accepted bone/weight,
            // so space-separated dumps produced empty weight lists and Unity attached everything to bone 0.
            if (parts.Length >= 3 && !parts[1].Contains("/"))
            {
                bool nextLooksPair = parts[2].Contains("/");
                bool remainingLooksNumericPairs = ((parts.Length - 2) % 2) == 0;

                if (nextLooksPair || remainingLooksNumericPairs)
                {
                    int possibleVertex = ParseI(parts[1]);
                    int maxReasonableVertex = mesh.Vertices != null ? mesh.Vertices.Count - 1 : -1;

                    if (possibleVertex >= 0 && (maxReasonableVertex < 0 || possibleVertex <= maxReasonableVertex))
                    {
                        explicitVertexIndex = possibleVertex;
                        start = 2;
                    }
                }
            }

            List<PS3DS_OBMWeight> weights = new List<PS3DS_OBMWeight>();

            for (int w = start; w < parts.Length; w++)
            {
                string token = parts[w];

                if (string.IsNullOrEmpty(token))
                    continue;

                int slash = token.IndexOf('/');
                if (slash >= 0)
                {
                    string boneText = token.Substring(0, slash);
                    string weightText = token.Substring(slash + 1);
                    AddParsedWeight(weights, boneText, weightText);
                }
                else if (w + 1 < parts.Length)
                {
                    AddParsedWeight(weights, parts[w], parts[w + 1]);
                    w++;
                }
            }

            if (explicitVertexIndex >= 0)
            {
                while (mesh.Weights.Count <= explicitVertexIndex)
                    mesh.Weights.Add(new List<PS3DS_OBMWeight>());

                mesh.Weights[explicitVertexIndex] = weights;
            }
            else
            {
                mesh.Weights.Add(weights);
            }
        }

        private void AddParsedWeight(List<PS3DS_OBMWeight> weights, string boneText, string weightText)
        {
            if (weights == null)
                return;

            PS3DS_OBMWeight weight = new PS3DS_OBMWeight();
            weight.BoneIndex = ParseI(boneText);
            weight.Weight = ParseF(weightText);

            // Be permissive with exporters that write byte/ushort/percent weights.
            // BoneWeight is normalized later, but this also keeps 0..255 and 0..65535 values readable.
            if (weight.Weight > 1.0001f)
            {
                if (weight.Weight <= 100f)
                    weight.Weight /= 100f;
                else if (weight.Weight <= 255f)
                    weight.Weight /= 255f;
                else
                    weight.Weight /= 65535f;
            }

            if (weight.Weight > 0.0001f)
                weights.Add(weight);
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

                tr.SetParent(armatureGO.transform, false);
                tr.localPosition = bone.Position;
                tr.localRotation = bone.Rotation;
                tr.localScale = bone.Scale == Vector3.zero ? Vector3.one : bone.Scale;

                result.Add(tr);
                map[bone.Name] = tr;
            }

            for (int i = 0; i < data.Bones.Count; i++)
            {
                PS3DS_OBMBone bone = data.Bones[i];

                if (!string.IsNullOrEmpty(bone.ParentName) && map.ContainsKey(bone.ParentName))
                    result[i].SetParent(map[bone.ParentName], false);
            }

            return result;
        }

        private Matrix4x4[] CreateBindposes(List<Transform> bones)
        {
            Matrix4x4[] bindposes = new Matrix4x4[bones.Count];

            Transform root = null;
            if (bones != null && bones.Count > 0 && bones[0] != null)
                root = bones[0].root;

            Matrix4x4 rootMatrix = root != null ? root.localToWorldMatrix : Matrix4x4.identity;
            for (int i = 0; i < bones.Count; i++)
                bindposes[i] = bones[i].worldToLocalMatrix * rootMatrix;

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
            string meshFolder = m_outputFolder + "/" + baseName;
            EnsureFolder(meshFolder);

            Mesh mesh = BuildUnityMesh(src, bones, meshFolder, baseName, meshIndex);
            mesh.bindposes = bindposes;

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

        private Mesh BuildUnityMesh(PS3DS_OBMMesh src, List<Transform> bones, string meshFolder, string baseName, int meshIndex)
        {
            int boneCount = bones != null ? bones.Count : 0;
            Mesh mesh = new Mesh();
            mesh.name = src.Name;

            List<Vector3> vertices = new List<Vector3>();
            List<Vector3> normals = new List<Vector3>();
            List<Vector2> uvs = new List<Vector2>();
            List<BoneWeight> boneWeights = new List<BoneWeight>();
            List<int> debugSourceVertexIndices = new List<int>();
            List<int> debugWeightIndices = new List<int>();
            List<int> debugDominantBoneIndices = new List<int>();

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

                    // Most PBR OBM dumps write one vw entry per source vertex and faces only as v/n/t.
                    // If the face does not contain an explicit weight index, the vertex index is the correct fallback.
                    if (weightIndex < 0 && m_useVertexIndexAsWeightFallback)
                        weightIndex = vertexIndex;

                    vertices.Add(GetVector3(src.Vertices, vertexIndex));
                    normals.Add(normalIndex >= 0 ? GetVector3(src.Normals, normalIndex) : Vector3.up);
                    uvs.Add(uvIndex >= 0 ? GetVector2(src.UVs, uvIndex) : Vector2.zero);
                    BoneWeight bw = CreateBoneWeight(src, weightIndex, boneCount);
                    boneWeights.Add(bw);
                    debugSourceVertexIndices.Add(vertexIndex);
                    debugWeightIndices.Add(weightIndex);
                    debugDominantBoneIndices.Add(GetDominantBoneIndex(bw));

                    submeshTriangles[submesh].Add(vertices.Count - 1);
                }
            }

            mesh.vertices = vertices.ToArray();
            mesh.normals = normals.ToArray();
            mesh.uv = uvs.ToArray();
            mesh.boneWeights = boneWeights.ToArray();

            if (m_applyDebugBoneColors)
                ApplyDebugBoneColors(mesh, debugDominantBoneIndices);

            if (m_debugSkinningStats)
                DebugSkinningStats(src, boneWeights, boneCount, bones, debugSourceVertexIndices, debugWeightIndices, meshFolder, baseName, meshIndex);

            mesh.subMeshCount = submeshTriangles.Length;
            for (int i = 0; i < submeshTriangles.Length; i++)
                mesh.SetTriangles(submeshTriangles[i].ToArray(), i);

            if (normals.Count == 0)
                mesh.RecalculateNormals();

            mesh.RecalculateBounds();
            return mesh;
        }

        private void DebugSkinningStats(
            PS3DS_OBMMesh src,
            List<BoneWeight> boneWeights,
            int boneCount,
            List<Transform> bones,
            List<int> sourceVertexIndices,
            List<int> weightIndices,
            string meshFolder,
            string baseName,
            int meshIndex)
        {
            if (src == null || boneWeights == null)
                return;

            int weighted = 0;
            int nonRoot = 0;
            int invalid = 0;
            int rootOnly = 0;
            int maxIndex = 0;
            int emptyWeightRows = 0;
            int nonEmptySourceRows = 0;

            if (src.Weights != null)
            {
                for (int i = 0; i < src.Weights.Count; i++)
                {
                    if (src.Weights[i] == null || src.Weights[i].Count == 0)
                        emptyWeightRows++;
                    else
                        nonEmptySourceRows++;
                }
            }

            for (int i = 0; i < boneWeights.Count; i++)
            {
                BoneWeight bw = boneWeights[i];
                float total = bw.weight0 + bw.weight1 + bw.weight2 + bw.weight3;

                if (total > 0.0001f)
                    weighted++;

                if ((bw.boneIndex0 != 0 && bw.weight0 > 0.0001f) ||
                    (bw.boneIndex1 != 0 && bw.weight1 > 0.0001f) ||
                    (bw.boneIndex2 != 0 && bw.weight2 > 0.0001f) ||
                    (bw.boneIndex3 != 0 && bw.weight3 > 0.0001f))
                    nonRoot++;

                if (bw.boneIndex0 >= boneCount || bw.boneIndex1 >= boneCount || bw.boneIndex2 >= boneCount || bw.boneIndex3 >= boneCount)
                    invalid++;

                if (bw.boneIndex0 == 0 && bw.weight0 > 0.999f && bw.weight1 < 0.0001f && bw.weight2 < 0.0001f && bw.weight3 < 0.0001f)
                    rootOnly++;

                maxIndex = Mathf.Max(maxIndex, bw.boneIndex0);
                maxIndex = Mathf.Max(maxIndex, bw.boneIndex1);
                maxIndex = Mathf.Max(maxIndex, bw.boneIndex2);
                maxIndex = Mathf.Max(maxIndex, bw.boneIndex3);
            }

            Debug.Log("Skinning stats for " + src.Name +
                      " | unityVertices=" + boneWeights.Count +
                      " | sourceVertices=" + (src.Vertices != null ? src.Vertices.Count : 0) +
                      " | source vw=" + (src.Weights != null ? src.Weights.Count : 0) +
                      " | nonEmptyVWRows=" + nonEmptySourceRows +
                      " | emptyVWRows=" + emptyWeightRows +
                      " | bones=" + boneCount +
                      " | weighted=" + weighted +
                      " | nonRootWeighted=" + nonRoot +
                      " | rootOnly=" + rootOnly +
                      " | maxBoneIndex=" + maxIndex +
                      " | invalid=" + invalid);

            DebugLogRawVWSamples(src, bones);

            if (m_exportSkinningDebugCsv)
                ExportSkinningDebugCsv(src, boneWeights, bones, sourceVertexIndices, weightIndices, meshFolder, baseName, meshIndex);

            if (nonRoot == 0 && boneWeights.Count > 0)
                Debug.LogWarning("All generated BoneWeights are attached only to bone 0. That means the parsed VW rows are empty/zero, the face fallback points to empty VW rows, or the OBM uses another skinning table format.");
        }

        private void DebugLogRawVWSamples(PS3DS_OBMMesh src, List<Transform> bones)
        {
            if (src == null || src.Weights == null || m_debugLogWeightSamples <= 0)
                return;

            int printed = 0;
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("First non-empty VW rows for " + src.Name + ":");

            for (int i = 0; i < src.Weights.Count && printed < m_debugLogWeightSamples; i++)
            {
                List<PS3DS_OBMWeight> row = src.Weights[i];
                if (row == null || row.Count == 0)
                    continue;

                sb.Append("vw[").Append(i).Append("] = ");
                for (int w = 0; w < row.Count; w++)
                {
                    int bi = row[w].BoneIndex;
                    sb.Append(bi).Append("(").Append(GetBoneName(bones, bi)).Append("):").Append(row[w].Weight.ToString("0.###"));
                    if (w + 1 < row.Count)
                        sb.Append(", ");
                }
                sb.AppendLine();
                printed++;
            }

            if (printed == 0)
                sb.AppendLine("No non-empty VW rows found. The OBM did not parse any usable vertex weights.");

            Debug.Log(sb.ToString());
        }

        private void ExportSkinningDebugCsv(
            PS3DS_OBMMesh src,
            List<BoneWeight> boneWeights,
            List<Transform> bones,
            List<int> sourceVertexIndices,
            List<int> weightIndices,
            string meshFolder,
            string baseName,
            int meshIndex)
        {
            if (string.IsNullOrEmpty(meshFolder))
                return;

            EnsureFolder(meshFolder);

            string safeName = SanitizeFileName(baseName + "_" + src.Name + "_" + meshIndex + "_SkinningDebug") + ".csv";
            string assetPath = AssetDatabase.GenerateUniqueAssetPath(meshFolder + "/" + safeName);
            string fullPath = ToAbsoluteProjectPath(assetPath);

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("unityVertex,sourceVertex,weightRow,dominantBone,dominantBoneName,bone0,bone0Name,weight0,bone1,bone1Name,weight1,bone2,bone2Name,weight2,bone3,bone3Name,weight3,sourcePosition");

            for (int i = 0; i < boneWeights.Count; i++)
            {
                BoneWeight bw = boneWeights[i];
                int dominant = GetDominantBoneIndex(bw);
                int sourceVertex = i < sourceVertexIndices.Count ? sourceVertexIndices[i] : -1;
                int weightRow = i < weightIndices.Count ? weightIndices[i] : -1;
                Vector3 pos = GetVector3(src.Vertices, sourceVertex);

                sb.Append(i).Append(',');
                sb.Append(sourceVertex).Append(',');
                sb.Append(weightRow).Append(',');
                sb.Append(dominant).Append(',');
                sb.Append(EscapeCsv(GetBoneName(bones, dominant))).Append(',');
                sb.Append(bw.boneIndex0).Append(',');
                sb.Append(EscapeCsv(GetBoneName(bones, bw.boneIndex0))).Append(',');
                sb.Append(bw.weight0.ToString("0.######")).Append(',');
                sb.Append(bw.boneIndex1).Append(',');
                sb.Append(EscapeCsv(GetBoneName(bones, bw.boneIndex1))).Append(',');
                sb.Append(bw.weight1.ToString("0.######")).Append(',');
                sb.Append(bw.boneIndex2).Append(',');
                sb.Append(EscapeCsv(GetBoneName(bones, bw.boneIndex2))).Append(',');
                sb.Append(bw.weight2.ToString("0.######")).Append(',');
                sb.Append(bw.boneIndex3).Append(',');
                sb.Append(EscapeCsv(GetBoneName(bones, bw.boneIndex3))).Append(',');
                sb.Append(bw.weight3.ToString("0.######")).Append(',');
                sb.Append(EscapeCsv(pos.x.ToString("0.######") + " " + pos.y.ToString("0.######") + " " + pos.z.ToString("0.######")));
                sb.AppendLine();
            }

            File.WriteAllText(fullPath, sb.ToString());
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
            Debug.Log("Skinning CSV exported: " + assetPath);
        }

        private static string ToAbsoluteProjectPath(string assetPath)
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName.Replace("\\", "/");
            return Path.Combine(projectRoot, assetPath).Replace("\\", "/");
        }

        private static string EscapeCsv(string value)
        {
            if (value == null)
                return "";

            if (value.IndexOf(',') >= 0 || value.IndexOf('"') >= 0 || value.IndexOf('\n') >= 0 || value.IndexOf('\r') >= 0)
                return "\"" + value.Replace("\"", "\"\"") + "\"";

            return value;
        }

        private static string GetBoneName(List<Transform> bones, int index)
        {
            if (bones == null || index < 0 || index >= bones.Count || bones[index] == null)
                return "";

            return bones[index].name;
        }

        private static int GetDominantBoneIndex(BoneWeight bw)
        {
            int index = bw.boneIndex0;
            float weight = bw.weight0;

            if (bw.weight1 > weight)
            {
                index = bw.boneIndex1;
                weight = bw.weight1;
            }

            if (bw.weight2 > weight)
            {
                index = bw.boneIndex2;
                weight = bw.weight2;
            }

            if (bw.weight3 > weight)
                index = bw.boneIndex3;

            return index;
        }

        private void ApplyDebugBoneColors(Mesh mesh, List<int> dominantBoneIndices)
        {
            if (mesh == null || dominantBoneIndices == null || dominantBoneIndices.Count != mesh.vertexCount)
                return;

            Color32[] colors = new Color32[mesh.vertexCount];
            for (int i = 0; i < colors.Length; i++)
                colors[i] = ColorFromBoneIndex(dominantBoneIndices[i]);

            mesh.colors32 = colors;
        }

        private static Color32 ColorFromBoneIndex(int boneIndex)
        {
            // Deterministic pseudo-random pastel-ish color per bone index.
            uint x = (uint)(boneIndex + 1) * 747796405u + 2891336453u;
            x = ((x >> 16) ^ x) * 2246822519u;
            x = ((x >> 13) ^ x) * 3266489917u;
            x = (x >> 16) ^ x;

            byte r = (byte)(80 + (x & 127));
            byte g = (byte)(80 + ((x >> 8) & 127));
            byte b = (byte)(80 + ((x >> 16) & 127));
            return new Color32(r, g, b, 255);
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


        private void ImportSDROnly()
        {
            if (string.IsNullOrEmpty(m_sdrFilePath) || !File.Exists(m_sdrFilePath))
            {
                Debug.LogError("Select a valid SDR/OUT file.");
                return;
            }

            PS3DS_SDRData sdr = ParseSDR(m_sdrFilePath);
            if (sdr == null || sdr.Bones.Count == 0)
            {
                Debug.LogError("No SDR skeleton found: " + m_sdrFilePath);
                return;
            }

            string baseName = Path.GetFileNameWithoutExtension(m_sdrFilePath);
            GameObject root = new GameObject(baseName + "_SDR");

            PS3DS_OBMData data = new PS3DS_OBMData();
            data.ArmatureName = string.IsNullOrEmpty(sdr.SkeletonName) ? "Armature" : sdr.SkeletonName;
            data.Bones = BuildOBMBonesFromSDR(sdr);
            CreateBones(data, root.transform);

            ImportSDRAnimationsFromData(sdr, m_sdrFilePath, root);

            Selection.activeGameObject = root;
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("Imported SDR armature only: " + m_sdrFilePath);
        }

        private List<PS3DS_OBMBone> BuildOBMBonesFromSDR(PS3DS_SDRData sdr)
        {
            List<PS3DS_OBMBone> result = new List<PS3DS_OBMBone>();

            if (sdr == null || sdr.Bones.Count == 0)
                return result;

            sdr.Bones.Sort(SortSDRBonesByIndex);

            for (int i = 0; i < sdr.Bones.Count; i++)
            {
                PS3DS_SDRBone src = sdr.Bones[i];
                PS3DS_OBMBone bone = new PS3DS_OBMBone();
                bone.Name = src.Name;
                bone.ParentName = src.ParentIndex >= 0 ? FindSDRBoneNameByIndex(sdr.Bones, src.ParentIndex) : "";
                bone.Position = src.Position;
                bone.Rotation = Quaternion.Euler(src.RotationEuler * Mathf.Rad2Deg);
                bone.Scale = src.Scale;
                result.Add(bone);
            }

            return result;
        }

        private static int SortSDRBonesByIndex(PS3DS_SDRBone a, PS3DS_SDRBone b)
        {
            if (a.Index < b.Index)
                return -1;
            if (a.Index > b.Index)
                return 1;
            return 0;
        }

        private static string FindSDRBoneNameByIndex(List<PS3DS_SDRBone> bones, int index)
        {
            for (int i = 0; i < bones.Count; i++)
            {
                if (bones[i].Index == index)
                    return bones[i].Name;
            }

            return "";
        }

        private void ImportSDRToSelected()
        {
            if (string.IsNullOrEmpty(m_sdrFilePath) || !File.Exists(m_sdrFilePath))
            {
                Debug.LogError("Select a valid SDR/OUT file.");
                return;
            }

            GameObject root = Selection.activeGameObject;
            if (root == null)
            {
                Debug.LogError("Select the imported OBM root GameObject first.");
                return;
            }

            ImportSDRAnimations(m_sdrFilePath, root);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        private string FindMatchingSDR(string obmPath)
        {
            string folder = Path.GetDirectoryName(obmPath);
            string name = Path.GetFileNameWithoutExtension(obmPath);

            string sdr = FindExternalFileInsensitive(folder, name, ".sdr");
            if (!string.IsNullOrEmpty(sdr))
                return sdr;

            string outFile = FindExternalFileInsensitive(folder, name, ".out");
            if (!string.IsNullOrEmpty(outFile))
                return outFile;

            return "";
        }

        private void ImportSDRAnimations(string sdrPath, GameObject root)
        {
            PS3DS_SDRData sdr = ParseSDR(sdrPath);
            ImportSDRAnimationsFromData(sdr, sdrPath, root);
        }

        private void ImportSDRAnimationsFromData(PS3DS_SDRData sdr, string sdrPath, GameObject root)
        {
            if (root == null)
                return;

            if (sdr == null || sdr.Actions.Count == 0)
            {
                Debug.LogError("No SDR actions found: " + sdrPath);
                return;
            }

            EnsureSDRArmatureIfNeeded(sdr, root);

            string baseName = Path.GetFileNameWithoutExtension(sdrPath);
            string animFolder = m_outputFolder + "/" + root.name + "/Animations";
            EnsureFolder(animFolder);

            Dictionary<string, Transform> boneMap = new Dictionary<string, Transform>();
            Transform[] allTransforms = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < allTransforms.Length; i++)
            {
                if (!boneMap.ContainsKey(allTransforms[i].name))
                    boneMap.Add(allTransforms[i].name, allTransforms[i]);
            }

            RebindSkinnedMeshesFromSDR(root, sdr);

            List<AnimationClip> importedClips = new List<AnimationClip>();
            int importedCount = 0;
            foreach (KeyValuePair<int, PS3DS_SDRAction> pair in sdr.Actions)
            {
                PS3DS_SDRAction action = pair.Value;
                if (action == null || action.BoneCurves.Count == 0)
                    continue;

                AnimationClip clip = CreateClipFromSDRAction(action, root.transform, boneMap);
                if (clip == null)
                    continue;

                clip.name = SanitizeFileName(baseName + "_" + action.Name);
                clip.frameRate = m_sdrFrameRate;
                clip.legacy = false;
                clip.wrapMode = WrapMode.Loop;

                string clipPath = animFolder + "/" + clip.name + ".anim";
                clipPath = AssetDatabase.GenerateUniqueAssetPath(clipPath);
                AssetDatabase.CreateAsset(clip, clipPath);

                AnimationClip savedClip = AssetDatabase.LoadAssetAtPath(clipPath, typeof(AnimationClip)) as AnimationClip;
                if (savedClip != null)
                    importedClips.Add(savedClip);

                importedCount++;
            }

            if (m_createMecanimController && importedClips.Count > 0)
                CreateMecanimController(root, animFolder, baseName, importedClips);

            Debug.Log("Imported SDR mecanim animation clips: " + importedCount + " from " + sdrPath);
        }

        private void EnsureSDRArmatureIfNeeded(PS3DS_SDRData sdr, GameObject root)
        {
            if (sdr == null || root == null || sdr.Bones.Count == 0)
                return;

            Transform armature = FindDirectChild(root.transform, string.IsNullOrEmpty(sdr.SkeletonName) ? "Armature" : sdr.SkeletonName);
            if (armature == null)
                armature = FindDirectChild(root.transform, "Armature");

            bool needsCreation = armature == null || armature.childCount == 0;
            if (!needsCreation)
                return;

            PS3DS_OBMData data = new PS3DS_OBMData();
            data.ArmatureName = string.IsNullOrEmpty(sdr.SkeletonName) ? "Armature" : sdr.SkeletonName;
            data.Bones = BuildOBMBonesFromSDR(sdr);

            List<Transform> bones = CreateBones(data, root.transform);
            RebindSkinnedMeshesFromSDR(root, sdr);
            Debug.Log("Created missing armature from SDR: " + bones.Count + " bones.");
        }

        private static Transform FindDirectChild(Transform parent, string name)
        {
            if (parent == null || string.IsNullOrEmpty(name))
                return null;

            for (int i = 0; i < parent.childCount; i++)
            {
                Transform child = parent.GetChild(i);
                if (child.name == name)
                    return child;
            }

            return null;
        }


        private void RebindSkinnedMeshesFromSDR(GameObject root, PS3DS_SDRData sdr)
        {
            if (root == null || sdr == null || sdr.Bones == null || sdr.Bones.Count == 0)
                return;

            Dictionary<string, Transform> transformMap = new Dictionary<string, Transform>();
            Transform[] allTransforms = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < allTransforms.Length; i++)
            {
                if (!transformMap.ContainsKey(allTransforms[i].name))
                    transformMap.Add(allTransforms[i].name, allTransforms[i]);
            }

            int maxIndex = 0;
            for (int i = 0; i < sdr.Bones.Count; i++)
            {
                if (sdr.Bones[i].Index > maxIndex)
                    maxIndex = sdr.Bones[i].Index;
            }

            Transform[] indexedBones = new Transform[maxIndex + 1];
            for (int i = 0; i < sdr.Bones.Count; i++)
            {
                PS3DS_SDRBone sdrBone = sdr.Bones[i];
                Transform tr = null;
                if (!string.IsNullOrEmpty(sdrBone.Name))
                    transformMap.TryGetValue(sdrBone.Name, out tr);

                if (tr != null && sdrBone.Index >= 0 && sdrBone.Index < indexedBones.Length)
                    indexedBones[sdrBone.Index] = tr;
            }

            for (int i = 0; i < indexedBones.Length; i++)
            {
                if (indexedBones[i] == null)
                {
                    Debug.LogWarning("Missing SDR bone transform at index " + i + ". Using root as fallback. Skinning may be wrong for this bone.");
                    indexedBones[i] = root.transform;
                }
            }

            Matrix4x4[] bindposes = new Matrix4x4[indexedBones.Length];
            for (int i = 0; i < indexedBones.Length; i++)
                bindposes[i] = indexedBones[i].worldToLocalMatrix * root.transform.localToWorldMatrix;

            SkinnedMeshRenderer[] renderers = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                SkinnedMeshRenderer smr = renderers[i];
                if (smr.sharedMesh != null)
                {
                    smr.sharedMesh.bindposes = bindposes;
                    EditorUtility.SetDirty(smr.sharedMesh);
                }

                smr.bones = indexedBones;
                smr.rootBone = indexedBones.Length > 0 ? indexedBones[0] : root.transform;
                Bounds bounds = smr.localBounds;
                bounds.Expand(m_boundExpand);
                smr.localBounds = bounds;
                smr.updateWhenOffscreen = true;
                EditorUtility.SetDirty(smr);
            }
        }

        private void CreateMecanimController(GameObject root, string animFolder, string baseName, List<AnimationClip> clips)
        {
            if (root == null || clips == null || clips.Count == 0)
                return;

            string controllerPath = animFolder + "/" + SanitizeFileName(baseName + "_Controller") + ".controller";
            controllerPath = AssetDatabase.GenerateUniqueAssetPath(controllerPath);

            AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
            AnimatorControllerLayer layer = controller.layers[0];
            AnimatorStateMachine stateMachine = layer.stateMachine;

            for (int i = 0; i < clips.Count; i++)
            {
                AnimationClip clip = clips[i];
                AnimatorState state = stateMachine.AddState(clip.name);
                state.motion = clip;

                if (i == 0)
                    stateMachine.defaultState = state;
            }

            Animator animator = root.GetComponent<Animator>();
            if (animator == null)
                animator = root.AddComponent<Animator>();

            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            UnityEngine.Animation legacy = root.GetComponent<UnityEngine.Animation>();
            if (legacy != null)
                DestroyImmediate(legacy, true);

            EditorUtility.SetDirty(root);
            EditorUtility.SetDirty(animator);
            Debug.Log("Created Mecanim controller: " + controllerPath);
        }

        private void RebindSkinnedMeshes(GameObject root, List<Transform> bones)
        {
            if (root == null || bones == null || bones.Count == 0)
                return;

            Matrix4x4[] bindposes = CreateBindposes(bones);
            SkinnedMeshRenderer[] renderers = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i].sharedMesh != null)
                    renderers[i].sharedMesh.bindposes = bindposes;

                renderers[i].bones = bones.ToArray();
                renderers[i].rootBone = bones[0];
            }
        }

        private AnimationClip CreateClipFromSDRAction(PS3DS_SDRAction action, Transform root, Dictionary<string, Transform> boneMap)
        {
            AnimationClip clip = new AnimationClip();
            clip.name = action.Name;

            foreach (KeyValuePair<string, List<PS3DS_SDRFCurve>> bonePair in action.BoneCurves)
            {
                Transform boneTransform = null;
                if (!boneMap.TryGetValue(bonePair.Key, out boneTransform) || boneTransform == null)
                {
                    Debug.LogWarning("SDR bone not found in imported OBM armature: " + bonePair.Key);
                    continue;
                }

                string path = AnimationUtility.CalculateTransformPath(boneTransform, root);

                for (int i = 0; i < bonePair.Value.Count; i++)
                {
                    PS3DS_SDRFCurve fcurve = bonePair.Value[i];
                    if (fcurve == null || fcurve.Keyframes.Count == 0)
                        continue;

                    AddFCurveToClip(clip, path, fcurve);
                }
            }

            return clip;
        }

        private void AddFCurveToClip(AnimationClip clip, string path, PS3DS_SDRFCurve fcurve)
        {
            int axis = fcurve.Axis - 1;

            if (fcurve.Axis == 0 && fcurve.DataType == PS3DS_SDRDataType.Vec3)
            {
                AddVector3CurveToClip(clip, path, fcurve);
                return;
            }

            if (axis < 0 || axis > 2)
                axis = Mathf.Clamp(fcurve.ChannelIndex, 0, 2);

            string property = GetUnityTransformProperty(fcurve.Component, axis);
            if (string.IsNullOrEmpty(property))
                return;

            AnimationCurve curve = new AnimationCurve();
            for (int i = 0; i < fcurve.Keyframes.Count; i++)
            {
                PS3DS_SDRKeyframe src = fcurve.Keyframes[i];
                Keyframe key = new Keyframe(src.Time, src.Value.x);
                curve.AddKey(key);
            }

            clip.SetCurve(path, typeof(Transform), property, curve);
        }

        private void AddVector3CurveToClip(AnimationClip clip, string path, PS3DS_SDRFCurve fcurve)
        {
            for (int axis = 0; axis < 3; axis++)
            {
                string property = GetUnityTransformProperty(fcurve.Component, axis);
                if (string.IsNullOrEmpty(property))
                    continue;

                AnimationCurve curve = new AnimationCurve();
                for (int i = 0; i < fcurve.Keyframes.Count; i++)
                {
                    PS3DS_SDRKeyframe src = fcurve.Keyframes[i];
                    curve.AddKey(new Keyframe(src.Time, GetVectorComponent(src.Value, axis)));
                }

                clip.SetCurve(path, typeof(Transform), property, curve);
            }
        }

        private static float GetVectorComponent(Vector4 v, int axis)
        {
            if (axis == 0)
                return v.x;
            if (axis == 1)
                return v.y;
            return v.z;
        }

        private static string GetUnityTransformProperty(PS3DS_SDRComponent component, int axis)
        {
            string suffix = axis == 0 ? ".x" : axis == 1 ? ".y" : ".z";

            if (component == PS3DS_SDRComponent.Location)
                return "localPosition" + suffix;

            if (component == PS3DS_SDRComponent.RotationEuler)
                return "localEulerAnglesRaw" + suffix;

            if (component == PS3DS_SDRComponent.Scale)
                return "localScale" + suffix;

            return "";
        }

        private PS3DS_SDRData ParseSDR(string path)
        {
            byte[] bytes = File.ReadAllBytes(path);
            PS3DS_SDRReader reader = new PS3DS_SDRReader(bytes);
            PS3DS_SDRData data = new PS3DS_SDRData();

            uint skeletonsListAddr = reader.ReadUInt32(0x8);
            ushort numSkeletons = reader.ReadUInt16(0x18);

            if (skeletonsListAddr == 0 || numSkeletons == 0)
            {
                Debug.LogError("This SDR/OUT does not look like a normal SDR with skeleton list: " + path);
                return data;
            }

            for (int i = 0; i < numSkeletons; i++)
            {
                uint skeletonHeaderAddr = reader.ReadUInt32((int)skeletonsListAddr + 4 * i);
                ParseSDRSkeleton(reader, skeletonHeaderAddr, data);
            }

            return data;
        }

        private void ParseSDRSkeleton(PS3DS_SDRReader reader, uint address, PS3DS_SDRData data)
        {
            if (!reader.IsValidAddress(address))
                return;

            uint nameAddr = reader.ReadUInt32((int)address + 0x00);
            string skeletonName = reader.ReadString(nameAddr);
            if (!string.IsNullOrEmpty(skeletonName))
                data.SkeletonName = skeletonName;

            uint actionsAddr = reader.ReadUInt32((int)address + 0x0C);
            ushort numActions = reader.ReadUInt16((int)address + 0x08);
            ParseSDRActions(reader, actionsAddr, numActions, data);

            uint rootBoneAddr = reader.ReadUInt32((int)address + 0x10);
            ParseSDRBoneRecursive(reader, rootBoneAddr, data, -1);
        }

        private void ParseSDRActions(PS3DS_SDRReader reader, uint address, int numActions, PS3DS_SDRData data)
        {
            if (!reader.IsValidAddress(address))
                return;

            for (int i = 0; i < numActions; i++)
            {
                uint actionAddr = address + (uint)(i * 0x30);
                uint nameAddr = reader.ReadUInt32((int)actionAddr);
                string name = reader.ReadString(nameAddr);
                if (string.IsNullOrEmpty(name))
                    name = "Action_" + i;

                if (!data.Actions.ContainsKey(i))
                    data.Actions.Add(i, new PS3DS_SDRAction(name));
            }
        }

        private void ParseSDRBoneRecursive(PS3DS_SDRReader reader, uint address, PS3DS_SDRData data, int parentIndex)
        {
            if (!reader.IsValidAddress(address))
                return;

            uint nameAddr = reader.ReadUInt32((int)address + 0x04);
            string boneName = reader.ReadString(nameAddr);
            if (string.IsNullOrEmpty(boneName))
                boneName = "Bone_" + reader.ReadUInt16((int)address + 0x08);

            int boneIndex = reader.ReadUInt16((int)address + 0x08);
            PS3DS_SDRBone sdrBone = new PS3DS_SDRBone();
            sdrBone.Index = boneIndex;
            sdrBone.Name = boneName;
            sdrBone.ParentIndex = parentIndex;
            sdrBone.Position = ReadOptionalVec3(reader, reader.ReadUInt32((int)address + 0x0C), Vector3.zero);
            sdrBone.RotationEuler = ReadOptionalVec3(reader, reader.ReadUInt32((int)address + 0x10), Vector3.zero);
            sdrBone.Scale = ReadOptionalVec3(reader, reader.ReadUInt32((int)address + 0x14), Vector3.one);
            AddOrReplaceSDRBone(data, sdrBone);

            uint animDataAddr = reader.ReadUInt32((int)address + 0x20);
            if (animDataAddr != 0)
                ParseSDRFCurveBlocks(reader, animDataAddr, boneName, data);

            uint childAddr = reader.ReadUInt32((int)address + 0x24);
            if (childAddr != 0)
                ParseSDRBoneRecursive(reader, childAddr, data, boneIndex);

            uint nextAddr = reader.ReadUInt32((int)address + 0x28);
            if (nextAddr != 0)
                ParseSDRBoneRecursive(reader, nextAddr, data, parentIndex);
        }

        private static Vector3 ReadOptionalVec3(PS3DS_SDRReader reader, uint address, Vector3 fallback)
        {
            if (!reader.IsValidAddress(address))
                return fallback;

            return new Vector3(reader.ReadFloat((int)address), reader.ReadFloat((int)address + 4), reader.ReadFloat((int)address + 8));
        }

        private static void AddOrReplaceSDRBone(PS3DS_SDRData data, PS3DS_SDRBone bone)
        {
            for (int i = 0; i < data.Bones.Count; i++)
            {
                if (data.Bones[i].Index == bone.Index)
                {
                    data.Bones[i] = bone;
                    return;
                }
            }

            data.Bones.Add(bone);
        }

        private void ParseSDRFCurveBlocks(PS3DS_SDRReader reader, uint address, string boneName, PS3DS_SDRData data)
        {
            uint nextAddr = address;
            int guard = 0;

            while (nextAddr != 0 && reader.IsValidAddress(nextAddr) && guard < 4096)
            {
                guard++;

                ushort actionIndex = reader.ReadUInt16((int)nextAddr + 0x00);
                ushort numFCurves = reader.ReadUInt16((int)nextAddr + 0x02);
                uint fcurveListAddr = reader.ReadUInt32((int)nextAddr + 0x04);

                PS3DS_SDRAction action = null;
                if (!data.Actions.TryGetValue(actionIndex, out action))
                {
                    action = new PS3DS_SDRAction("Action_" + actionIndex);
                    data.Actions.Add(actionIndex, action);
                }

                if (!action.BoneCurves.ContainsKey(boneName))
                    action.BoneCurves.Add(boneName, new List<PS3DS_SDRFCurve>());

                for (int i = 0; i < numFCurves; i++)
                {
                    uint fcurveAddr = fcurveListAddr + (uint)(i * 0x10);
                    PS3DS_SDRFCurve fcurve = ParseSDRFCurve(reader, fcurveAddr);
                    if (fcurve != null && fcurve.Keyframes.Count > 0)
                        action.BoneCurves[boneName].Add(fcurve);
                }

                nextAddr = reader.ReadUInt32((int)nextAddr + 0x0C);
            }
        }

        private PS3DS_SDRFCurve ParseSDRFCurve(PS3DS_SDRReader reader, uint address)
        {
            if (!reader.IsValidAddress(address))
                return null;

            int componentIndex = reader.ReadByte((int)address + 0x01);
            if (componentIndex < 0 || componentIndex >= 3)
                return null;

            int axis = reader.ReadByte((int)address + 0x02);
            int channelIndex = reader.ReadByte((int)address + 0x03);
            int dataTypeId = reader.ReadByte((int)address + 0x06);
            int exp = reader.ReadByte((int)address + 0x07);
            uint keyframeAddr = reader.ReadUInt32((int)address + 0x08);

            PS3DS_SDRDataType dataType = ConvertSDRDataType(dataTypeId);
            if (dataType == PS3DS_SDRDataType.Float || dataType == PS3DS_SDRDataType.Quat || dataType == PS3DS_SDRDataType.Vec3 || dataType == PS3DS_SDRDataType.Vec2)
                exp = 0;

            PS3DS_SDRFCurve fcurve = new PS3DS_SDRFCurve();
            fcurve.Axis = axis;
            fcurve.ChannelIndex = channelIndex;
            fcurve.Component = (PS3DS_SDRComponent)componentIndex;
            fcurve.DataType = axis == 0 ? PS3DS_SDRDataType.Vec3 : dataType;
            fcurve.Keyframes = ParseSDRKeyframes(reader, keyframeAddr, exp, fcurve.DataType);
            return fcurve;
        }

        private List<PS3DS_SDRKeyframe> ParseSDRKeyframes(PS3DS_SDRReader reader, uint address, int scaleExp, PS3DS_SDRDataType dataType)
        {
            List<PS3DS_SDRKeyframe> result = new List<PS3DS_SDRKeyframe>();
            if (!reader.IsValidAddress(address))
                return result;

            uint valsAddr = reader.ReadUInt32((int)address + 0x00);
            ushort valueCount = reader.ReadUInt16((int)address + 0x08);
            uint keyframesAddr = reader.ReadUInt32((int)address + 0x10);
            ushort numKeyframes = reader.ReadUInt16((int)address + 0x14);
            float scale = Mathf.Pow(2f, scaleExp);
            if (scale <= 0.0001f)
                scale = 1f;

            if (numKeyframes > 0)
            {
                for (int i = 0; i < numKeyframes; i++)
                {
                    uint keyframeAddr = keyframesAddr + (uint)(i * 0x0C);
                    ushort valueIndex = reader.ReadUInt16((int)keyframeAddr + 0x02);
                    Vector4 value = ReadSDRKeyframeValue(reader, dataType, valsAddr, valueIndex);
                    float time = reader.ReadFloat((int)keyframeAddr + 0x08);
                    result.Add(new PS3DS_SDRKeyframe(time, value / scale));
                }
            }
            else if (valueCount > 0)
            {
                int framerate = reader.ReadUInt16((int)address + 0x16) & 0xFF;
                if (framerate <= 0)
                    framerate = Mathf.RoundToInt(m_sdrFrameRate);
                if (framerate <= 0)
                    framerate = 30;

                for (int i = 0; i < valueCount; i++)
                {
                    Vector4 value = ReadSDRKeyframeValue(reader, dataType, valsAddr, i);
                    float time = (0.5f + (float)(i - 1)) / (float)framerate;
                    if (time < 0f)
                        time = 0f;
                    result.Add(new PS3DS_SDRKeyframe(time, value / scale));
                }
            }

            return result;
        }

        private Vector4 ReadSDRKeyframeValue(PS3DS_SDRReader reader, PS3DS_SDRDataType dataType, uint baseAddress, int index)
        {
            if (!reader.IsValidAddress(baseAddress))
                return Vector4.zero;

            if (dataType == PS3DS_SDRDataType.Float)
                return new Vector4(reader.ReadFloat((int)baseAddress + index * 4), 0f, 0f, 0f);

            if (dataType == PS3DS_SDRDataType.UChar)
                return new Vector4(reader.ReadByte((int)baseAddress + index), 0f, 0f, 0f);

            if (dataType == PS3DS_SDRDataType.Char)
                return new Vector4(reader.ReadSByte((int)baseAddress + index), 0f, 0f, 0f);

            if (dataType == PS3DS_SDRDataType.UShort)
                return new Vector4(reader.ReadUInt16((int)baseAddress + index * 2), 0f, 0f, 0f);

            if (dataType == PS3DS_SDRDataType.Short)
                return new Vector4(reader.ReadInt16((int)baseAddress + index * 2), 0f, 0f, 0f);

            if (dataType == PS3DS_SDRDataType.Vec2)
            {
                int addr = (int)baseAddress + index * 8;
                return new Vector4(reader.ReadFloat(addr), reader.ReadFloat(addr + 4), 0f, 0f);
            }

            if (dataType == PS3DS_SDRDataType.Quat)
            {
                int addr = (int)baseAddress + index * 16;
                return new Vector4(reader.ReadFloat(addr), reader.ReadFloat(addr + 4), reader.ReadFloat(addr + 8), reader.ReadFloat(addr + 12));
            }

            // Vec3 / default
            {
                int addr = (int)baseAddress + index * 12;
                return new Vector4(reader.ReadFloat(addr), reader.ReadFloat(addr + 4), reader.ReadFloat(addr + 8), 0f);
            }
        }

        private static PS3DS_SDRDataType ConvertSDRDataType(int type)
        {
            if (type == 0)
                return PS3DS_SDRDataType.Float;
            if (type == 2)
                return PS3DS_SDRDataType.Quat;
            if (type == 5)
                return PS3DS_SDRDataType.UChar;
            if (type == 6)
                return PS3DS_SDRDataType.Char;
            if (type == 7)
                return PS3DS_SDRDataType.UShort;
            if (type == 8)
                return PS3DS_SDRDataType.Short;

            return PS3DS_SDRDataType.Float;
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


        private enum PS3DS_SDRComponent
        {
            Location = 0,
            RotationEuler = 1,
            Scale = 2
        }

        private enum PS3DS_SDRDataType
        {
            Float,
            Quat,
            UChar,
            Char,
            UShort,
            Short,
            Vec2,
            Vec3
        }

        private class PS3DS_SDRData
        {
            public string SkeletonName = "Armature";
            public List<PS3DS_SDRBone> Bones = new List<PS3DS_SDRBone>();
            public Dictionary<int, PS3DS_SDRAction> Actions = new Dictionary<int, PS3DS_SDRAction>();
        }

        private class PS3DS_SDRBone
        {
            public int Index;
            public string Name;
            public int ParentIndex;
            public Vector3 Position;
            public Vector3 RotationEuler;
            public Vector3 Scale;
        }

        private class PS3DS_SDRAction
        {
            public string Name;
            public Dictionary<string, List<PS3DS_SDRFCurve>> BoneCurves = new Dictionary<string, List<PS3DS_SDRFCurve>>();

            public PS3DS_SDRAction(string name)
            {
                Name = name;
            }
        }

        private class PS3DS_SDRFCurve
        {
            public int Axis;
            public int ChannelIndex;
            public PS3DS_SDRComponent Component;
            public PS3DS_SDRDataType DataType;
            public List<PS3DS_SDRKeyframe> Keyframes = new List<PS3DS_SDRKeyframe>();
        }

        private class PS3DS_SDRKeyframe
        {
            public float Time;
            public Vector4 Value;

            public PS3DS_SDRKeyframe(float time, Vector4 value)
            {
                Time = time;
                Value = value;
            }
        }

        private class PS3DS_SDRReader
        {
            private byte[] m_bytes;

            public PS3DS_SDRReader(byte[] bytes)
            {
                m_bytes = bytes;
            }

            public bool IsValidAddress(uint address)
            {
                return address < m_bytes.Length;
            }

            public byte ReadByte(int address)
            {
                if (address < 0 || address >= m_bytes.Length)
                    return 0;

                return m_bytes[address];
            }

            public sbyte ReadSByte(int address)
            {
                return unchecked((sbyte)ReadByte(address));
            }

            public ushort ReadUInt16(int address)
            {
                if (address < 0 || address + 1 >= m_bytes.Length)
                    return 0;

                return (ushort)((m_bytes[address] << 8) | m_bytes[address + 1]);
            }

            public short ReadInt16(int address)
            {
                return unchecked((short)ReadUInt16(address));
            }

            public uint ReadUInt32(int address)
            {
                if (address < 0 || address + 3 >= m_bytes.Length)
                    return 0;

                return ((uint)m_bytes[address] << 24) |
                       ((uint)m_bytes[address + 1] << 16) |
                       ((uint)m_bytes[address + 2] << 8) |
                       (uint)m_bytes[address + 3];
            }

            public float ReadFloat(int address)
            {
                if (address < 0 || address + 3 >= m_bytes.Length)
                    return 0f;

                byte[] tmp = new byte[4];
                tmp[0] = m_bytes[address + 3];
                tmp[1] = m_bytes[address + 2];
                tmp[2] = m_bytes[address + 1];
                tmp[3] = m_bytes[address];
                return System.BitConverter.ToSingle(tmp, 0);
            }

            public string ReadString(uint address)
            {
                if (!IsValidAddress(address))
                    return "";

                int start = (int)address;
                int end = start;
                while (end < m_bytes.Length && m_bytes[end] != 0)
                    end++;

                if (end <= start)
                    return "";

                return Encoding.ASCII.GetString(m_bytes, start, end - start);
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
            public Vector3 Scale;
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