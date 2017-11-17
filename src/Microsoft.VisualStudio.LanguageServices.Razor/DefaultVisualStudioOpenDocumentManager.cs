// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Runtime.InteropServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Razor;
using Microsoft.VisualStudio.Editor;
using Microsoft.VisualStudio.Editor.Razor;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.TextManager.Interop;

namespace Microsoft.VisualStudio.LanguageServices.Razor
{
    [Export(typeof(VisualStudioOpenDocumentManager))]
    internal class DefaultVisualStudioOpenDocumentManager : VisualStudioOpenDocumentManager
    {
        private readonly IVsFileChangeEx _fileChangeService;
        private readonly IVsRunningDocumentTable _runningDocumentTable;
        private readonly IVsEditorAdaptersFactoryService _editorAdaptersFactoryService;
        private readonly ForegroundDispatcher _foregroundDispatcher;

        private List<VisualStudioDocumentTracker> _documents;
        private Dictionary<string, List<string>> _viewImportsCache;
        private Dictionary<string, FileChangeTracker> _fileChangeTrackerCache;

        [ImportingConstructor]
        public DefaultVisualStudioOpenDocumentManager(
            [Import(typeof(SVsServiceProvider))] IServiceProvider serviceProvider,
            [Import(typeof(VisualStudioWorkspace))] Workspace workspace,
            IVsEditorAdaptersFactoryService editorAdaptersFactoryService)
        {
            if (serviceProvider == null)
            {
                throw new ArgumentNullException(nameof(serviceProvider));
            }

            if (workspace == null)
            {
                throw new ArgumentNullException(nameof(workspace));
            }

            if (editorAdaptersFactoryService == null)
            {
                throw new ArgumentNullException(nameof(editorAdaptersFactoryService));
            }

            _fileChangeService = serviceProvider.GetService(typeof(SVsFileChangeEx)) as IVsFileChangeEx;
            _runningDocumentTable = serviceProvider.GetService(typeof(SVsRunningDocumentTable)) as IVsRunningDocumentTable;
            _editorAdaptersFactoryService = editorAdaptersFactoryService;

            _documents = new List<VisualStudioDocumentTracker>();
            _viewImportsCache = new Dictionary<string, List<string>>();
            _fileChangeTrackerCache = new Dictionary<string, FileChangeTracker>();

            _foregroundDispatcher = workspace.Services.GetRequiredService<ForegroundDispatcher>();
        }

        public override IReadOnlyList<VisualStudioDocumentTracker> OpenDocuments => _documents;

        public override void AddDocument(VisualStudioDocumentTracker tracker)
        {
            _foregroundDispatcher.AssertForegroundThread();

            if (!_documents.Contains(tracker))
            {
                _documents.Add(tracker);

                if (!tracker.TextBuffer.Properties.TryGetProperty<VisualStudioRazorParser>(typeof(VisualStudioRazorParser), out var parser))
                {
                    // The document tracker doesn't have a corresponding Razor parser. This should never be the case.
                    return;
                }

                var currentDocumentFilePath = tracker.FilePath;
                var imports = parser.TemplateEngine.GetImportItems(currentDocumentFilePath);
                foreach (var import in imports)
                {
                    var importFilePath = import.PhysicalPath;
                    if (importFilePath == null)
                    {
                        // This is probably an in-memory view import. We can't track it.
                        continue;
                    }

                    if (!_viewImportsCache.ContainsKey(importFilePath))
                    {
                        // First time seeing this view import. Create a list for all open documents associated with this view import.
                        _viewImportsCache[importFilePath] = new List<string>()
                        {
                            currentDocumentFilePath,
                        };
                    }
                    else
                    {
                        // We already have other open documents associated with this view import.
                        // Add the current open document to that list.
                        var openDocumentsForImport = _viewImportsCache[importFilePath];
                        openDocumentsForImport.Add(currentDocumentFilePath);
                    }

                    FileChangeTracker fileChangeTracker;
                    if (!_fileChangeTrackerCache.ContainsKey(importFilePath))
                    {
                        // First time seeing this view import. Create a change tracker for it.
                        fileChangeTracker = new FileChangeTracker(
                            _fileChangeService,
                            _runningDocumentTable,
                            _editorAdaptersFactoryService,
                            _foregroundDispatcher,
                            importFilePath);

                        _fileChangeTrackerCache[importFilePath] = fileChangeTracker;
                    }
                    else
                    {
                        fileChangeTracker = _fileChangeTrackerCache[importFilePath];
                    }

                    // We want to queue a parse every time this view import changes.
                    fileChangeTracker.UpdatedOnDisk += (sender, args) =>
                    {
                        parser.QueueReparse();
                    };

                    // This should no-op if we are already listening for changes to this view import.
                    fileChangeTracker.StartListeningForChanges();
                }
            }
        }

