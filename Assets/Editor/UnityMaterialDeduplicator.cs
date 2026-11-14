using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

public class ScarletMaterialDeduplicator : EditorWindow
{
    private class MaterialGroup
    {
        public Material master;
        public List<Material> duplicates = new List<Material>();
    }

    private readonly List<MaterialGroup> groups = new List<MaterialGroup>();
    private Vector2 scroll;
    private bool scanDone;

    // Extensions of the 3D model files whose Materials remapping gets updated
    private static readonly string[] ModelExtensions =
    {
        ".fbx", ".glb", ".gltf", ".obj", ".blend", ".dae", ".3ds"
    };

    [MenuItem("Tools/Scarlet/Material Deduplicator")]
    public static void ShowWindow()
    {
        GetWindow<ScarletMaterialDeduplicator>("Material Deduplicator");
    }

    // ============================================================
    // GUI
    // ============================================================

    private void OnGUI()
    {
        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("Scarlet Material Deduplicator", EditorStyles.boldLabel);

        EditorGUILayout.HelpBox(
            "1) SCAN: finds strictly identical Materials (Assets folder only).\n" +
            "2) MERGE: replaces the duplicates with a single Material (models, prefabs, scenes).\n" +
            "3) Check your project (nothing pink?), then TRASH to delete the duplicates.",
            MessageType.Info);

        EditorGUILayout.Space(5);

        if (GUILayout.Button("1. SCAN PROJECT", GUILayout.Height(35)))
            Scan();

        if (!scanDone)
            return;

        EditorGUILayout.Space(10);

        int duplicateCount = groups.Sum(g => g.duplicates.Count);
        EditorGUILayout.LabelField($"Duplicate groups: {groups.Count}", EditorStyles.boldLabel);
        EditorGUILayout.LabelField($"Duplicate Materials: {duplicateCount}");

        if (groups.Count == 0)
        {
            EditorGUILayout.HelpBox("No strictly identical Materials found.", MessageType.Info);
            return;
        }

        EditorGUILayout.Space(5);

        if (GUILayout.Button("2. MERGE DUPLICATES", GUILayout.Height(40)))
        {
            Merge();
            GUIUtility.ExitGUI();
        }

        if (GUILayout.Button("3. TRASH DUPLICATES (after checking)", GUILayout.Height(30)))
        {
            TrashDuplicates();
            GUIUtility.ExitGUI();
        }

        EditorGUILayout.Space(10);

        scroll = EditorGUILayout.BeginScrollView(scroll);
        DisplayGroups();
        EditorGUILayout.EndScrollView();
    }

    private void DisplayGroups()
    {
        for (int i = 0; i < groups.Count; i++)
        {
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField($"Group {i + 1}", EditorStyles.boldLabel);

            EditorGUILayout.LabelField("Kept Material:");
            EditorGUILayout.ObjectField(groups[i].master, typeof(Material), false);

            EditorGUILayout.LabelField("Duplicates:");
            foreach (Material duplicate in groups[i].duplicates)
                EditorGUILayout.ObjectField(duplicate, typeof(Material), false);

            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(5);
        }
    }

    // ============================================================
    // SCAN
    // ============================================================

    private void Scan()
    {
        groups.Clear();
        scanDone = true;

        // signature -> Materials that share exactly this signature
        var bySignature = new Dictionary<string, List<Material>>();
        string[] guids = AssetDatabase.FindAssets("t:Material", new[] { "Assets" });

        try
        {
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);

                // Skip Materials embedded in an FBX/GLB: they cannot be replaced
                if (!path.EndsWith(".mat"))
                    continue;

                Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material == null || material.shader == null)
                    continue;

                if (EditorUtility.DisplayCancelableProgressBar(
                        "Material Deduplicator", $"Analyzing: {material.name}", (float)i / guids.Length))
                    break;

                string signature = ComputeSignature(material);

                if (!bySignature.TryGetValue(signature, out List<Material> list))
                {
                    list = new List<Material>();
                    bySignature[signature] = list;
                }

                list.Add(material);
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        foreach (List<Material> list in bySignature.Values)
        {
            if (list.Count < 2)
                continue;

            // Kept Material = shortest name (so no "(1)" or "_1" suffix), then alphabetical path order
            List<Material> sorted = list
                .OrderBy(m => m.name.Length)
                .ThenBy(m => AssetDatabase.GetAssetPath(m))
                .ToList();

            groups.Add(new MaterialGroup
            {
                master = sorted[0],
                duplicates = sorted.Skip(1).ToList()
            });
        }

