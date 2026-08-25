using System.Collections.Generic;
using System.Linq;
using GI.UnityToolkit.Variables;
using Sirenix.OdinInspector;
using UnityEditor;
using UnityEngine;

namespace GI.UnityToolkit.Content.Editor
{
    /// <summary>
    /// Passed to <see cref="ContentEditor.DrawToolbarLeft"/> / <see cref="ContentEditor.DrawToolbarRight"/>.
    /// Exposes the current selection plus the same operations the built-in buttons use, so a
    /// custom editor can add buttons without reimplementing CRUD. Every mutating member defers via
    /// <see cref="ContentEditorWindow.Defer"/> — safe to call from inside OnGUI.
    /// </summary>
    public readonly struct ContentToolbar
    {
        public ContentEditorWindow Window { get; }
        public ContentTypeInfo TypeInfo { get; }
        public ContentEditor Editor { get; }
        public IReadOnlyList<DataObject> Selection { get; }

        public int SelectionCount => Selection.Count;
        public DataObject Primary => Selection.Count > 0 ? Selection[0] : null;

        /// <summary>The single selected real folder, if that's what's selected (never the
        /// synthetic "Other" bucket). Null whenever an asset — or nothing, or more than one item —
        /// is selected instead.</summary>
        internal ContentFolderMenuItem SelectedFolder { get; }

        /// <summary>Whether <see cref="Rename"/> has something to act on: exactly one asset, or a
        /// single selected folder.</summary>
        public bool CanRename => SelectionCount == 1 || (SelectionCount == 0 && SelectedFolder != null);

        /// <summary>Folder new assets land in: the selected folder, the selected asset's folder,
        /// or the type root.</summary>
        public string TargetFolder { get; }

        internal ContentToolbar(ContentEditorWindow window, ContentTypeInfo typeInfo,
            ContentEditor editor, IReadOnlyList<DataObject> selection,
            string targetFolder, ContentFolderMenuItem selectedFolder)
        {
            Window = window;
            TypeInfo = typeInfo;
            Editor = editor;
            Selection = selection;
            TargetFolder = targetFolder;
            SelectedFolder = selectedFolder;
        }

        public void Create()
        {
            var typeInfo = TypeInfo;
            var editor = Editor;
            var window = Window;
            var folder = TargetFolder;
            window.Defer(() =>
            {
                var asset = ContentAssetOps.Create(typeInfo, folder);
                editor.NotifyCreated(asset);
                window.SelectAssetAfterRebuild(asset);
            });
        }

        public void CreateFolder()
        {
            var window = Window;
            var folder = TargetFolder;
            window.Defer(() => ContentAssetOps.CreateFolder(folder));
        }

        public void Duplicate()
        {
            var source = Primary;
            if (source == null) return;
            var typeInfo = TypeInfo;
            var editor = Editor;
            var window = Window;
            window.Defer(() =>
            {
                var copy = ContentAssetOps.Duplicate(typeInfo, source);
                if (copy == null) return;
                editor.NotifyDuplicated(source, copy);
                window.SelectAssetAfterRebuild(copy);
            });
        }

        public void Rename(Rect anchor)
        {
            if (SelectionCount == 0 && SelectedFolder != null)
            {
                Window.ShowRenameFolderPopup(SelectedFolder, anchor);
                return;
            }
            var asset = Primary;
            if (asset == null) return;
            Window.ShowRenameAssetPopup(TypeInfo, Editor, asset, anchor);
        }

        public void Delete()
        {
            if (Selection.Count == 0) return;
            var typeInfo = TypeInfo;
            var editor = Editor;
            var window = Window;
            var assets = Selection.ToList();
            window.Defer(() =>
            {
                // Prune AFTER the delete, once the assets are actually destroyed — pruning first
                // would find nothing dead yet, since a UnityEngine.Object isn't null until then.
                ContentAssetOps.Delete(typeInfo, editor, assets);
                editor.PruneDetailTargets();
            });
        }

        public void Reveal()
        {
            if (Primary != null) EditorGUIUtility.PingObject(Primary);
        }

        public void SelectAsset(DataObject asset) => Window.SelectAssetAfterRebuild(asset);
        public void RequestRebuild() => Window.Defer(() => { });
    }

    /// <summary>
    /// Lightweight detail-pane stand-in for multi-select.
    /// Drawn in the right-hand pane instead of one heavy view per asset when more than
    /// one item is selected — a full PropertyTree (or worse, a paintable hex grid) per selected
    /// asset is a visible perf cliff, so multi-select gets a summary instead.
    /// </summary>
    public sealed class ContentMultiSelection
    {
        [HideLabel, TextArea(3, 20), ReadOnly]
        public string Summary { get; }

        public ContentMultiSelection(IReadOnlyList<DataObject> assets, ContentTypeInfo info)
        {
            Summary = assets.Count == 0
                ? "Nothing selected."
                : $"{assets.Count} {info?.DisplayName ?? "items"} selected:\n" +
                  string.Join("\n", assets.Take(20).Select(a => "  • " + (a != null ? a.name : "(missing)"))) +
                  (assets.Count > 20 ? $"\n  …and {assets.Count - 20} more" : string.Empty);
        }
    }
}