        public override void RemoveDocument(VisualStudioDocumentTracker tracker)
        {
            _foregroundDispatcher.AssertForegroundThread();

            if (_documents.Contains(tracker))
            {
                _documents.Remove(tracker);
            }

            if (!tracker.TextBuffer.Properties.TryGetProperty<VisualStudioRazorParser>(typeof(VisualStudioRazorParser), out var parser))
            {
                // The document tracker doesn't have a corresponding Razor parser. This should never be the case.
                return;
            }

            var currentDocumentFilePath = tracker.FilePath;
            var imports = parser.TemplateEngine.GetImportItems(currentDocumentFilePath);
            foreach (var import in imports)
            {
                var importFilePath = import.PhysicalPath;
                if (importFilePath == null)
                {
                    // This is probably an in-memory view import. We can't track it.
                    continue;
                }

                if (_viewImportsCache.ContainsKey(importFilePath))
                {
                    // Remove the current document from the list of open documents associated with this import.
                    var openDocumentsForImport = _viewImportsCache[importFilePath];
                    openDocumentsForImport.Remove(currentDocumentFilePath);

                    if (openDocumentsForImport.Count == 0 && _fileChangeTrackerCache.ContainsKey(importFilePath))
                    {
                        // There are no open documents that are associated with this view import.
                        // We don't have to watch it for now.
                        var fileChangeTracker = _fileChangeTrackerCache[importFilePath];
                        fileChangeTracker.StopListeningForChanges();
                    }
                }
            }
        }

        private class FileChangeTracker : IVsFileChangeEvents, IVsRunningDocTableEvents
        {
            private const uint FileChangeFlags = (uint)(_VSFILECHANGEFLAGS.VSFILECHG_Time | _VSFILECHANGEFLAGS.VSFILECHG_Size);

            private readonly IVsFileChangeEx _fileChangeService;
            private readonly IVsRunningDocumentTable _runningDocumentTable;
            private readonly IVsEditorAdaptersFactoryService _editorAdaptersFactoryService;
            private readonly ForegroundDispatcher _foregroundDispatcher;
            private readonly string _filePath;

            private uint _fileChangeCookie;
            private uint _runningDocumentTableCookie;

            public event EventHandler UpdatedOnDisk;

            public FileChangeTracker(
                IVsFileChangeEx fileChangeService,
                IVsRunningDocumentTable runningDocumentTable,
                IVsEditorAdaptersFactoryService editorAdaptersFactoryService,
                ForegroundDispatcher foregroundDispatcher,
                string filePath)
            {
                _fileChangeService = fileChangeService;
                _runningDocumentTable = runningDocumentTable;
                _editorAdaptersFactoryService = editorAdaptersFactoryService;
                _foregroundDispatcher = foregroundDispatcher;
                _filePath = filePath;
                _fileChangeCookie = VSConstants.VSCOOKIE_NIL;
                _runningDocumentTableCookie = VSConstants.VSCOOKIE_NIL;
            }

            public string FilePath => _filePath;

            public int FilesChanged(uint cChanges, string[] rgpszFile, uint[] rggrfChange)
            {
                _foregroundDispatcher.AssertForegroundThread();

                UpdatedOnDisk?.Invoke(this, EventArgs.Empty);

                return VSConstants.S_OK;
            }

            public int DirectoryChanged(string pszDirectory)
            {
                return VSConstants.S_OK;
            }

