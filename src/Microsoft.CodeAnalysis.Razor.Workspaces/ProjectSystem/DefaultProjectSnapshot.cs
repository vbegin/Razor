﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;

namespace Microsoft.CodeAnalysis.Razor.ProjectSystem
{
    // All of the public state of this is immutable - we create a new instance and notify subscribers
    // when it changes. 
    //
    // However we use the private state to track things like dirty/clean.
    //
    // See the private constructors... When we update the snapshot we either are processing a Workspace
    // change (Project) or updating the computed state (ProjectSnapshotUpdateContext). We don't do both
    // at once. 
    internal class DefaultProjectSnapshot : ProjectSnapshot
    {
        public DefaultProjectSnapshot(Project underlyingProject)
        {
            if (underlyingProject == null)
            {
                throw new ArgumentNullException(nameof(underlyingProject));
            }

            UnderlyingProject = underlyingProject;
        }

        private DefaultProjectSnapshot(Project underlyingProject, DefaultProjectSnapshot other)
        {
            if (underlyingProject == null)
            {
                throw new ArgumentNullException(nameof(underlyingProject));
            }

            if (other == null)
            {
                throw new ArgumentNullException(nameof(other));
            }
            
            UnderlyingProject = underlyingProject;

            ComputedVersion = other.ComputedVersion;
            Configuration = other.Configuration;
        }

        private DefaultProjectSnapshot(ProjectSnapshotUpdateContext update, DefaultProjectSnapshot other)
        {
            if (update == null)
            {
                throw new ArgumentNullException(nameof(update));
            }

            if (other == null)
            {
                throw new ArgumentNullException(nameof(other));
            }

            UnderlyingProject = other.UnderlyingProject;

            ComputedVersion = update.UnderlyingProject.Version;
            Configuration = update.Configuration;
        }

        public override ProjectExtensibilityConfiguration Configuration { get; }

        public override Project UnderlyingProject { get; }

        // This is the version that the computed state is based on.
        public VersionStamp? ComputedVersion { get; set; }

        // We know the project is dirty if we don't have a computed result, or it was computed for a different version.
        // Since the PSM updates the snapshots synchronously, the snapshot can never be older than the computed state.
        public bool IsDirty => ComputedVersion == null || ComputedVersion.Value != UnderlyingProject.Version;

        public DefaultProjectSnapshot WithProjectChange(Project project)
        {
            if (project == null)
            {
                throw new ArgumentNullException(nameof(project));
            }

            return new DefaultProjectSnapshot(project, this);
        }

        public DefaultProjectSnapshot WithProjectChange(ProjectSnapshotUpdateContext update)
        {
            if (update == null)
            {
                throw new ArgumentNullException(nameof(update));
            }

            return new DefaultProjectSnapshot(update, this);
        }

        public bool HasChangesComparedTo(ProjectSnapshot original)
        {
            if (original == null)
            {
                throw new ArgumentNullException(nameof(original));
            }

            return !object.Equals(Configuration, original.Configuration);
        }
    }
}
