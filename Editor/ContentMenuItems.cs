using System.Collections.Generic;
using GI.UnityToolkit.Variables;
using Sirenix.OdinInspector.Editor;
using Sirenix.Utilities.Editor;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace GI.UnityToolkit.Content.Editor
{
    /// <summary>
    /// Common surface <see cref="ContentDragDrop"/> and <see cref="ContentAssetOps.Move"/>
    /// need from a menu item, regardless of whether it represents an asset or a folder.
    /// </summary>
    internal interface IContentMenuItem
    {
        string DiskPath { get; }
        bool IsFolder { get; }
        string DragLabel { get; }
    }
    
    /// <summary>
    /// One DataObject asset in the list
    /// </summary>
    internal sealed class ContentAssetMenuItem : OdinMenuItem, IContentMenuItem
    {
        public DataObject Asset { get; }
        public string DiskPath { get; }
        public bool IsFolder => false;
        public bool IsStray { get; }
        public string DragLabel => SmartName;

        private ContentAssetMenuItem(OdinMenuTree tree, string name, DataObject asset, string diskPath, bool isStray)
            : base(tree, name, asset)
        {
            Asset = asset;
            DiskPath = diskPath;
            IsStray = isStray;
        }

        public static ContentAssetMenuItem Create(OdinMenuTree tree, DataObject asset, ContentTypeInfo info, ContentEditor editor, bool stray)
        {
            string diskPath = AssetDatabase.GetAssetPath(asset);
            string label = editor != null ? editor.GetMenuLabelFor(asset) : asset.name;
            var item = new ContentAssetMenuItem(tree, label, asset, diskPath, stray)
            {
                SearchString = stray ? $"{label} {diskPath}" : label
            };

            Texture icon = editor?.GetMenuIconFor(asset);
            if (icon != null) item.Icon = icon;
            return item;
        }

        protected override void OnDrawMenuItem(Rect rect, Rect labelRect)
        {
            base.OnDrawMenuItem(rect, labelRect);
            ContentDragDrop.HandleDragSource(this, rect);
            if (IsStray) ContentMenuGUI.DrawWarningBadge(rect);
        }
    }
    
    /// <summary>
    /// A real disk subfolder, or the synthetic "Other" bucket.
    /// </summary>
    internal sealed class ContentFolderMenuItem : OdinMenuItem, IContentMenuItem
    {
        public string DiskPath { get; }
        public bool IsFolder => true;
        public bool IsOther { get; }
        public ContentTypeInfo TypeInfo { get; }
        public ContentEditor Editor { get; }
        public string DragLabel => SmartName;

        /// <summary>
        /// A real disk subfolder, or the synthetic "Other" bucket.
        /// </summary>
        /// <param name="tree"></param>
        /// <param name="name"></param>
        /// <param name="diskPath">Null for the synthetic "Other" bucket, which is not a real folder.</param>
        /// <param name="info"></param>
        /// <param name="editor"></param>
        /// <param name="isOther"></param>
        public ContentFolderMenuItem(OdinMenuTree tree, string name, string diskPath, ContentTypeInfo info, ContentEditor editor, bool isOther = false)
            : base(tree, name, null)
        {
            DiskPath = diskPath;
            TypeInfo = info;
            Editor = editor;
            IsOther = isOther;
            Icon = ContentMenuGUI.FolderIcon;
            OnRightClick += _ => ShowContextMenu();
        }

        protected override void OnDrawMenuItem(Rect rect, Rect labelRect)
        {
            base.OnDrawMenuItem(rect, labelRect);
            if (IsOther)
            {
                ContentDragDrop.RejectDrop(rect);
                ContentMenuGUI.DrawWarningBadge(rect);
                return;
            }
            ContentDragDrop.HandleDragSource(this, rect);
            ContentDragDrop.HandleFolderDrop(this, rect);
        }

        private void ShowContextMenu()
        {
            if (IsOther) return;
            var window = Editor?.Window;
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("New Subfolder"), false,
                () => window?.Defer(() => ContentAssetOps.CreateFolder(DiskPath)));
            menu.AddItem(new GUIContent("Rename Folder"), false,
                () => window?.ShowRenameFolderPopup(this, Rect));
            menu.AddItem(new GUIContent("Delete Folder"), false,
                () => window?.Defer(() => ContentAssetOps.DeleteFolder(DiskPath)));
            menu.AddSeparator(string.Empty);
            menu.AddItem(new GUIContent("Show in Project"), false,
                () => Selection.activeObject = AssetDatabase.LoadAssetAtPath<Object>(DiskPath));
            menu.ShowAsContext();
        }
    }
    
    /// <summary>
    /// Small shared drawing helpers.
    /// </summary>
    internal static class ContentMenuGUI
    {
        public static Texture FolderIcon => EditorIcons.UnityFolderIcon;

        private static readonly Dictionary<Color, Texture2D> DotCache = new();

        /// <summary>A small filled circle icon, used by per-asset list icons (e.g. pass/fail,
        /// split/single) that need to stay truthful without a real on-disk folder to back them.</summary>
        public static Texture DotIcon(Color color)
        {
            if (DotCache.TryGetValue(color, out var cached) && cached != null) return cached;

            const int size = 16;
            const float radius = size * 0.4f;
            var tex = new Texture2D(size, size) { hideFlags = HideFlags.HideAndDontSave };
            var pixels = new Color[size * size];
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var dx = x - (size - 1) * 0.5f;
                    var dy = y - (size - 1) * 0.5f;
                    pixels[y * size + x] = dx * dx + dy * dy <= radius * radius ? color : new Color(0f, 0f, 0f, 0f);
                }
            }

            tex.SetPixels(pixels);
            tex.Apply();
            DotCache[color] = tex;
            return tex;
        }

        public static void DrawWarningBadge(Rect itemRect)
        {
            if (Event.current.type != EventType.Repaint) return;
            
            Texture icon = EditorIcons.AlertTriangle.Raw;
            if (icon == null) return;
            
            var badgeRect = new Rect(itemRect.xMax - 18f, itemRect.y + (itemRect.height - 14f) * 0.5f, 14f, 14f);
            GUI.DrawTexture(badgeRect, icon);
        }
    }
}
