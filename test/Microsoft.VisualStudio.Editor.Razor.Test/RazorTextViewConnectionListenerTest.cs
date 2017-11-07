// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Collections.ObjectModel;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Razor;
using Microsoft.CodeAnalysis.Razor.Editor;
using Microsoft.CodeAnalysis.Razor.ProjectSystem;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Utilities;
using Moq;
using Xunit;

namespace Microsoft.VisualStudio.Editor.Razor
{
    public class RazorTextViewConnectionListenerTest : ForegroundDispatcherTestBase
    {
        private Workspace Workspace { get; } = new AdhocWorkspace();

        private IContentType RazorContentType { get; } = Mock.Of<IContentType>(c => c.IsOfType(RazorLanguage.ContentType) == true);

        private IContentType NonRazorContentType { get; } = Mock.Of<IContentType>(c => c.IsOfType(It.IsAny<string>()) == false);

        [ForegroundFact]
        public void SubjectBuffersConnected_ForNonRazorTextBuffer_DoesNothing()
        {
            // Arrange
            var editorFactoryService = new Mock<RazorEditorFactoryService>(MockBehavior.Strict);
            var factory = new RazorTextViewConnectionListener(Dispatcher, editorFactoryService.Object, Workspace);
            var textView = Mock.Of<ITextView>();
            var buffers = new Collection<ITextBuffer>()
            {
                Mock.Of<ITextBuffer>(b => b.ContentType == NonRazorContentType && b.Properties == new PropertyCollection()),
            };

            // Act & Assert
            factory.SubjectBuffersConnected(textView, ConnectionReason.BufferGraphChange, buffers);
        }

        [ForegroundFact]
        public void SubjectBuffersConnected_ForRazorTextBuffer_AddsTextViewToTracker()
        {
            // Arrange
            var textView = Mock.Of<ITextView>();
            var buffers = new Collection<ITextBuffer>()
            {
                Mock.Of<ITextBuffer>(b => b.ContentType == RazorContentType && b.Properties == new PropertyCollection()),
            };
            var documentTrackerMock = new Mock<InternalVisualStudioDocumentTracker>(MockBehavior.Strict);
            documentTrackerMock.Setup(tracker => tracker.AddTextView(textView))
                .Verifiable();
            VisualStudioDocumentTracker documentTracker = documentTrackerMock.Object;
            var editorFactoryService = Mock.Of<RazorEditorFactoryService>(factoryService => factoryService.TryGetDocumentTracker(It.IsAny<ITextBuffer>(), out documentTracker) == true);
            var textViewListener = new RazorTextViewConnectionListener(Dispatcher, editorFactoryService, Workspace);

            // Act
            textViewListener.SubjectBuffersConnected(textView, ConnectionReason.BufferGraphChange, buffers);

            // Assert
            documentTrackerMock.Verify();
        }

        [ForegroundFact]
        public void SubjectBuffersDisconnected_ForAnyTextBufferWithTracker_RemovesTextView()
        {
            // Arrange
            var textView1 = Mock.Of<ITextView>();
            var textView2 = Mock.Of<ITextView>();

            var buffers = new Collection<ITextBuffer>()
            {
                Mock.Of<ITextBuffer>(b => b.ContentType == RazorContentType && b.Properties == new PropertyCollection()),
                Mock.Of<ITextBuffer>(b => b.ContentType == NonRazorContentType && b.Properties == new PropertyCollection()),
            };

            // Preload the buffer's properties with a tracker, so it's like we've already tracked this one.
            var tracker1 = new Mock<InternalVisualStudioDocumentTracker>(MockBehavior.Strict);
            tracker1.Setup(tracker => tracker.RemoveTextView(textView2))
                .Verifiable();
            buffers[0].Properties.AddProperty(typeof(VisualStudioDocumentTracker), tracker1.Object);

            var tracker2 = new Mock<InternalVisualStudioDocumentTracker>(MockBehavior.Strict);
            tracker2.Setup(tracker => tracker.RemoveTextView(textView2))
                .Verifiable();
            buffers[1].Properties.AddProperty(typeof(VisualStudioDocumentTracker), tracker2.Object);
            var textViewListener = new RazorTextViewConnectionListener(Dispatcher, Mock.Of<RazorEditorFactoryService>(), Workspace);

            // Act
            textViewListener.SubjectBuffersDisconnected(textView2, ConnectionReason.BufferGraphChange, buffers);

            // Assert
            tracker1.Verify();
            tracker2.Verify();
        }

        [ForegroundFact]
        public void SubjectBuffersDisconnected_ForAnyTextBufferWithoutTracker_DoesNothing()
        {
            // Arrange
            var textViewListener = new RazorTextViewConnectionListener(Dispatcher, Mock.Of<RazorEditorFactoryService>(), Workspace);

            var textView = Mock.Of<ITextView>();

            var buffers = new Collection<ITextBuffer>()
            {
                Mock.Of<ITextBuffer>(b => b.ContentType == RazorContentType && b.Properties == new PropertyCollection()),
            };

            // Act
            textViewListener.SubjectBuffersDisconnected(textView, ConnectionReason.BufferGraphChange, buffers);

            // Assert
            Assert.False(buffers[0].Properties.ContainsProperty(typeof(VisualStudioDocumentTracker)));
        }
    }
}
