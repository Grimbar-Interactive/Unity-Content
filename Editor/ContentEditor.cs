using System;
using System.Collections.Generic;
using GI.UnityToolkit.Variables;
using Sirenix.OdinInspector.Editor;
using UnityEngine;

namespace GI.UnityToolkit.Content.Editor
{
    /// <summary>
    /// Non-generic bridge for <see cref="ContentEditor{T}"/>. The <see cref="ContentEditorWindow"/>
    /// only ever talks to this type; custom editors derive from the generic form.
    /// </summary>
    public abstract class ContentEditor : IDisposable
    {
        public ContentTypeInfo TypeInfo { get; internal set; }
        public ContentEditorWindow Window { get; internal set; }
        public abstract Type ContentType { get; }


        #region List
        // -----------------------------------------------------------------------
        // Left list — override BuildMenu to take over the list entirely
        // -----------------------------------------------------------------------
        
        /// <summary>
        /// Populates the left-hand menu tree. The default builds the folder-nested,
        /// alphabetically sorted list with the "Other" bucket. Override to replace the list
        /// wholesale; the detail pane and toolbar are unaffected.
        /// </summary>
        public virtual void BuildMenu(OdinMenuTree tree) => ContentTreeBuilder.Build(tree, TypeInfo, this);

        public virtual float MenuWidth => 260f;
        public virtual bool SupportsFolders => true;
        public virtual bool SupportsMultiSelect => true;
        #endregion

        #region Toolbar
        // -----------------------------------------------------------------------
        // Toolbar — additive by default, fully replaceable if wanted
        // -----------------------------------------------------------------------

        /// <summary>
        /// Set false to suppress the built-in Create / New Folder / Duplicate / Rename /
        /// Delete buttons and supply your own from <see cref="DrawToolbarRight"/>.
        /// </summary>
        public virtual bool DrawDefaultToolbar => true;

        /// <summary>
        /// Extra controls drawn immediately after the type dropdown, before the flexible space.
        /// </summary>
        public virtual void DrawToolbarLeft(ContentToolbar toolbar) { }

        /// <summary>
        /// Extra controls drawn after the built-in buttons.
        /// </summary>
        public virtual void DrawToolbarRight(ContentToolbar toolbar) { }
        #endregion

        #region Internal
        // -----------------------------------------------------------------------
        // Internal bridge to the strongly typed ContentEditor<T>
        // -----------------------------------------------------------------------
        internal abstract object GetDetailTargetFor(DataObject asset);
        internal abstract void PruneDetailTargets();
        internal abstract string GetMenuLabelFor(DataObject asset);
        internal abstract Texture GetMenuIconFor(DataObject asset);
        internal abstract void NotifyCreated(DataObject asset);
        internal abstract void NotifyDuplicated(DataObject source, DataObject copy);
        internal abstract void NotifyRenamed(DataObject asset, string previousName);
        internal abstract string GetDeleteWarningFor(IReadOnlyList<DataObject> assets);
        internal abstract bool ConfirmDeleteFor(IReadOnlyList<DataObject> assets);
        #endregion

        public virtual void Dispose() { }
    }

    /// <summary>
    /// Default editor for a <see cref="DataObject"/> tagged <see cref="ContentAttribute"/>.
    /// Derive and override <see cref="CreateDetailView"/> to replace the right-hand pane, and/or
    /// <see cref="ContentEditor.BuildMenu"/> to replace the left-hand list. The two are
    /// independent — overriding one leaves the other at its default.
    /// </summary>
    // ReSharper disable once ClassWithVirtualMembersNeverInherited.Global
    public class ContentEditor<T> : ContentEditor where T : DataObject
    {
        public sealed override Type ContentType => typeof(T);

        // Keyed by instance id, not by the object: UnityEngine.Object overrides == to conflate
        // destroyed-with-null but not Equals/GetHashCode, which would leave unremovable entries.
        private readonly Dictionary<EntityId, (T asset, object view)> _detailTargets = new();

