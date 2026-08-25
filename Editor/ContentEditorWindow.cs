using System;
using System.Collections.Generic;
using System.Linq;
using GI.UnityToolkit.Variables;
using Sirenix.OdinInspector.Editor;
using Sirenix.Utilities.Editor;
using UnityEditor;
using UnityEngine;

namespace GI.UnityToolkit.Content.Editor
{
    /// <summary>
    /// All-in-one editor for every <see cref="DataObject"/> type tagged <see cref="ContentAttribute"/>.
    /// Shows a type dropdown, a folder-nested alphabetical list of that type's assets on the left,
    /// and a detail editor on the right: by default the asset's own Odin inspector, or whatever a
    /// custom <see cref="ContentEditor{T}"/> provides. Open via: Grimbar Interactive ▸ Content
    /// </summary>
    public class ContentEditorWindow : OdinMenuEditorWindow
    {
        [SerializeField] private string _selectedTypeName;

        private ContentEditor _editor;
        private ContentTypeInfo _typeInfo;
        private Rect _renameButtonRect;

        [MenuItem("Grimbar Interactive/Content")]
        private static void Open()
        {
            var window = GetWindow<ContentEditorWindow>();
            window.titleContent = new GUIContent("Content");
            window.Show();
        }

        protected override void OnDestroy()
        {
            _editor?.Dispose();
            base.OnDestroy();
        }

        // -----------------------------------------------------------------------
        // Menu tree
        // -----------------------------------------------------------------------
        protected override OdinMenuTree BuildMenuTree()
        {
            EnsureEditor();

            var tree = new OdinMenuTree(supportsMultiSelect: _editor?.SupportsMultiSelect ?? false)
            {
                Config =
                {
                    DrawSearchToolbar = true,
                    UseCachedExpandedStates = true,
                    // False (Odin's default) so mousing down on an item that's already part of a
                    // multi-selection doesn't immediately collapse the selection to just that item
                    // before ContentDragDrop's drag threshold is reached — that collapse is
                    // exactly what would stop a multi-select drag from ever seeing the rest.
                    SelectMenuItemsOnMouseDown = false,
                    ConfirmSelectionOnDoubleClick = true
                }
            };
            tree.Selection.SelectionConfirmed += OnSelectionConfirmed;

            if (_editor == null) return tree;

            MenuWidth = _editor.MenuWidth;
            _editor.BuildMenu(tree);
            return tree;
        }

        /// <summary>
        /// Double-click on a single asset or real folder opens the rename popup right
        /// there, anchored on the item — the same rename path the toolbar button and F2 use.
        /// </summary>
        private void OnSelectionConfirmed(OdinMenuTreeSelection selection)
        {
            if (selection is not { Count: 1 }) return;
            var item = selection[0];

            if (item is ContentFolderMenuItem { IsOther: false } folder)
            {
                ShowRenameFolderPopup(folder, folder.Rect);
                return;
            }

            if (item is ContentAssetMenuItem { Asset: { } asset } && _typeInfo != null)
            {
                ShowRenameAssetPopup(_typeInfo, _editor, asset, item.Rect);
            }
        }

        protected override void DrawMenu()
        {
            var area = EditorGUILayout.BeginVertical();
            base.DrawMenu();
            EditorGUILayout.EndVertical();

            // Folder items handle their own rects inside base.DrawMenu(), so a drop on a folder
            // never reaches here — this only catches the empty space below the list.
            if (Event.current.type != EventType.Used && _typeInfo != null)
            {
                ContentDragDrop.HandleRootDrop(area, _typeInfo);
            }
        }

        protected override IEnumerable<object> GetTargets()
        {
            var selection = MenuTree?.Selection;
            if (_editor == null || selection == null || selection.Count == 0) yield break;

            // One heavy view per asset (a PropertyTree, or worse a paintable hex grid) is a perf
            // cliff on multi-select, so summarize instead. The toolbar still spans the selection.
            if (selection.Count > 1)
            {
                yield return new ContentMultiSelection(SelectedAssets(), _typeInfo);
                yield break;
            }

            foreach (var item in selection)
            {
                if (item.Value is DataObject asset)
                {
                    yield return _editor.GetDetailTargetFor(asset);
                }
            }
        }
        
