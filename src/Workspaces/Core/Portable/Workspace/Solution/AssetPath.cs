﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Runtime.Serialization;
using Roslyn.Utilities;

namespace Microsoft.CodeAnalysis.Serialization;

/// <summary>
/// Required information passed with an asset synchronization request to tell the host where to scope the request to. In
/// particular, this is often used to scope to a particular <see cref="Project"/> or <see cref="Document"/> to avoid
/// having to search the entire solution.
/// </summary>
[DataContract]
internal readonly struct AssetPath
{
    /// <summary>
    /// Special instance, allowed only in tests/debug-asserts, that can do a full lookup across the entire checksum
    /// tree.  Should not be used in normal release-mode product code.
    /// </summary>
    public static readonly AssetPath FullLookupForTesting = AssetPathKind.Solution | AssetPathKind.Projects | AssetPathKind.Documents;

    [DataMember(Order = 0)]
    private readonly AssetPathKind _kind;

    /// <summary>
    /// If not null, the search should only descend into the single project with this id.
    /// </summary>
    [DataMember(Order = 1)]
    public readonly ProjectId? ProjectId;

    /// <summary>
    /// If not null, the search should only descend into the single document with this id.
    /// </summary>
    [DataMember(Order = 2)]
    public readonly DocumentId? DocumentId;

    public AssetPath(AssetPathKind kind, ProjectId? projectId = null, DocumentId? documentId = null)
    {
        _kind = kind;
        ProjectId = projectId;
        DocumentId = documentId;
    }

    public AssetPath(AssetPathKind kind, ProjectId projectId)
        : this(kind, projectId, documentId: null)
    {
    }

    public AssetPath(AssetPathKind kind, DocumentId documentId)
        : this(kind, documentId.ProjectId, documentId)
    {
    }

    public bool IncludeSolution => (_kind & AssetPathKind.Solution) != 0;
    public bool IncludeProjects => (_kind & AssetPathKind.Projects) != 0;
    public bool IncludeDocuments => (_kind & AssetPathKind.Documents) != 0;

    public bool IncludeProjectStateChecksums => (_kind & AssetPathKind.ProjectStateChecksums) != 0;
    public bool IncludeProjectAttributes => (_kind & AssetPathKind.ProjectAttributes) != 0;
    public bool IncludeProjectCompilationOptions => (_kind & AssetPathKind.ProjectCompilationOptions) != 0;
    public bool IncludeProjectParseOptions => (_kind & AssetPathKind.ProjectParseOptions) != 0;
    public bool IncludeProjectProjectReferences => (_kind & AssetPathKind.ProjectProjectReferences) != 0;
    public bool IncludeProjectMetadataReferences => (_kind & AssetPathKind.ProjectMetadataReferences) != 0;
    public bool IncludeProjectAnalyzerReferences => (_kind & AssetPathKind.ProjectAnalyzerReferences) != 0;

    public bool IncludeDocumentStateChecksums => (_kind & AssetPathKind.DocumentStateChecksums) != 0;
    public bool IncludeDocumentAttributes => (_kind & AssetPathKind.DocumentAttributes) != 0;
    public bool IncludeDocumentText => (_kind & AssetPathKind.DocumentText) != 0;

    public static implicit operator AssetPath(AssetPathKind kind) => new(kind);

    /// <summary>
    /// Searches only for information about this project.
    /// </summary>
    public static implicit operator AssetPath(ProjectId projectId) => new(AssetPathKind.Projects, projectId);

    /// <summary>
    /// Searches only for information about this document.
    /// </summary>
    public static implicit operator AssetPath(DocumentId documentId) => new(AssetPathKind.Documents, documentId);

    /// <summary>
    /// Searches the requested project, and all documents underneath it.  Used only in tests.
    /// </summary>
    /// <param name="projectId"></param>
    /// <returns></returns>
    public static AssetPath SolutionAndProjectForTesting(ProjectId projectId)
        => new(AssetPathKind.Solution | AssetPathKind.Projects, projectId);

    /// <summary>
    /// Searches all documents within the specified project.
    /// </summary>
    /// <param name="projectId"></param>
    /// <returns></returns>
    public static AssetPath DocumentsInProject(ProjectId projectId)
        => new(AssetPathKind.Documents, projectId);
}

[Flags]
internal enum AssetPathKind
{
    /// <summary>
    /// Search solution-level information.
    /// </summary>
    Solution = 1 << 0,

    ProjectStateChecksums = 1 << 1,
    ProjectAttributes = 1 << 2,
    ProjectCompilationOptions = 1 << 3,
    ProjectParseOptions = 1 << 4,
    ProjectProjectReferences = 1 << 5,
    ProjectMetadataReferences = 1 << 6,
    ProjectAnalyzerReferences = 1 << 7,

    /// <summary>
    /// Search projects for results.  All project-level information will be searched.
    /// </summary>
    Projects = ProjectStateChecksums | ProjectAttributes | ProjectCompilationOptions | ProjectParseOptions | ProjectProjectReferences | ProjectMetadataReferences | ProjectAnalyzerReferences,

    DocumentStateChecksums = 1 << 8,
    DocumentAttributes = 1 << 9,
    DocumentText = 1 << 10,

    /// <summary>
    /// Search documents for results.
    /// </summary>
    Documents = DocumentStateChecksums | DocumentAttributes | DocumentText,
}