        /// <summary>
        /// Returns the object Odin draws in the right-hand pane. The default returns the
        /// asset itself, which Odin renders with its normal inspector. Return a view POCO decorated
        /// with Odin layout attributes to take over the pane. The instance is cached per asset for
        /// the lifetime of this editor, so it may hold selection state and a long-lived
        /// <see cref="Sirenix.OdinInspector.Editor.PropertyTree"/>; it is disposed with the editor
        /// if it implements <see cref="IDisposable"/>.
        /// </summary>
        protected virtual object CreateDetailView(T asset) => asset;

        /// <summary>
        /// List label. Defaults to the asset file name, which is what Rename edits.
        /// </summary>
        protected virtual string GetMenuLabel(T asset) => asset.name;

        /// <summary>
        /// Optional per-asset list icon. A cheap way to surface a discriminator
        /// without folder grouping.
        /// </summary>
        protected virtual Texture GetMenuIcon(T asset) => null;

        /// <summary>
        /// Called after the asset exists on disk and its name/id have been written.
        /// </summary>
        protected virtual void OnCreated(T asset) { }
        protected virtual void OnDuplicated(T source, T copy) { }
        protected virtual void OnRenamed(T asset, string previousName) { }

        /// <summary>
        /// Extra line appended to the delete confirmation dialog, e.g. dependency warnings.
        /// </summary>
        protected virtual string GetDeleteWarning(IReadOnlyList<T> assets) => null;

        /// <summary>
        /// Last chance to cascade or veto a deletion. Return false to abort. The generic
        /// "cannot be undone" dialog has already been accepted at this point.
        /// </summary>
        protected virtual bool OnConfirmDelete(IReadOnlyList<T> assets) => true;

        // -----------------------------------------------------------------------
        // Internal bridge
        // -----------------------------------------------------------------------
        internal sealed override object GetDetailTargetFor(DataObject asset)
        {
            if (asset is not T typed) return asset;
            var id = typed.GetEntityId();
            if (_detailTargets.TryGetValue(id, out var entry) && entry.view != null) return entry.view;
            var view = CreateDetailView(typed) ?? typed;
            _detailTargets[id] = (typed, view);
            return view;
        }

        internal sealed override void PruneDetailTargets()
        {
            List<EntityId> dead = null;
            foreach (var kv in _detailTargets)
            {
                if (kv.Value.asset == null) (dead ??= new List<EntityId>()).Add(kv.Key);
            }

            if (dead == null) return;
            foreach (var id in dead)
            {
                if (_detailTargets[id].view is IDisposable d) d.Dispose();
                _detailTargets.Remove(id);
            }
        }

        internal sealed override string GetMenuLabelFor(DataObject a) => a is T t ? GetMenuLabel(t) : a.name;
        internal sealed override Texture GetMenuIconFor(DataObject a) => a is T t ? GetMenuIcon(t) : null;
        internal sealed override void NotifyCreated(DataObject a) { if (a is T t) OnCreated(t); }
        internal sealed override void NotifyRenamed(DataObject a, string previous) { if (a is T t) OnRenamed(t, previous); }

        internal sealed override void NotifyDuplicated(DataObject source, DataObject copy)
        {
            if (source is T st && copy is T ct) OnDuplicated(st, ct);
        }

        internal sealed override string GetDeleteWarningFor(IReadOnlyList<DataObject> assets) => GetDeleteWarning(Cast(assets));
        internal sealed override bool ConfirmDeleteFor(IReadOnlyList<DataObject> assets) => OnConfirmDelete(Cast(assets));

        private static IReadOnlyList<T> Cast(IReadOnlyList<DataObject> assets)
        {
            var list = new List<T>(assets.Count);
            foreach (var dataObject in assets)
            {
                if (dataObject is T t) list.Add(t);
            }

            return list;
        }

        public override void Dispose()
        {
            foreach (var entry in _detailTargets.Values)
            {
                if (entry.view is IDisposable d) d.Dispose();
            }

            _detailTargets.Clear();
        }
    }
}
