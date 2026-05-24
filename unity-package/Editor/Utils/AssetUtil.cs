using UnityEditor;

namespace UnityMCP.Utils
{
    /// <summary>Small AssetDatabase path helpers shared by the asset/prefab/scene handlers.</summary>
    public static class AssetUtil
    {
        /// <summary>Ensure the folder that would contain <paramref name="assetPath"/> exists.</summary>
        public static void EnsureFolderForAsset(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath)) return;
            int slash = assetPath.LastIndexOf('/');
            if (slash <= 0) return;
            EnsureFolder(assetPath.Substring(0, slash));
        }

        /// <summary>Create a project folder (and any missing parents), e.g. "Assets/A/B/C".</summary>
        public static void EnsureFolder(string folderPath)
        {
            if (string.IsNullOrEmpty(folderPath) || AssetDatabase.IsValidFolder(folderPath)) return;
            var parts = folderPath.Split('/');
            string cur = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = cur + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(cur, parts[i]);
                cur = next;
            }
        }
    }
}
