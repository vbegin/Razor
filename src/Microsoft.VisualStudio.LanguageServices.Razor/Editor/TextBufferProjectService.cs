﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Text;

namespace Microsoft.VisualStudio.LanguageServices.Razor.Editor
{
    internal abstract class TextBufferProjectService
    {
        public abstract IVsHierarchy GetHierarchy(ITextBuffer textBuffer);

        public abstract bool IsSupportedProject(IVsHierarchy hierarchy);

        public abstract string GetProjectPath(IVsHierarchy hierarchy);

        public abstract string GetProjectName(IVsHierarchy hierarchy);
    }
}