        Debug.Log($"Material Deduplicator: {groups.Count} duplicate groups found.");
        Repaint();
    }

    // Two Materials with the same signature are strictly identical.
    private static string ComputeSignature(Material m)
    {
        Shader shader = m.shader;
        var sb = new StringBuilder();

        sb.Append(shader.GetInstanceID()).Append('|')
          .Append(m.renderQueue).Append('|')
          .Append(m.enableInstancing).Append('|')
          .Append(m.doubleSidedGI).Append('|')
          .Append((int)m.globalIlluminationFlags).Append('|');

        foreach (string keyword in m.enabledKeywords.Select(k => k.name).OrderBy(k => k))
            sb.Append(keyword).Append(';');

        sb.Append('|');

        int count = shader.GetPropertyCount();
        for (int i = 0; i < count; i++)
        {
            string name = shader.GetPropertyName(i);
            sb.Append(name).Append('=');

            switch (shader.GetPropertyType(i))
            {
                case ShaderPropertyType.Color:
                    Color c = m.GetColor(name);
                    sb.Append(F(c.r)).Append(',').Append(F(c.g)).Append(',')
                      .Append(F(c.b)).Append(',').Append(F(c.a));
                    break;

                case ShaderPropertyType.Vector:
                    Vector4 v = m.GetVector(name);
                    sb.Append(F(v.x)).Append(',').Append(F(v.y)).Append(',')
                      .Append(F(v.z)).Append(',').Append(F(v.w));
                    break;

                case ShaderPropertyType.Float:
                case ShaderPropertyType.Range:
                    sb.Append(F(m.GetFloat(name)));
                    break;

                case ShaderPropertyType.Int:
                    sb.Append(m.GetInteger(name));
                    break;

                case ShaderPropertyType.Texture:
                    Texture tex = m.GetTexture(name);
                    Vector2 offset = m.GetTextureOffset(name);
                    Vector2 scale = m.GetTextureScale(name);
                    sb.Append(tex != null ? tex.GetInstanceID() : 0).Append('@')
                      .Append(F(offset.x)).Append(',').Append(F(offset.y)).Append('x')
                      .Append(F(scale.x)).Append(',').Append(F(scale.y));
                    break;
            }

            sb.Append(';');
        }

        return sb.ToString();
    }

    private static string F(float value)
    {
        return value.ToString("F5", CultureInfo.InvariantCulture);
    }

    // ============================================================
    // MERGE (single pass over the project)
    // ============================================================

    private void Merge()
    {
        if (groups.Count == 0)
            return;

        // Avoid losing unsaved work in the open scene(s)
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return;

        // duplicate -> kept Material
        var map = new Dictionary<Material, Material>();
        foreach (MaterialGroup group in groups)
            foreach (Material duplicate in group.duplicates)
                map[duplicate] = group.master;

        var duplicatePaths = new HashSet<string>(map.Keys.Select(AssetDatabase.GetAssetPath));

        if (!EditorUtility.DisplayDialog(
                "Merge Materials?",
                $"References to {map.Count} duplicate Materials will be replaced.\n\n" +
                "The affected models, prefabs and scenes will be modified.\n" +
                "The duplicates will NOT be deleted.",
                "Continue", "Cancel"))
            return;

        List<string> users = FindUsers(duplicatePaths);

        // Models first, then prefabs, assets, and scenes last
        var models = users.Where(p => ModelExtensions.Contains(Path.GetExtension(p).ToLowerInvariant())).ToList();
        var prefabs = users.Where(p => p.EndsWith(".prefab")).ToList();
        var assets = users.Where(p => p.EndsWith(".asset")).ToList();
        var scenes = users.Where(p => p.EndsWith(".unity")).ToList();

        int total = models.Count + prefabs.Count + assets.Count + scenes.Count;
        int done = 0;

        try
        {
            foreach (string path in models)
            {
                Progress("Models", path, ++done, total);
                RemapInModel(path, map);
            }

            foreach (string path in prefabs)
            {
                Progress("Prefabs", path, ++done, total);
                ReplaceInPrefab(path, map);
            }

            foreach (string path in assets)
            {
                Progress("Assets", path, ++done, total);
                ReplaceInAsset(path, map);
            }

            foreach (string path in scenes)
            {
                Progress("Scenes", path, ++done, total);
                ReplaceInScene(path, map);
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        EditorUtility.DisplayDialog(
            "Merge complete",
            $"{models.Count} model(s), {prefabs.Count} prefab(s), {assets.Count} asset(s) " +
            $"and {scenes.Count} scene(s) updated.\n\n" +
            "Check your project (nothing pink?) before deleting the duplicates.",
            "OK");

        Scan();
    }

    private static void Progress(string label, string path, int done, int total)
    {
        EditorUtility.DisplayProgressBar("Merging Materials", $"{label}: {path}", (float)done / Mathf.Max(1, total));
    }

    // Returns the assets in "Assets/" that directly use at least one of the given Materials.
    private static List<string> FindUsers(HashSet<string> materialPaths)
    {
        var users = new List<string>();
        string[] allPaths = AssetDatabase.GetAllAssetPaths();

        try
        {
            for (int i = 0; i < allPaths.Length; i++)
            {
                string path = allPaths[i];

                if (!path.StartsWith("Assets/") || path.EndsWith(".cs") || materialPaths.Contains(path))
                    continue;

                if (AssetDatabase.IsValidFolder(path))
                    continue;

                if (i % 200 == 0)
                    EditorUtility.DisplayProgressBar("Material Deduplicator", "Looking for usages...", (float)i / allPaths.Length);

                foreach (string dependency in AssetDatabase.GetDependencies(path, false))
                {
                    if (materialPaths.Contains(dependency))
                    {
                        users.Add(path);
                        break;
                    }
                }
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        return users;
    }

    // ============================================================
    // MODELS (Materials tab of the Import Settings)
    // ============================================================

    private static void RemapInModel(string path, Dictionary<Material, Material> map)
    {
        AssetImporter importer = AssetImporter.GetAtPath(path);
        if (importer == null)
            return;

        bool changed = false;

        foreach (var entry in importer.GetExternalObjectMap().ToList())
        {
            if (entry.Key.type != typeof(Material))
                continue;

            Material current = entry.Value as Material;
            if (current != null && map.TryGetValue(current, out Material master))
            {
                importer.AddRemap(entry.Key, master);
                changed = true;
            }
        }

        if (changed)
            importer.SaveAndReimport();
    }

    // ============================================================
    // PREFABS / ASSETS / SCENES
    // ============================================================

    private static void ReplaceInPrefab(string path, Dictionary<Material, Material> map)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(path);

        try
        {
            bool changed = false;

            foreach (Component component in root.GetComponentsInChildren<Component>(true))
            {
                if (component != null && ReplaceInObject(component, map))
                    changed = true;
            }

            if (changed)
                PrefabUtility.SaveAsPrefabAsset(root, path);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static void ReplaceInAsset(string path, Dictionary<Material, Material> map)
    {
        foreach (Object obj in AssetDatabase.LoadAllAssetsAtPath(path))
        {
            if (obj is ScriptableObject)
                ReplaceInObject(obj, map);
        }
    }

    private static void ReplaceInScene(string path, Dictionary<Material, Material> map)
    {
        Scene scene = SceneManager.GetSceneByPath(path);
        bool wasAlreadyOpen = scene.isLoaded;

        if (!wasAlreadyOpen)
            scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);

        try
        {
            bool changed = false;

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (Component component in root.GetComponentsInChildren<Component>(true))
                {
                    if (component != null && ReplaceInObject(component, map))
                        changed = true;
                }
            }

            if (changed)
            {
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }
        }
        finally
        {
            if (!wasAlreadyOpen)
                EditorSceneManager.CloseScene(scene, true);
        }
    }

    // Walks through ALL properties (including arrays such as m_Materials)
    private static bool ReplaceInObject(Object target, Dictionary<Material, Material> map)
    {
        var serializedObject = new SerializedObject(target);
        SerializedProperty property = serializedObject.GetIterator();
        bool changed = false;

        while (property.NextVisible(true))
        {
            if (property.propertyType != SerializedPropertyType.ObjectReference)
                continue;

            Material current = property.objectReferenceValue as Material;
            if (current != null && map.TryGetValue(current, out Material master))
            {
                property.objectReferenceValue = master;
                changed = true;
            }
        }

        if (changed)
        {
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(target);
        }

        return changed;
    }

    // ============================================================
    // DELETION (separate step, with a safety check)
    // ============================================================

    private void TrashDuplicates()
    {
        var duplicatePaths = new HashSet<string>(
            groups.SelectMany(g => g.duplicates).Select(AssetDatabase.GetAssetPath));

        // Safety: refuse if anything still uses a duplicate
        List<string> stillUsed = FindUsers(duplicatePaths);

        if (stillUsed.Count > 0)
        {
            EditorUtility.DisplayDialog(
                "Deletion refused",
                "These assets still use duplicates (run MERGE first):\n\n" +
                string.Join("\n", stillUsed.Take(10)) +
                (stillUsed.Count > 10 ? $"\n... and {stillUsed.Count - 10} more" : ""),
                "OK");
            return;
        }

        if (!EditorUtility.DisplayDialog(
                "Delete duplicates?",
                $"{duplicatePaths.Count} Materials will be moved to the trash.",
                "Delete", "Cancel"))
            return;

        foreach (string path in duplicatePaths)
            AssetDatabase.MoveAssetToTrash(path);

        AssetDatabase.Refresh();
        Scan();
    }
}