        protected override void OnImGUI()
        {
            wantsMouseMove = true;
            DrawHeader();
            base.OnImGUI();
        }

        private void DrawHeader()
        {
            var types = ContentTypeRegistry.AllTypes;
            if (types.Count == 0)
            {
                EditorGUILayout.HelpBox("No types are tagged [Content].", MessageType.Info);
                return;
            }

            SirenixEditorGUI.BeginHorizontalToolbar();
            DrawTypeDropdown(types);

            if (_editor == null)
            {
                GUILayout.FlexibleSpace();
                SirenixEditorGUI.EndHorizontalToolbar();
                return;
            }

            var selection = SelectedAssets();
            var toolbar = new ContentToolbar(this, _typeInfo, _editor, selection,
                ResolveTargetFolder(), ResolveSelectedFolder());

            _editor.DrawToolbarLeft(toolbar);
            GUILayout.FlexibleSpace();

            if (_editor.DrawDefaultToolbar) DrawDefaultButtons(toolbar);
            _editor.DrawToolbarRight(toolbar);
            SirenixEditorGUI.EndHorizontalToolbar();

            HandleShortcuts(toolbar);
        }

        private void DrawTypeDropdown(IReadOnlyList<ContentTypeInfo> types)
        {
            var labels = new string[types.Count];
            var currentIndex = 0;
            for (var i = 0; i < types.Count; i++)
            {
                labels[i] = string.IsNullOrEmpty(types[i].Category)
                    ? types[i].DisplayName
                    : $"{types[i].Category}/{types[i].DisplayName}";
                if (types[i] == _typeInfo) currentIndex = i;
            }

            var newIndex = EditorGUILayout.Popup(currentIndex, labels, EditorStyles.toolbarPopup, GUILayout.Width(180f));
            if (newIndex == currentIndex) return;

            ActivateType(types[newIndex]);
            ForceMenuTreeRebuild();
        }

        private void DrawDefaultButtons(ContentToolbar toolbar)
        {
            var hasSelection = toolbar.SelectionCount > 0;
            var singleSelection = toolbar.SelectionCount == 1;

            if (SirenixEditorGUI.ToolbarButton("Create")) toolbar.Create();

            if (_editor.SupportsFolders && SirenixEditorGUI.ToolbarButton("New Folder"))
            {
                toolbar.CreateFolder();
            }

            bool duplicateClicked;
            using (new EditorGUI.DisabledScope(!singleSelection))
            {
                duplicateClicked = SirenixEditorGUI.ToolbarButton("Duplicate");
            }

            if (duplicateClicked && singleSelection) toolbar.Duplicate();

            bool renameClicked;
            using (new EditorGUI.DisabledScope(!toolbar.CanRename))
            {
                renameClicked = SirenixEditorGUI.ToolbarButton("Rename");
            }

            _renameButtonRect = GUILayoutUtility.GetLastRect();
            if (renameClicked && toolbar.CanRename) toolbar.Rename(_renameButtonRect);

            bool deleteClicked;
            using (new EditorGUI.DisabledScope(!hasSelection))
            {
                deleteClicked = SirenixEditorGUI.ToolbarButton("Delete");
            }

            if (deleteClicked && hasSelection) toolbar.Delete();
        }

        private void HandleShortcuts(ContentToolbar toolbar)
        {
            if (EditorGUIUtility.editingTextField) return;
            var ev = Event.current;
            if (ev.type != EventType.KeyDown || ev.keyCode != KeyCode.F2) return;
            if (!toolbar.CanRename) return;

            toolbar.Rename(_renameButtonRect);
            ev.Use();
        }

        // -----------------------------------------------------------------------
        // Selection / type-switch helpers
        // -----------------------------------------------------------------------
        private List<DataObject> SelectedAssets()
        {
            var list = new List<DataObject>();
            if (MenuTree?.Selection == null) return list;
            foreach (var item in MenuTree.Selection)
            {
                if (item.Value is DataObject asset) list.Add(asset);
            }

            return list;
        }

