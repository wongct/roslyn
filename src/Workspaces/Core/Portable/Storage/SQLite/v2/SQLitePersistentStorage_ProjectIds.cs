﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Concurrent;
using Microsoft.CodeAnalysis.SQLite.v2.Interop;
using Microsoft.CodeAnalysis.Storage;

namespace Microsoft.CodeAnalysis.SQLite.v2
{
    internal partial class SQLitePersistentStorage
    {
        /// <summary>
        /// Mapping from the workspace's ID for a project, to the ID we use in the DB for the project.
        /// Kept locally so we don't have to hit the DB for the common case of trying to determine the 
        /// DB id for a project.
        /// </summary>
        private readonly ConcurrentDictionary<ProjectId, ProjectPrimaryKey> _projectIdToIdMap = new();

        /// <summary>
        /// Given a project, and the name of a stream to read/write, gets the integral DB ID to 
        /// use to find the data inside the ProjectData table.
        /// </summary>
        private (ProjectPrimaryKey projectKeyId, int dataNameId)? TryGetProjectDataId(
            SqlConnection connection, ProjectKey project, string name, bool allowWrite)
        {
            var projectId = TryGetProjectId(connection, project, allowWrite);
            var nameId = TryGetStringId(connection, name, allowWrite);
            return projectId == null || nameId == null ? null : (projectId.Value, nameId.Value);
        }

        private ProjectPrimaryKey? TryGetProjectId(SqlConnection connection, ProjectKey project, bool allowWrite)
        {
            // First see if we've cached the ID for this value locally.  If so, just return
            // what we already have.
            if (_projectIdToIdMap.TryGetValue(project.Id, out var existingId))
                return existingId;

            var id = TryGetProjectIdFromDatabase(connection, project, allowWrite);
            if (id != null)
            {
                // Cache the value locally so we don't need to go back to the DB in the future.
                _projectIdToIdMap.TryAdd(project.Id, id.Value);
            }

            return id;
        }

        private ProjectPrimaryKey? TryGetProjectIdFromDatabase(SqlConnection connection, ProjectKey project, bool allowWrite)
        {
            // Key the project off both its path and name.  That way we work properly
            // in host and test scenarios.
            var projectPathId = TryGetStringId(connection, project.FilePath, allowWrite);
            var projectNameId = TryGetStringId(connection, project.Name, allowWrite);

            return projectPathId == null || projectNameId == null ? null : new ProjectPrimaryKey(projectPathId.Value, projectNameId.Value);
        }
    }
}
