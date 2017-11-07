// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.VisualStudio.Text.Editor;

namespace Microsoft.VisualStudio.Editor.Razor
{
    internal abstract class InternalVisualStudioDocumentTracker : VisualStudioDocumentTracker
    {
        public abstract void AddTextView(ITextView textView);

        public abstract void RemoveTextView(ITextView textView);
    }
}