            public void StartListeningForChanges()
            {
                try
                {
                    if (_fileChangeCookie == VSConstants.VSCOOKIE_NIL)
                    {
                        _fileChangeService.AdviseFileChange(_filePath, FileChangeFlags, this, out _fileChangeCookie);
                    }

                    if (_runningDocumentTableCookie == VSConstants.VSCOOKIE_NIL)
                    {
                        _runningDocumentTable.AdviseRunningDocTableEvents(this, out _runningDocumentTableCookie);
                    }
                }
                catch (Exception exception)
                {
                    throw new InvalidOperationException(
                        Resources.FormatUnexpectedException(
                            typeof(FileChangeTracker).FullName,
                            nameof(StartListeningForChanges)),
                        exception);
                }
            }

            public void StopListeningForChanges()
            {
                try
                {
                    if (_fileChangeCookie != VSConstants.VSCOOKIE_NIL)
                    {
                        _fileChangeService.UnadviseFileChange(_fileChangeCookie);
                    }

                    if (_runningDocumentTableCookie != VSConstants.VSCOOKIE_NIL)
                    {
                        _runningDocumentTable.UnadviseRunningDocTableEvents(_runningDocumentTableCookie);
                    }
                }
                catch (Exception exception)
                {
                    throw new InvalidOperationException(
                        Resources.FormatUnexpectedException(
                            typeof(FileChangeTracker).FullName,
                            nameof(StopListeningForChanges)),
                        exception);
                }
            }

            public int OnAfterFirstDocumentLock(uint docCookie, uint dwRDTLockType, uint dwReadLocksRemaining, uint dwEditLocksRemaining)
            {
                return VSConstants.S_OK;
            }

            public int OnBeforeLastDocumentUnlock(uint docCookie, uint dwRDTLockType, uint dwReadLocksRemaining, uint dwEditLocksRemaining)
            {
                return VSConstants.S_OK;
            }

            public int OnAfterSave(uint docCookie)
            {
                _foregroundDispatcher.AssertForegroundThread();

                OnAfterSaveWorker(docCookie);

                return VSConstants.S_OK;
            }

            public int OnAfterAttributeChange(uint docCookie, uint grfAttribs)
            {
                return VSConstants.S_OK;
            }

            public int OnBeforeDocumentWindowShow(uint docCookie, int fFirstShow, IVsWindowFrame pFrame)
            {
                return VSConstants.S_OK;
            }

            public int OnAfterDocumentWindowHide(uint docCookie, IVsWindowFrame pFrame)
            {
                return VSConstants.S_OK;
            }

            private void OnAfterSaveWorker(uint docCookie)
            {
                if (!TryGetTextBufferFromDocCookie(docCookie, out var textBuffer))
                {
                    return;
                }

                if (!textBuffer.IsRazorBuffer() ||
                    !textBuffer.Properties.TryGetProperty<VisualStudioDocumentTracker>(typeof(VisualStudioDocumentTracker), out var tracker))
                {
                    // This is not the document we care about. Bail.
                    return;
                }

                if (string.Equals(_filePath, tracker.FilePath, StringComparison.OrdinalIgnoreCase))
                {
                    UpdatedOnDisk?.Invoke(this, EventArgs.Empty);
                }
            }

            private bool TryGetTextBufferFromDocCookie(uint docCookie, out ITextBuffer textBuffer)
            {
                // Refer https://github.com/dotnet/roslyn/blob/aeeec402a2f223580f36c298bbd9d92ffc94b330/src/VisualStudio/Core/Def/Implementation/SaveEventsService.cs#L97

                textBuffer = null;
                var docData = IntPtr.Zero;

                try
                {
                    Marshal.ThrowExceptionForHR(
                        _runningDocumentTable.GetDocumentInfo(
                            docCookie,
                            out var flags,
                            out var readLocks,
                            out var writeLocks,
                            out var moniker,
                            out var hierarchy,
                            out var itemid,
                            out docData));

                    if (Marshal.GetObjectForIUnknown(docData) is IVsTextBuffer shimTextBuffer)
                    {
                        textBuffer = _editorAdaptersFactoryService.GetDocumentBuffer(shimTextBuffer);
                        if (textBuffer != null)
                        {
                            return true;
                        }
                    }
                }
                finally
                {
                    if (docData != IntPtr.Zero)
                    {
                        Marshal.Release(docData);
                    }
                }

                return false;
            }
        }
    }
}
