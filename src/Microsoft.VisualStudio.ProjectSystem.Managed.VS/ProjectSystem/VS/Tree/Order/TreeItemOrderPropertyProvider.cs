﻿// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;

namespace Microsoft.VisualStudio.ProjectSystem.VS.Tree.Order
{
    /// <summary>
    /// Provider that computes display order of tree items based on input ordering of
    /// evaluated includes from the project file.
    /// </summary>
    internal class TreeItemOrderPropertyProvider : IProjectTreePropertiesProvider
    {
        private const string FullPathProperty = "FullPath";
        private readonly Dictionary<string, int> _displayOrderMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> _rootedOrderMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private readonly UnconfiguredProject _project;

        public TreeItemOrderPropertyProvider(IReadOnlyCollection<ProjectItemIdentity> orderedItems, UnconfiguredProject project)
        {
            _project = project;
            OrderedItems = orderedItems;

            ComputeIndices();
        }

        public IReadOnlyCollection<ProjectItemIdentity> OrderedItems { get; }

        /// <summary>
        /// Preorder folders and items that are provided as ordered evaluated includes
        /// </summary>
        private void ComputeIndices()
        {
            var duplicateFiles = OrderedItems
                .Select(p => Path.GetFileName(p.EvaluatedInclude))
                .GroupBy(file => file, StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key).ToImmutableHashSet();

            var index = 1;

            // This enumerates all the folder names and file names and maps them
            // against their assigned display index. Any file names that are duplicates
            // are mapped in in _rootedOrderMap only; everything else is mapped in
            // _displayOrderMap only.
            foreach (var item in OrderedItems)
            {
                var includeParts = item.EvaluatedInclude.Split(CommonConstants.PathSeparators, StringSplitOptions.RemoveEmptyEntries);
                foreach (var part in includeParts)
                {
                    var rootedPath = duplicateFiles.Contains(part) ? _project.MakeRooted(item.EvaluatedInclude) : null;
                    if (rootedPath != null && !_rootedOrderMap.ContainsKey(rootedPath))
                    {
                        _rootedOrderMap.Add(rootedPath, index++);
                    }
                    else if (!_displayOrderMap.ContainsKey(part))
                    {
                        _displayOrderMap.Add(part, index++);
                    }
                }
            }
        }

        /// <summary>
        /// Assign a display order property to items that have previously been preordered
        /// or other (hidden) items under the project root that are not folders
        /// </summary>
        /// <param name="propertyContext">context for the tree item being evaluated</param>
        /// <param name="propertyValues">mutable properties that can be changed to affect display order etc</param>
        public void CalculatePropertyValues(
            IProjectTreeCustomizablePropertyContext propertyContext, 
            IProjectTreeCustomizablePropertyValues propertyValues)
        {
            if (propertyValues is IProjectTreeCustomizablePropertyValues2 propertyValues2)
            {
                // assign display order to folders and items that appear in order map
                if (_displayOrderMap.TryGetValue(propertyContext.ItemName, out var index)
                    || (propertyContext.Metadata.TryGetValue(FullPathProperty, out var fullPath)
                        && _rootedOrderMap.TryGetValue(fullPath, out index)))
                {
                    // sometimes these items temporarily have null item type. Ignore these cases
                    if (propertyContext.ItemType != null)
                    {
                        propertyValues2.DisplayOrder = index;
                    }
                }
                else if (!propertyContext.IsFolder)
                {
                    // move unordered non-folder items to the end 
                    // (this will typically be hidden items visible on "Show All Files")
                    propertyValues2.DisplayOrder = int.MaxValue;
                }
            }
        }
    }
}