        /// <summary>
        /// The folder a new asset should land in: the selected folder, the selected
        /// asset's folder, or the type root.
        /// </summary>
        private string ResolveTargetFolder()
        {
            var selection = MenuTree?.Selection;
            var selected = selection is { Count: 1 } ? selection[0] : null;

            return selected switch
            {
                ContentFolderMenuItem { IsOther: false } folder => folder.DiskPath,
                ContentAssetMenuItem { IsStray: false } assetItem => ContentAssetOps.ParentFolder(assetItem.DiskPath),
                _ => _typeInfo?.RootFolder
            };
        }

        /// <summary>The single selected real folder, or null if the selection is an asset, empty,
        /// multiple items, or the synthetic "Other" bucket.</summary>
        private ContentFolderMenuItem ResolveSelectedFolder()
        {
            var selection = MenuTree?.Selection;
            if (selection is not { Count: 1 }) return null;
            return selection[0] is ContentFolderMenuItem { IsOther: false } folder ? folder : null;
        }

        private void EnsureEditor()
        {
            var types = ContentTypeRegistry.AllTypes;
            if (types.Count == 0)
            {
                _editor?.Dispose();
                _editor = null;
                _typeInfo = null;
                return;
            }

            ContentTypeInfo info = null;
            if (!string.IsNullOrEmpty(_selectedTypeName))
            {
                info = types.FirstOrDefault(t => t.ContentType.FullName == _selectedTypeName);
            }

            info ??= types[0];

            if (_editor != null && _typeInfo == info) return;
            ActivateType(info);
        }

        private void ActivateType(ContentTypeInfo info)
        {
            _editor?.Dispose();
            _typeInfo = info;
            _selectedTypeName = info?.ContentType.FullName;
            _editor = info != null ? ContentTypeRegistry.CreateEditor(info) : null;
            if (_editor != null) _editor.Window = this;
        }

        // -----------------------------------------------------------------------
        // Deferred mutation — every AssetDatabase write goes through here
        // -----------------------------------------------------------------------

        /// <summary>Runs <paramref name="operation"/> next editor tick, then rebuilds the menu
        /// tree and repaints. AssetDatabase mutation during a GUI event throws (this frame's
        /// layout groups are already committed) and can re-enter BuildMenuTree mid-draw.</summary>
        internal void Defer(Action operation)
        {
            EditorApplication.delayCall += () =>
            {
                if (this == null) return;   // window destroyed between frames
                operation();
                ForceMenuTreeRebuild();
                Repaint();
            };
        }

        /// <summary>
        /// Selects <paramref name="asset"/> once the rebuild triggered by the enclosing
        /// <see cref="Defer"/> call has happened, and expands its folder ancestors — Odin's
        /// expanded-state cache is keyed by path, so a moved/renamed item can otherwise reappear
        /// collapsed.
        /// </summary>
        internal void SelectAssetAfterRebuild(DataObject asset)
        {
            EditorApplication.delayCall += () =>
            {
                if (this == null || asset == null) return;
                TrySelectMenuItemWithObject(asset);
                ExpandAncestorsOf(asset);
            };
        }

        internal void ShowRenameFolderPopup(ContentFolderMenuItem folder, Rect anchor)
        {
            var path = folder.DiskPath;
            PopupWindow.Show(anchor, new RenameAssetPopup(folder.Name,
                newName => Defer(() => ContentAssetOps.RenameFolder(path, newName))));
        }

        internal void ShowRenameAssetPopup(ContentTypeInfo typeInfo, ContentEditor editor, DataObject asset, Rect anchor)
        {
            PopupWindow.Show(anchor, new RenameAssetPopup(asset.name,
                newName => Defer(() => ContentAssetOps.Rename(typeInfo, editor, asset, newName))));
        }

        private void ExpandAncestorsOf(DataObject asset)
        {
            if (MenuTree == null) return;
            foreach (var item in MenuTree.EnumerateTree())
            {
                if (!ReferenceEquals(item.Value, asset)) continue;
                for (var p = item.Parent; p != null; p = p.Parent)
                {
                    p.Toggled = true;
                }
                break;
            }
        }
    }
}
