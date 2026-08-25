using System;
using GI.UnityToolkit.Variables;

// ReSharper disable UnusedAutoPropertyAccessor.Global

namespace GI.UnityToolkit.Content
{
    /// <summary>
    /// Opts a <see cref="DataObject"/> type into the Content window
    /// (<c>Grimbar Interactive ▸ Content</c>). Place it on the concrete asset type; placing it on an
    /// abstract base opts in every concrete descendant instead.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class ContentAttribute : Attribute
    {
        /// <summary>Label shown in the type dropdown and the toolbar. Defaults to a nicified,
        /// pluralized type name ("ActionCardData" becomes "Action Cards").</summary>
        public string DisplayName { get; set; }

        /// <summary>Subfolder under the configured content root. Defaults to <see cref="DisplayName"/>.</summary>
        public string FolderName { get; set; }

        /// <summary>Serialized string property that mirrors the asset file name. Written on
        /// create/duplicate/rename. Set to null to opt out.</summary>
        public string NameProperty { get; set; } = "DisplayName";

        /// <summary>Serialized string property holding a slug id. Written on create/duplicate
        /// only, never on rename. Set to null to opt out.</summary>
        public string IdProperty { get; set; } = "Id";

        /// <summary>File name for newly created assets. Defaults to "New " + the nicified type name.</summary>
        public string DefaultAssetName { get; set; }

        /// <summary>Optional submenu grouping in the type dropdown.</summary>
        public string Category { get; set; }

        /// <summary>Sort order in the type dropdown; ties are broken alphabetically.</summary>
        public int Order { get; set; }
    }
}
