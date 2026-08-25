using System;
using System.Collections.Generic;
using System.Linq;
using GI.UnityToolkit.Variables;
using Sirenix.OdinInspector.Editor;
using Sirenix.Utilities.Editor;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace GI.UnityToolkit.Content.Editor
{
    /// <summary>
    /// Drag-and-drop for the Content list: assets and folders can be dragged into
    /// any real folder or the type root, and interoperate with the Project window and object
    /// fields via Unity's own <see cref="DragAndDrop"/> API.
    /// </summary>
    internal static class ContentDragDrop
    {
        private const string DragKey = "GI.UnityToolkit.Content.Drag";
        private const float DragThreshold = 6f;
        private static readonly Color HighlightColor = new(0.40f, 0.60f, 1.00f);

        private static OdinMenuItem _pressedItem;
        private static Vector2 _pressPosition;
        private static object _hoverTarget;
        private static readonly object RootTarget = new();

        // -----------------------------------------------------------------------
        // Drag source — asset items and real folder items
        // -----------------------------------------------------------------------
        public static void HandleDragSource(OdinMenuItem item, Rect rect)
        {
            if (item is not IContentMenuItem) return;
            var ev = Event.current;
            switch (ev.type)
            {
                case EventType.MouseDown when ev.button == 0 && rect.Contains(ev.mousePosition):
                    // Do NOT Use() — Odin still needs this MouseDown to change the selection.
                    _pressedItem = item;
                    _pressPosition = ev.mousePosition;
                    break;

                case EventType.MouseDrag when _pressedItem == item && Vector2.Distance(ev.mousePosition, _pressPosition) > DragThreshold:
                    if (StartDrag(item)) ev.Use(); // suppress Odin's own drag-select once a drag actually starts
                    _pressedItem = null;
                    break;

                case EventType.MouseUp:
                case EventType.DragExited:
                    _pressedItem = null;
                    _hoverTarget = null;
                    break;
            }
        }

        private static bool StartDrag(OdinMenuItem item)
        {
            var payload = BuildPayload(item);
            if (payload.Count == 0) return false;

            DragAndDrop.PrepareStartDrag();
            DragAndDrop.objectReferences = payload
                .OfType<ContentAssetMenuItem>()
                .Select(i => (Object)i.Asset).ToArray();
            DragAndDrop.paths = payload.Select(i => i.DiskPath).Where(p => !string.IsNullOrEmpty(p)).ToArray();
            DragAndDrop.SetGenericData(DragKey, payload);
            DragAndDrop.StartDrag(payload.Count == 1 ? payload[0].DragLabel : $"{payload.Count} items");
            return true;
        }

        /// <summary>
        /// Dragging a selected item drags the whole selection; dragging an unselected item drags just that one.
        /// </summary>
        private static List<IContentMenuItem> BuildPayload(OdinMenuItem item)
        {
            var result = new List<IContentMenuItem>();
            if (item.IsSelected && item.MenuTree?.Selection is { Count: > 1 })
            {
                foreach (var selected in item.MenuTree.Selection)
                {
                    if (selected is IContentMenuItem candidate && !string.IsNullOrEmpty(candidate.DiskPath))
                    {
                        result.Add(candidate);
                    }
                }
            }

            if (result.Count == 0 && item is IContentMenuItem single && !string.IsNullOrEmpty(single.DiskPath))
            {
                result.Add(single);
            }

            return result;
        }

        // -----------------------------------------------------------------------
        // Drop targets — real folder items, and (from the window) the empty area below the list
        // -----------------------------------------------------------------------
        public static void HandleFolderDrop(ContentFolderMenuItem folder, Rect rect)
        {
            HandleDrop(rect, folder.DiskPath, folder, folder.TypeInfo);
        }

        public static void HandleRootDrop(Rect rect, ContentTypeInfo info)
        {
            HandleDrop(rect, info?.RootFolder, RootTarget, info);
        }

        /// <summary>
        /// The "Other" bucket must actively reject a drag, not merely ignore it — an
        /// unhandled event falls through to the root drop zone drawn behind it, which would
        /// otherwise silently move the asset to the type root instead.
        /// </summary>
        public static void RejectDrop(Rect rect)
        {
            var ev = Event.current;
            if (ev.type != EventType.DragUpdated && ev.type != EventType.DragPerform) return;
            if (!rect.Contains(ev.mousePosition)) return;
            DragAndDrop.visualMode = DragAndDropVisualMode.Rejected;
            ev.Use();
        }

        private static void HandleDrop(Rect rect, string destination, object target, ContentTypeInfo info)
        {
            var ev = Event.current;

            if (ev.type == EventType.Repaint)
            {
                if (ReferenceEquals(_hoverTarget, target))
                {
                    SirenixEditorGUI.DrawBorders(rect, 2, HighlightColor);
                }
                
                return;
            }
            if (ev.type == EventType.DragExited) { _hoverTarget = null; return; }
            if (ev.type != EventType.DragUpdated && ev.type != EventType.DragPerform) return;
            if (!rect.Contains(ev.mousePosition)) return;

            var payload = ResolvePayload(info);
            var valid = payload != null && CanDrop(payload, destination);

            if (ev.type == EventType.DragUpdated)
            {
                DragAndDrop.visualMode = valid ? DragAndDropVisualMode.Move : DragAndDropVisualMode.Rejected;
                _hoverTarget = valid ? target : null;
                ev.Use();
                return;
            }

            // DragPerform
            _hoverTarget = null;
            if (!valid) return;
            DragAndDrop.AcceptDrag();
            var items = payload.ToList();
            var dest = destination;
            
            // Never mutate the AssetDatabase inside a GUI event — this frame's layout groups are
            // already committed and MoveAsset can trigger a reimport mid-frame.
            EditorApplication.delayCall += () => ContentAssetOps.Move(items, dest);
            ev.Use();
        }

        /// <summary>
        /// Internal drags carry generic data. A drag arriving from the Project window
        /// carries only objectReferences; accept it when every object belongs to the type on
        /// screen, so assets can be filed straight from the Project window too.
        /// </summary>
        private static List<IContentMenuItem> ResolvePayload(ContentTypeInfo info)
        {
            if (DragAndDrop.GetGenericData(DragKey) is List<IContentMenuItem> internalPayload)
                return internalPayload;

            var refs = DragAndDrop.objectReferences;
            if (info == null || refs == null || refs.Length == 0) return null;

            var result = new List<IContentMenuItem>(refs.Length);
            foreach (var obj in refs)
            {
                if (obj is not DataObject data) return null;
                if (ContentTypeRegistry.FindNearest(data.GetType()) != info) return null;
                result.Add(new ExternalDragItem(data));
            }
            return result;
        }

        /// <summary>
        /// Accepts the drop as long as at least one item would actually move. A
        /// multi-selection spanning several folders commonly has a few members already sitting in
        /// the destination — those are just skipped (see <see cref="ContentAssetOps.Move"/>),
        /// not treated as a reason to reject the whole batch. A folder onto itself or into its own
        /// descendant is always a hard rejection, regardless of the rest of the selection.
        /// </summary>
        private static bool CanDrop(IReadOnlyList<IContentMenuItem> payload, string destination)
        {
            if (string.IsNullOrEmpty(destination)) return false;

            var anyMovable = false;
            foreach (var item in payload)
            {
                var path = item.DiskPath;
                if (string.IsNullOrEmpty(path)) return false;

                if (item.IsFolder)
                {
                    if (destination == path) return false; // onto itself
                    if (destination.StartsWith(path + "/", StringComparison.Ordinal)) return false; // into own descendant
                }

                if (ContentAssetOps.ParentFolder(path) != destination) anyMovable = true;
            }
            return anyMovable;
        }

        /// <summary>
        /// Wraps a plain drag arriving from the Project window in the same shape our own
        /// drags carry, so <see cref="ContentAssetOps.Move"/> can treat both uniformly.
        /// </summary>
        private sealed class ExternalDragItem : IContentMenuItem
        {
            public string DiskPath { get; }
            public bool IsFolder => false;
            public string DragLabel { get; }

            public ExternalDragItem(DataObject asset)
            {
                DiskPath = AssetDatabase.GetAssetPath(asset);
                DragLabel = asset.name;
            }
        }
    }
}
