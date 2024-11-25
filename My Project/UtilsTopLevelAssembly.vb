﻿Option Strict On


Public Class UtilsTopLevelAssembly
    Public Property FMain As Form_Main
    ' Private IndexedDrives As New List(Of String)

    Public Sub New(_Form_Main As Form_Main)
        Me.FMain = _Form_Main
    End Sub

    Public Function GetLinksTopDown(TopLevelFolders As List(Of String),
                                    TopLevelAssembly As String,
                                    ActiveFileExtensionsList As List(Of String),
                                    Report As Boolean) As List(Of String)

        Dim DMApp As New DesignManager.Application
        Dim AllLinkedFilenames As New List(Of String)
        Dim FoundFiles As New List(Of String)
        Dim FileExtension As String
        Dim AllFilenames As New Dictionary(Of String, String)
        Dim tmpAllFilenames As New Dictionary(Of String, String)
        Dim LinkDict As New Dictionary(Of String, Dictionary(Of String, List(Of String)))
        Dim UnrelatedFiles As New List(Of String)
        Dim K As String
        Dim V As String

        DMApp.Visible = 1  ' So it can be seen and closed in case of program malfunction.
        'DMApp.Visible = 0

        FMain.Activate()

        For Each TopLevelFolder In TopLevelFolders
            tmpAllFilenames = GetAllFilenamesTopDown(TopLevelFolder)
            For Each K In tmpAllFilenames.Keys
                V = tmpAllFilenames(K)
                If Not AllFilenames.Keys.Contains(K) Then
                    AllFilenames.Add(K, V)
                End If
            Next
        Next

        If Not AllFilenames.Keys.Contains(TopLevelAssembly.ToLower) Then
            AllFilenames.Add(TopLevelAssembly.ToLower, TopLevelAssembly)
        End If

        LinkDict = CreateLinkDict(AllFilenames, LinkDict)

        LinkDict = PopulateLinkDict(DMApp, LinkDict, TopLevelAssembly)

        If CheckInterruptRequest() Then
            DMApp.Quit()
            Return FoundFiles
        End If

        LinkDict = VisitLinks(TopLevelAssembly, LinkDict)

        For Each Filename In LinkDict.Keys
            FileExtension = System.IO.Path.GetExtension(Filename).Replace(".", "*.")
            If ActiveFileExtensionsList.Contains(FileExtension) Then
                If LinkDict(Filename)("Visited")(0) = "True" Then
                    FoundFiles.Add(Filename)
                Else
                    UnrelatedFiles.Add(Filename)
                End If
            End If
        Next

        If Report Then
            ReportUnrelatedFiles(TopLevelFolders, FoundFiles)

            'If UnrelatedFiles.Count > 0 Then
            '    Dim Timestamp As String = System.DateTime.Now.ToString("yyyyMMdd_HHmmss")
            '    Dim LogfileName As String
            '    'Dim msg As String
            '    LogfileName = IO.Path.GetTempPath + "\Housekeeper_" + Timestamp + "_Unrelated_Files.log"

            '    Try
            '        Using writer As New IO.StreamWriter(LogfileName, True)
            '            For Each Filename In UnrelatedFiles
            '                ' Filename = Filename.Replace(TopLevelFolder, "")
            '                writer.WriteLine(String.Format(Filename))
            '            Next
            '        End Using

            '        Process.Start("Notepad.exe", LogfileName)

            '    Catch ex As Exception
            '    End Try

            'End If

        End If

        DMApp.Quit()

        Return FoundFiles

    End Function

    Private Function CreateLinkDict(
         AllFilenames As Dictionary(Of String, String),
         LinkDict As Dictionary(Of String, Dictionary(Of String, List(Of String)))
         ) As Dictionary(Of String, Dictionary(Of String, List(Of String)))

        Dim FilenameLowerCase As String
        Dim Filename As String

        For Each FilenameLowerCase In AllFilenames.Keys
            Filename = AllFilenames(FilenameLowerCase)

            LinkDict.Add(Filename, New Dictionary(Of String, List(Of String)))

            LinkDict(Filename).Add("Visited", New List(Of String))
            LinkDict(Filename)("Visited").Add("False")

            LinkDict(Filename).Add("Contains", New List(Of String))

            LinkDict(Filename).Add("ContainedBy", New List(Of String))
        Next

        Return LinkDict
    End Function

    Private Function PopulateLinkDict(
         DMApp As DesignManager.Application,
         LinkDict As Dictionary(Of String, Dictionary(Of String, List(Of String))),
         TopLevelAssembly As String
         ) As Dictionary(Of String, Dictionary(Of String, List(Of String)))

        Dim Filename As String

        Dim LinkedDocsName As String
        Dim ErrorFlag As String = "HousekeeperErrorFile"
        Dim msg As String = ""
        Dim tf As Boolean
        Dim Extension As String

        Dim ActiveFileExtensionsList As New List(Of String)


        For Each Filename In LinkDict.Keys

            If CheckInterruptRequest() Then
                Return LinkDict
            End If

            Dim LinkedDocsNames As New List(Of String)

            LinkedDocsNames = GetContains(DMApp, LinkDict, Filename, ErrorFlag)

            If LinkedDocsNames.Contains(ErrorFlag) Then
                If msg = "" Then
                    msg = String.Format("{0}{1}", "Design Manager could not process the following file(s)", vbCrLf)
                    msg = String.Format("{0}{1}{2}{2}", msg, "Will continue processing, but please verify results", vbCrLf)
                End If
                msg = String.Format("{0}{1}{2}", msg, Filename, vbCrLf)
            Else
                tf = FMain.CheckBoxTLAIncludePartCopies.Checked
                If tf Then
                    For Each LinkedDocsName In LinkedDocsNames
                        LinkDict(Filename)("Contains").Add(LinkedDocsName)
                    Next
                Else  ' Don't include part copies
                    Extension = IO.Path.GetExtension(Filename)
                    tf = Extension = ".par"
                    tf = (tf) Or (Extension = ".psm")
                    If Not tf Then
                        For Each LinkedDocsName In LinkedDocsNames
                            LinkDict(Filename)("Contains").Add(LinkedDocsName)
                        Next
                    End If
                End If
            End If
        Next

        If msg <> "" Then
            MsgBox(msg, vbOKOnly)
        End If

        LinkDict = GetContainedBy(LinkDict)

        Return LinkDict
    End Function

    Private Function GetContains(
        DMApp As DesignManager.Application,
        LinkDict As Dictionary(Of String, Dictionary(Of String, List(Of String))),
        Filename As String,
        ErrorFlag As String
         ) As List(Of String)

        Dim DMDoc As DesignManager.Document
        Dim LinkedDocs As DesignManager.LinkedDocuments
        Dim LinkedDoc As DesignManager.Document
        Dim CorrectedFilename As String

        Dim LinkedDocsNames As New List(Of String)
        Dim msg As String = ""

        If CheckInterruptRequest() Then
            Return LinkedDocsNames
        End If

        UpdateStatus("Follow Links", Filename)

        'DMDoc = CType(DMApp.Open(Filename), DesignManager.Document)

        ' Some files are corrrupt or have other problems
        Try
            DMDoc = CType(DMApp.Open(Filename), DesignManager.Document)

            LinkedDocs = CType(DMDoc.LinkedDocuments, DesignManager.LinkedDocuments)

            If Not IsNothing(LinkedDocs) Then
                For Each LinkedDoc In LinkedDocs
                    CorrectedFilename = GetCorrectedFilename(LinkDict, LinkedDoc.FullName)
                    If Not LinkDict(Filename)("Contains").Contains(CorrectedFilename) Then
                        If CorrectedFilename <> "" Then
                            LinkedDocsNames.Add(CorrectedFilename)
                        End If
                    End If
                Next
            Else
                'LinkedDocsNames.Add(ErrorFlag)
            End If

        Catch ex As Exception
            LinkedDocsNames.Add(ErrorFlag)
        End Try


        Return LinkedDocsNames
    End Function

    Private Function GetContainedBy(
        LinkDict As Dictionary(Of String, Dictionary(Of String, List(Of String)))
         ) As Dictionary(Of String, Dictionary(Of String, List(Of String)))

        Dim Filename As String
        Dim ContainsFilename As String

        For Each Filename In LinkDict.Keys
            UpdateStatus("Contained By", Filename)
            For Each ContainsFilename In LinkDict(Filename)("Contains")
                LinkDict(ContainsFilename)("ContainedBy").Add(Filename)
            Next
        Next

        Return LinkDict
    End Function

    Private Function VisitLinks(
        Filename As String,
        LinkDict As Dictionary(Of String, Dictionary(Of String, List(Of String)))
        ) As Dictionary(Of String, Dictionary(Of String, List(Of String)))

        Dim LinkedFilename As String
        Dim ContainedByFilename As String
        Dim Extension As String

        UpdateStatus("Visit Links", Filename)

        LinkDict(Filename)("Visited")(0) = "True"
        For Each ContainedByFilename In LinkDict(Filename)("ContainedBy")
            Extension = IO.Path.GetExtension(ContainedByFilename)
            If Extension = ".dft" Then
                LinkDict(ContainedByFilename)("Visited")(0) = "True"
            End If
        Next

        For Each LinkedFilename In LinkDict(Filename)("Contains")
            If Not LinkDict(LinkedFilename)("Visited")(0) = "True" Then
                LinkDict(LinkedFilename)("Visited")(0) = "True"
                For Each ContainedByFilename In LinkDict(LinkedFilename)("ContainedBy")
                    Extension = IO.Path.GetExtension(ContainedByFilename)
                    If Extension = ".dft" Then
                        LinkDict(ContainedByFilename)("Visited")(0) = "True"
                    End If
                Next
                LinkDict = VisitLinks(LinkedFilename, LinkDict)
            End If
        Next

        Return LinkDict
    End Function

    Private Function GetCorrectedFilename(
        LinkDict As Dictionary(Of String, Dictionary(Of String, List(Of String))),
        Filename As String
        ) As String

        ' The CorrectFilename respects the capitalization as it is on disk
        ' which may not match the capitilization of the link

        Dim CorrectFilename As String = ""

        For Each CorrectFilename In LinkDict.Keys
            If Filename.Contains("!") Then
                If CorrectFilename.ToLower = Filename.ToLower.Split("!"c)(0) Then
                    Return CorrectFilename
                End If
            End If
            If CorrectFilename.ToLower = Filename.ToLower Then
                Return CorrectFilename
            End If
        Next

        Return ""
    End Function

    Private Function FollowLinksTopDown(LinkedDocument As DesignManager.Document,
                                 AllLinkedFilenames As List(Of String)) As List(Of String)
        Dim LinkedDocs As DesignManager.LinkedDocuments
        Dim LinkedDoc As DesignManager.Document
        Dim msg As String
        Dim ValidExtensions As New List(Of String)
        Dim Extension As String

        ValidExtensions.Add(".asm")
        ValidExtensions.Add(".par")
        ValidExtensions.Add(".psm")
        ValidExtensions.Add(".dft")

        If FileIO.FileSystem.FileExists(LinkedDocument.FullName) Then
            If Not AllLinkedFilenames.Contains(LinkedDocument.FullName) Then
                AllLinkedFilenames.Add(LinkedDocument.FullName)

                'msg = LinkedDocument.FullName '.Replace(Form1.TextBoxInputDirectory.Text, "")
                msg = System.IO.Path.GetFileName(LinkedDocument.FullName)
                msg = "Follow Links " + msg
                FMain.TextBoxStatus.Text = msg

                ' In case of corrupted file or other problem
                Try
                    LinkedDocs = CType(LinkedDocument.LinkedDocuments, DesignManager.LinkedDocuments)
                    If LinkedDocs.Count > 0 Then
                        For Each LinkedDoc In LinkedDocs
                            Extension = IO.Path.GetExtension(LinkedDoc.FullName)
                            If ValidExtensions.Contains(Extension) Then
                                AllLinkedFilenames = FollowLinksTopDown(LinkedDoc, AllLinkedFilenames)
                            End If
                        Next
                    End If
                Catch ex As Exception
                End Try
            End If
        End If

        Return AllLinkedFilenames

    End Function

    Private Function GetAllFilenamesTopDown(TopLevelFolder As String) As Dictionary(Of String, String)
        Dim AllFilenames As IReadOnlyCollection(Of String)
        Dim Filename As String
        Dim ActiveFileExtensionsList As New List(Of String)
        Dim AllFilenamesDict As New Dictionary(Of String, String)

        FMain.TextBoxStatus.Text = "Getting all filenames."

        ActiveFileExtensionsList.Add("*.asm")
        ActiveFileExtensionsList.Add("*.par")
        ActiveFileExtensionsList.Add("*.psm")
        ActiveFileExtensionsList.Add("*.dft")

        AllFilenames = FileIO.FileSystem.GetFiles(TopLevelFolder,
                        FileIO.SearchOption.SearchAllSubDirectories,
                        ActiveFileExtensionsList.ToArray)

        For Each Filename In AllFilenames
            AllFilenamesDict.Add(Filename.ToLower, Filename)
        Next

        FMain.TextBoxStatus.Text = "Done getting all filenames."

        Return AllFilenamesDict

    End Function


    Private Sub ReportUnrelatedFiles(
               TopLevelFolders As List(Of String),
               Foundfiles As List(Of String))

        Dim AllFiles As New List(Of String)
        Dim SomeFiles As IReadOnlyCollection(Of String)
        Dim ActiveFileExtensionsList As New List(Of String)
        Dim UnrelatedFiles As New List(Of String)

        If TopLevelFolders.Count = 0 Then
            Exit Sub
        End If

        ActiveFileExtensionsList.Add("*.asm")
        ActiveFileExtensionsList.Add("*.par")
        ActiveFileExtensionsList.Add("*.psm")
        ActiveFileExtensionsList.Add("*.dft")

        For Each TopLevelFolder In TopLevelFolders
            SomeFiles = FileIO.FileSystem.GetFiles(TopLevelFolder,
                        FileIO.SearchOption.SearchAllSubDirectories,
                        ActiveFileExtensionsList.ToArray)

            For Each Filename In SomeFiles
                If Not Foundfiles.Contains(Filename, StringComparer.OrdinalIgnoreCase) Then
                    If Not UnrelatedFiles.Contains(Filename, StringComparer.OrdinalIgnoreCase) Then
                        UnrelatedFiles.Add(Filename)
                    End If
                End If
            Next

        Next

        If UnrelatedFiles.Count > 0 Then
            Dim Timestamp As String = System.DateTime.Now.ToString("yyyyMMdd_HHmmss")
            Dim LogfileName As String
            LogfileName = IO.Path.GetTempPath + "\Housekeeper_" + Timestamp + "_Unrelated_Files.log"

            Try
                Using writer As New IO.StreamWriter(LogfileName, True)
                    writer.WriteLine("UNRELATED FILES")
                    writer.WriteLine("")
                    For Each Filename In UnrelatedFiles
                        ' Filename = Filename.Replace(TopLevelFolder, "")
                        writer.WriteLine(String.Format(Filename))
                    Next
                End Using

                Process.Start("Notepad.exe", LogfileName)

            Catch ex As Exception
            End Try

        End If

    End Sub


    Public Function GetLinksBottomUp(TopLevelFolders As List(Of String),
                             TopLevelAssembly As String,
                             ActiveFileExtensionsList As List(Of String),
                             DraftAndModelSameName As Boolean,
                             Report As Boolean) As List(Of String)

        Dim DMApp As New DesignManager.Application
        Dim AllLinkedFilenames As New List(Of String)
        Dim tmpAllLinkedFilenames As New List(Of String)
        Dim FoundFiles As New List(Of String)
        Dim FileExtension As String
        Dim AllFilenames As New Dictionary(Of String, String)
        Dim tf As Boolean
        Dim IndexedDrives As New List(Of String)
        Dim IsDriveIndexed As Boolean = False

        Dim TopLevelFolder As String
        Dim tmpAllLinkedFilename As String

        Dim TLADoc As DesignManager.Document

        ' TODO: Deal with a mix of indexed and non-indexed drives

        ' Passing in an empty TopLevelFolders signifies a bare top level assy.  Don't need an indexed drive for that case.
        If Not TopLevelFolders.Count = 0 Then
            IndexedDrives = GetIndexedDrives()

            If IndexedDrives.Count > 0 Then
                For Each IndexedDrive In IndexedDrives
                    For Each TopLevelFolder In TopLevelFolders
                        If TopLevelFolder.ToLower().StartsWith(IndexedDrive.ToLower()) Then
                            IsDriveIndexed = True
                            Exit For
                        End If
                    Next
                Next
            End If
        End If

        DMApp.Visible = 1
        'DMApp.Visible = 0
        DMApp.DisplayAlerts = 0

        FMain.Activate()

        FMain.TextBoxStatus.Text = String.Format("Opening {0}", System.IO.Path.GetFileName(TopLevelAssembly))

        TLADoc = CType(DMApp.OpenFileInDesignManager(TopLevelAssembly), DesignManager.Document)

        If TopLevelFolders.Count > 0 Then
            For Each TopLevelFolder In TopLevelFolders
                tmpAllLinkedFilenames = FollowLinksBottomUp(DMApp, TLADoc, AllLinkedFilenames,
                                                 TopLevelFolder, AllFilenames, IsDriveIndexed, DraftAndModelSameName)

                For Each tmpAllLinkedFilename In tmpAllLinkedFilenames
                    If Not AllLinkedFilenames.Contains(tmpAllLinkedFilename) Then
                        AllLinkedFilenames.Add(tmpAllLinkedFilename)
                    End If
                Next

            Next
        Else
            ' Bare top level assy.  Call FollowLinksBottomUp with TopLevelFolder = ""
            tmpAllLinkedFilenames = FollowLinksBottomUp(DMApp, TLADoc, AllLinkedFilenames,
                                                 "", AllFilenames, IsDriveIndexed, DraftAndModelSameName)

            For Each tmpAllLinkedFilename In tmpAllLinkedFilenames
                If Not AllLinkedFilenames.Contains(tmpAllLinkedFilename) Then
                    AllLinkedFilenames.Add(tmpAllLinkedFilename)
                End If
            Next

        End If

        DMApp.Quit()

        For Each Filename In AllLinkedFilenames
            FileExtension = System.IO.Path.GetExtension(Filename).Replace(".", "*.")
            tf = ActiveFileExtensionsList.Contains(FileExtension)
            tf = tf And (Not FoundFiles.Contains(Filename, StringComparer.OrdinalIgnoreCase))
            If tf Then
                FoundFiles.Add(Filename)
            End If
        Next

        If Report Then
            ReportUnrelatedFiles(TopLevelFolders, FoundFiles)

        End If

        Return FoundFiles

    End Function

    Private Function FollowLinksBottomUp(DMApp As DesignManager.Application,
                                         DMDoc As DesignManager.Document,
                                         AllLinkedFilenames As List(Of String),
                                         TopLevelFolder As String,
                                         AllFilenames As Dictionary(Of String, String),
                                         IsDriveIndexed As Boolean,
                                         DraftAndModelSameName As Boolean
                                         ) As List(Of String)

        Dim LinkedDocs As DesignManager.LinkedDocuments
        Dim LinkedDoc As DesignManager.Document
        Dim LinkedDocName As String
        Dim ValidExtensions As New List(Of String)
        Dim Extension As String
        Dim WhereUsedFiles As New List(Of String)
        Dim WhereUsedFile As String
        Dim tf As Boolean

        Dim Filename As String

        If CheckInterruptRequest() Then
            Return AllLinkedFilenames
        End If

        ValidExtensions.Add(".asm")
        ValidExtensions.Add(".par")
        ValidExtensions.Add(".psm")
        ValidExtensions.Add(".dft")

        Filename = DMDoc.FullName

        ' 20230730
        ' Deal with FOA files
        Dim UC As New UtilsCommon
        Filename = UC.SplitFOAName(Filename)("Filename")
        'If Filename.Contains("!") Then
        '    Filename = Filename.Split("!"c)(0)  ' c:\project\foa.asm!Member1 > c:\project\foa.asm
        'End If


        If FileIO.FileSystem.FileExists(Filename) Then
            tf = Not AllLinkedFilenames.Contains(Filename, StringComparer.OrdinalIgnoreCase)
            If tf Then
                AllLinkedFilenames.Add(Filename)

                UpdateStatus("Follow Links", Filename)

                ' In case of corrupted file or other problem
                Try

                    ' Get any draft files containing this file.
                    ' WhereUsedFiles = GetWhereUsedBottomUp(DMApp, TopLevelFolder, DMDoc.FullName, IsDriveIndexed, DraftAndModelSameName)
                    WhereUsedFiles = GetWhereUsedBottomUp(DMApp, TopLevelFolder, Filename, IsDriveIndexed, DraftAndModelSameName)
                    For Each WhereUsedFile In WhereUsedFiles
                        Extension = IO.Path.GetExtension(WhereUsedFile)
                        If Extension = ".dft" Then
                            If Not AllLinkedFilenames.Contains(WhereUsedFile, StringComparer.OrdinalIgnoreCase) Then
                                AllLinkedFilenames.Add(WhereUsedFile)
                            End If
                        End If
                    Next

                    If FMain.CheckBoxTLAIncludePartCopies.Checked Then
                        ' tf = System.IO.Path.GetExtension(DMDoc.FullName) <> ".dft"
                        tf = System.IO.Path.GetExtension(Filename) <> ".dft"
                    Else
                        'tf = System.IO.Path.GetExtension(DMDoc.FullName) = ".asm"
                        tf = System.IO.Path.GetExtension(Filename) = ".asm"
                    End If

                    ' Follow links contained by this file, if any.
                    If tf Then
                        LinkedDocs = CType(DMDoc.LinkedDocuments, DesignManager.LinkedDocuments)
                        If LinkedDocs.Count > 0 Then
                            For Each LinkedDoc In LinkedDocs

                                ' Get FOP status
                                Dim FOPStatus As Integer
                                LinkedDoc.IsDocumentFOP(FOPStatus)

                                ' FOP Masters can have links to many unrelated files.  For example, a fastener.  
                                ' Do not include them, or follow their links.
                                If Not (FOPStatus = DesignManager.DocFOPStatus.FOPMasterDocument) Then
                                    LinkedDocName = LinkedDoc.FullName

                                    LinkedDocName = UC.SplitFOAName(LinkedDocName)("Filename")
                                    'If LinkedDocName.Contains("!") Then
                                    '    LinkedDocName = LinkedDocName.Split("!"c)(0)
                                    'End If

                                    Extension = IO.Path.GetExtension(LinkedDocName)

                                    If ValidExtensions.Contains(Extension) Then
                                        'AllLinkedFilenames = FollowLinksBottomUp(DMApp, LinkedDocName, AllLinkedFilenames,
                                        '                                         TopLevelFolder, AllFilenames, IsDriveIndexed)
                                        AllLinkedFilenames = FollowLinksBottomUp(DMApp, LinkedDoc, AllLinkedFilenames,
                                                                             TopLevelFolder, AllFilenames, IsDriveIndexed, DraftAndModelSameName)
                                    End If
                                End If
                            Next

                        End If

                    End If
                Catch ex As Exception
                End Try
            End If
        End If


        Return AllLinkedFilenames

    End Function



    Private Function GetWhereUsedBottomUp(
                     DMApp As DesignManager.Application,
                     TopLevelFolder As String,
                     Filename As String,
                     IsDriveIndexed As Boolean,
                     DraftAndModelSameName As Boolean) As List(Of String)

        Dim AllWhereUsedFileNames As New List(Of String)
        ' Dim msg As String
        Dim Extension As String

        Dim WhereUsedDocuments As New List(Of DesignManager.Document)
        Dim WhereUsedDocument As DesignManager.Document

        Dim arrDocUsed As Object = Nothing

        Dim DraftFilename As String

        UpdateStatus("Where Used", Filename)

        If TopLevelFolder = "" Then
            Return AllWhereUsedFileNames
        End If

        If CheckInterruptRequest() Then
            Return AllWhereUsedFileNames
        End If

        If DraftAndModelSameName Then
            'DraftFilename = String.Format("{0}{1}", System.IO.Path.GetFileNameWithoutExtension(Filename), ".dft")
            DraftFilename = System.IO.Path.ChangeExtension(Filename, ".dft")
            AllWhereUsedFileNames.Add(DraftFilename)
            Return AllWhereUsedFileNames
        End If

        Extension = IO.Path.GetExtension(Filename)

        If Not Extension = ".dft" Then  ' Draft files are not "Used" anywhere.
            If Not IsDriveIndexed Then
                'This "resets" DMApp.FindWhereUsed().  Somehow.
                DMApp.WhereUsedCriteria(Nothing, True) = TopLevelFolder

                'Finds the first WhereUsed Document, if any.
                WhereUsedDocument = CType(
                DMApp.FindWhereUsed(FileIO.FileSystem.GetFileInfo(Filename)),
                DesignManager.Document)

                While Not WhereUsedDocument Is Nothing
                    If Not AllWhereUsedFileNames.Contains(WhereUsedDocument.FullName) Then
                        ' For bottom_up search, the only applicable where used results are draft files
                        If IO.Path.GetExtension(WhereUsedDocument.FullName) = ".dft" Then
                            AllWhereUsedFileNames.Add(WhereUsedDocument.FullName)
                        End If
                    End If
                    'Finds the next WhereUsed document, if any.
                    WhereUsedDocument = CType(DMApp.FindWhereUsed(), DesignManager.Document)
                End While

            Else  'It is indexed
                Try
                    DMApp.WhereUsedCriteria(Nothing, True) = TopLevelFolder

                    DMApp.FindWhereUsedDocuments(FileIO.FileSystem.GetFileInfo(Filename), arrDocUsed)

                    For Each item As String In DirectCast(arrDocUsed, Array)
                        If Not AllWhereUsedFileNames.Contains(item) Then
                            ' For bottom_up search, the only applicable where used results are draft files
                            If IO.Path.GetExtension(item) = ".dft" Then
                                AllWhereUsedFileNames.Add(item)
                            End If
                        End If
                    Next

                Catch ex As Exception
                    MsgBox(ex.ToString)
                End Try

            End If

        End If

        Return AllWhereUsedFileNames

    End Function

    Private Sub UpdateStatus(Description As String, Filename As String)
        Dim msg As String

        msg = System.IO.Path.GetFileName(Filename)
        msg = String.Format("{0} {1}", Description, msg)

        FMain.TextBoxStatus.Text = msg
    End Sub

    Private Function CheckInterruptRequest() As Boolean
        Dim tf As Boolean = False

        System.Windows.Forms.Application.DoEvents()
        If Form_Main.StopProcess Then
            FMain.TextBoxStatus.Text = "Processing aborted"
            tf = True
        End If

        Return tf
    End Function

    Private Function GetIndexedDrives() As List(Of String)

        'Dim SearchScopeFilename As String = _mainInstance.TextBoxFastSearchScopeFilename.Text
        Dim SearchScopeFilename As String = FMain.TextBoxFastSearchScopeFilename.Text

        Dim SearchScope As String() = Nothing
        Dim CommentString As String = "\\ "
        Dim Line As String
        Dim IndexedDrives As New List(Of String)
        Dim msg As String = ""

        Try
            SearchScope = IO.File.ReadAllLines(SearchScopeFilename)
        Catch ex As Exception
            msg = String.Format("Fast search scope file '{0}' (on Configuration Tab) not found.{1}", SearchScopeFilename, vbCrLf)
            MsgBox(msg)
            'Exit Function
        End Try

        If (SearchScope.Count > 0) And (SearchScope IsNot Nothing) Then
            For Each item As String In SearchScope
                Line = item.TrimStart()
                If (Not Line.StartsWith(CommentString)) And (Line.Count > 0) Then
                    IndexedDrives.Add(Line)
                End If
            Next
        End If

        Return IndexedDrives
    End Function



End Class
