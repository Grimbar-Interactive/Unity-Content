using System;
using System.Collections.Generic;
using System.Linq;
using GI.UnityToolkit.Variables;
using UnityEditor;

// ReSharper disable UnusedAutoPropertyAccessor.Global

namespace GI.UnityToolkit.Content.Editor
{
    /// <summary>
    /// Resolved, ready-to-use info for a single type tagged <see cref="ContentAttribute"/>.
    /// </summary>
    public sealed class ContentTypeInfo
    {
        public Type ContentType { get; }
        public ContentAttribute Attribute { get; }

        public string DisplayName { get; }
        public string FolderName { get; }
        public string NameProperty { get; }
        public string IdProperty { get; }
        public string DefaultAssetName { get; }
        public string Category { get; }
        public int Order { get; }

        /// <summary>
        /// Computed on every access so a change to <see cref="ContentSettings.ContentRoot"/>
        /// can never leave this stale.
        /// </summary>
        public string RootFolder => ContentSettings.instance.ContentRoot.TrimEnd('/') + "/" + FolderName;

        public ContentTypeInfo(Type contentType, ContentAttribute attribute)
        {
            ContentType = contentType;
            Attribute = attribute;

            DisplayName = string.IsNullOrEmpty(attribute.DisplayName)
                ? Pluralize(ObjectNames.NicifyVariableName(contentType.Name))
                : attribute.DisplayName;
            FolderName = string.IsNullOrEmpty(attribute.FolderName) ? DisplayName : attribute.FolderName;
            NameProperty = attribute.NameProperty;
            IdProperty = attribute.IdProperty;
            DefaultAssetName = string.IsNullOrEmpty(attribute.DefaultAssetName)
                ? "New " + ObjectNames.NicifyVariableName(contentType.Name)
                : attribute.DefaultAssetName;
            Category = attribute.Category;
            Order = attribute.Order;
        }

        private static string Pluralize(string name)
        {
            if (string.IsNullOrEmpty(name) || name.EndsWith("s")) return name;
            return name.EndsWith("y") ? name.Substring(0, name.Length - 1) + "ies" : name + "s";
        }
    }

    /// <summary>
    /// Discovers every <see cref="ContentAttribute"/>-tagged <see cref="DataObject"/> type and
    /// any custom <see cref="ContentEditor{T}"/> that overrides one of them. Built lazily on
    /// first access from GUI code — never from a static initializer, since <see cref="TypeCache"/>
    /// is only valid once the domain has finished loading.
    /// </summary>
    public static class ContentTypeRegistry
    {
        private static List<ContentTypeInfo> _all;
        private static Dictionary<Type, Type> _editorTypeByContentType;

        public static IReadOnlyList<ContentTypeInfo> AllTypes
        {
            get { Ensure(); return _all; }
        }

        /// <summary>Walks up the base-type chain from <paramref name="assetType"/> to find the
        /// nearest registered type. <c>t:Name</c> asset queries match subclasses too, so a query for
        /// an abstract base's assets must be attributed back to whichever concrete type actually owns it.</summary>
        public static ContentTypeInfo FindNearest(Type assetType)
        {
            Ensure();
            for (var t = assetType; t != null; t = t.BaseType)
            {
                var match = _all.FirstOrDefault(i => i.ContentType == t);
                if (match != null) return match;
            }
            return null;
        }

        /// <summary>Creates the editor for <paramref name="info"/> — a custom
        /// <see cref="ContentEditor{T}"/> subclass if one was found, otherwise the default.</summary>
        public static ContentEditor CreateEditor(ContentTypeInfo info)
        {
            Ensure();
            var editorType = _editorTypeByContentType.TryGetValue(info.ContentType, out var custom)
                ? custom
                : typeof(ContentEditor<>).MakeGenericType(info.ContentType);

            var editor = (ContentEditor)Activator.CreateInstance(editorType);
            editor.TypeInfo = info;
            return editor;
        }

        private static void Ensure()
        {
            if (_all != null) return;

            _all = new List<ContentTypeInfo>();
            foreach (var type in TypeCache.GetTypesWithAttribute<ContentAttribute>())
            {
                if (type.IsAbstract || !typeof(DataObject).IsAssignableFrom(type))
                {
                    UnityEngine.Debug.LogWarning(
                        $"[Content] '{type.FullName}' has [Content] but is not a concrete " +
                        "DataObject and will be skipped.");
                    continue;
                }
                var attribute = type.GetCustomAttributes(typeof(ContentAttribute), false)
                    .Cast<ContentAttribute>().First();
                _all.Add(new ContentTypeInfo(type, attribute));
            }
            _all.Sort((a, b) => a.Order != b.Order
                ? a.Order.CompareTo(b.Order)
                : string.CompareOrdinal(a.DisplayName, b.DisplayName));

            _editorTypeByContentType = new Dictionary<Type, Type>();
            var editorsByContentType = new Dictionary<Type, List<Type>>();
            foreach (var editorType in TypeCache.GetTypesDerivedFrom<ContentEditor>())
            {
                if (editorType.IsAbstract) continue;
                var contentType = GetGenericContentType(editorType);
                if (contentType == null) continue;
                if (!editorsByContentType.TryGetValue(contentType, out var list))
                {
                    editorsByContentType[contentType] = list = new List<Type>();
                }

                list.Add(editorType);
            }
            foreach (var (contentType, candidates) in editorsByContentType)
            {
                candidates.Sort((a, b) => string.CompareOrdinal(a.FullName, b.FullName));
                if (candidates.Count > 1)
                {
                    UnityEngine.Debug.LogWarning(
                        $"[Content] {candidates.Count} editors target '{contentType.Name}' " +
                        $"({string.Join(", ", candidates.Select(c => c.Name))}) — using '{candidates[0].Name}'.");
                }

                _editorTypeByContentType[contentType] = candidates[0];
            }
        }

        private static Type GetGenericContentType(Type editorType)
        {
            for (var t = editorType; t != null && t != typeof(object); t = t.BaseType)
            {
                if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(ContentEditor<>))
                {
                    return t.GetGenericArguments()[0];
                }
            }

            return null;
        }
    }
}
