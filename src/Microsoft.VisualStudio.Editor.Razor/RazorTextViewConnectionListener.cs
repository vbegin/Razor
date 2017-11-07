// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Razor;
using Microsoft.VisualStudio.LanguageServices;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Utilities;

namespace Microsoft.VisualStudio.Editor.Razor
{
    [ContentType(RazorLanguage.ContentType)]
    [TextViewRole(PredefinedTextViewRoles.Document)]
    [Export(typeof(ITextViewConnectionListener))]
    internal class RazorTextViewConnectionListener : ITextViewConnectionListener
    {
        private readonly ForegroundDispatcher _foregroundDispatcher;
        private readonly RazorEditorFactoryService _editorFactoryService;
        private readonly Workspace _workspace;

        [ImportingConstructor]
        public RazorTextViewConnectionListener(
            RazorEditorFactoryService editorFactoryService,
            [Import(typeof(VisualStudioWorkspace))] Workspace workspace)
        {
            if (editorFactoryService == null)
            {
                throw new ArgumentNullException(nameof(editorFactoryService));
            }

            if (workspace == null)
            {
                throw new ArgumentNullException(nameof(workspace));
            }

            _editorFactoryService = editorFactoryService;
            _workspace = workspace;

            _foregroundDispatcher = workspace.Services.GetRequiredService<ForegroundDispatcher>();
        }

        // This is only for testing. We want to avoid using the actual Roslyn GetService methods in unit tests.
        internal RazorTextViewConnectionListener(
            ForegroundDispatcher foregroundDispatcher,
            RazorEditorFactoryService editorFactoryService,
            [Import(typeof(VisualStudioWorkspace))] Workspace workspace)
        {
            if (foregroundDispatcher == null)
            {
                throw new ArgumentNullException(nameof(foregroundDispatcher));
            }

            if (editorFactoryService == null)
            {
                throw new ArgumentNullException(nameof(editorFactoryService));
            }

            if (workspace == null)
            {
                throw new ArgumentNullException(nameof(workspace));
            }

            _foregroundDispatcher = foregroundDispatcher;
            _editorFactoryService = editorFactoryService;
            _workspace = workspace;
        }

        public Workspace Workspace => _workspace;

        public void SubjectBuffersConnected(ITextView textView, ConnectionReason reason, IReadOnlyCollection<ITextBuffer> subjectBuffers)
        {
            if (textView == null)
            {
                throw new ArgumentException(nameof(textView));
            }

            if (subjectBuffers == null)
            {
                throw new ArgumentNullException(nameof(subjectBuffers));
            }

            _foregroundDispatcher.AssertForegroundThread();

            foreach (var textBuffer in subjectBuffers)
            {
                if (!textBuffer.IsRazorBuffer())
                {
                    continue;
                }

                if (!_editorFactoryService.TryGetDocumentTracker(textBuffer, out var documentTracker) ||
                    !(documentTracker is InternalVisualStudioDocumentTracker tracker))
                {
                    Debug.Fail("Tracker should always be available given our expectations of the VS workflow.");
                    return;
                }

                tracker.AddTextView(textView);
            }
        }

        public void SubjectBuffersDisconnected(ITextView textView, ConnectionReason reason, IReadOnlyCollection<ITextBuffer> subjectBuffers)
        {
            if (textView == null)
            {
                throw new ArgumentException(nameof(textView));
            }

            if (subjectBuffers == null)
            {
                throw new ArgumentNullException(nameof(subjectBuffers));
            }

            _foregroundDispatcher.AssertForegroundThread();

            // This means a Razor buffer has be detached from this ITextView or the ITextView is closing. Since we keep a 
            // list of all of the open text views for each text buffer, we need to update the tracker.
            //
            // Notice that this method is called *after* changes are applied to the text buffer(s). We need to check every
            // one of them for a tracker because the content type could have changed.
            foreach (var textBuffer in subjectBuffers)
            {
                InternalVisualStudioDocumentTracker documentTracker;
                if (textBuffer.Properties.TryGetProperty(typeof(VisualStudioDocumentTracker), out documentTracker))
                {
                    documentTracker.RemoveTextView(textView);
                }
            }
        }
    }
}
