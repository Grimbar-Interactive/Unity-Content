using System;
using System.Collections.Generic;
using GI.UnityToolkit.Variables;
using Sirenix.OdinInspector.Editor;
using UnityEditor;
using UnityEngine;

namespace GI.UnityToolkit.Content.Editor
{
    /// <summary>Builds the default left-hand list: assets nested by their real on-disk subfolder
    /// under the type's content root, alphabetical with folders first, plus an "Other" bucket for
    /// anything found outside the root.</summary>
    internal static class ContentTreeBuilder
    {
        public const string OTHER_FOLDER_NAME = "Other";
        private static readonly string[] SearchFolders = { "Assets" };

        public static void Build(OdinMenuTree tree, ContentTypeInfo info, ContentEditor editor)
        {
            var root = info.RootFolder;
            var rootPrefix = root + "/";

            var inRoot = new List<(DataObject asset, string folder)>();   // folder relative to root, "" = root itself
            var strays = new List<DataObject>();

            foreach (var guid in AssetDatabase.FindAssets($"t:{info.ContentType.Name}", SearchFolders))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var asset = AssetDatabase.LoadAssetAtPath<DataObject>(path);
                if (asset == null) continue;

                // "t:X" matches subclasses, and a subclass may be its own registered type.
                // Only the nearest registered type owns the asset.
                if (ContentTypeRegistry.FindNearest(asset.GetType()) != info) continue;

                if (path.StartsWith(rootPrefix, StringComparison.Ordinal))
                {
                    var relative = path.Substring(rootPrefix.Length);
                    var slash = relative.LastIndexOf('/');
                    inRoot.Add((asset, slash < 0 ? string.Empty : relative.Substring(0, slash)));
                }
                else
                {
                    strays.Add(asset);
                }
            }

            // Folder nodes, ordinal-sorted so every ancestor is inserted before its descendant
            // (an ancestor path is always a strict prefix of its descendant, and a prefix always
            // sorts first ordinally). That is what lets each folder be added directly under its
            // already-created parent below, with no intermediate node ever left as a plain item.
            var folderPaths = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var (_, folder) in inRoot) AddFolderChain(folderPaths, folder);
            if (editor.SupportsFolders) CollectDiskFolders(root, root, folderPaths);

            var folderItems = new Dictionary<string, ContentFolderMenuItem>();
            foreach (string relFolder in folderPaths)
            {
                var slash = relFolder.LastIndexOf('/');
                var parent = slash < 0 ? string.Empty : relFolder.Substring(0, slash);
                var name = slash < 0 ? relFolder : relFolder.Substring(slash + 1);

                var folderItem = new ContentFolderMenuItem(tree, name, root + "/" + relFolder, info, editor);
                AddChild(tree, folderItems, parent, folderItem);
                folderItems[relFolder] = folderItem;
            }

            foreach (var (asset, relFolder) in inRoot)
            {
                var leaf = ContentAssetMenuItem.Create(tree, asset, info, editor, stray: false);
                AddChild(tree, folderItems, relFolder, leaf);
            }

            // The "Other" bucket is flat, not path-mirrored: the point is to look wrong and get fixed.
            if (strays.Count > 0)
            {
                var other = new ContentFolderMenuItem(tree, OTHER_FOLDER_NAME, null, info, editor, isOther: true);
                tree.MenuItems.Add(other);
                foreach (DataObject stray in strays)
                    other.ChildMenuItems.Add(ContentAssetMenuItem.Create(tree, stray, info, editor, stray: true));
            }

            ValidateFolderItems(tree);
            Sort(tree);
        }

        private static void AddChild(OdinMenuTree tree, Dictionary<string, ContentFolderMenuItem> folders,
            string parentRelFolder, OdinMenuItem child)
        {
            if (string.IsNullOrEmpty(parentRelFolder)) tree.MenuItems.Add(child);
            else folders[parentRelFolder].ChildMenuItems.Add(child);
        }

        private static void AddFolderChain(ISet<string> folders, string relative)
        {
            if (string.IsNullOrEmpty(relative)) return;
            var i = -1;
            while ((i = relative.IndexOf('/', i + 1)) >= 0)
            {
                folders.Add(relative.Substring(0, i));
            }
            folders.Add(relative);
        }

        private static void CollectDiskFolders(string root, string current, ISet<string> folders)
        {
            if (!AssetDatabase.IsValidFolder(current)) return;
            foreach (var sub in AssetDatabase.GetSubFolders(current))
            {
                folders.Add(sub.Substring(root.Length + 1));
                CollectDiskFolders(root, sub, folders);
            }
        }

        /// <summary>Catches the one failure mode a hand-built tree shouldn't have: a plain menu
        /// item masquerading as a folder. Kept as a permanent guard in case a custom editor's
        /// BuildMenu mixes in raw <see cref="OdinMenuTree.Add(string,object)"/> calls.</summary>
        private static void ValidateFolderItems(OdinMenuTree tree)
        {
            foreach (var item in tree.EnumerateTree())
            {
                if (item.Value == null && item is not ContentFolderMenuItem)
                {
                    Debug.LogWarning($"[Content] '{item.GetFullPath()}' is a plain menu item and " +
                                     "will not accept drops.");
                }
            }
        }

        // -----------------------------------------------------------------------
        // Sort — folders first, "Other" pinned last, natural alphabetical
        // -----------------------------------------------------------------------

        /// <summary>Folders before assets, the "Other" bucket always last, otherwise number-aware
        /// alphabetical so "Item 2" precedes "Item 10".</summary>
        internal static readonly Comparison<OdinMenuItem> Comparison = (a, b) =>
        {
            var aOther = a is ContentFolderMenuItem { IsOther: true };
            var bOther = b is ContentFolderMenuItem { IsOther: true };
            if (aOther != bOther) return aOther ? 1 : -1;

            var aFolder = a is ContentFolderMenuItem;
            var bFolder = b is ContentFolderMenuItem;
            if (aFolder != bFolder) return aFolder ? -1 : 1;

            return EditorUtility.NaturalCompare(a.SmartName ?? string.Empty, b.SmartName ?? string.Empty);
        };

        private static void Sort(OdinMenuTree tree) => SortRecursive(tree.MenuItems);

        private static void SortRecursive(List<OdinMenuItem> items)
        {
            items.Sort(Comparison);
            foreach (var item in items)
            {
                SortRecursive(item.ChildMenuItems);
            }
        }
    }
}
