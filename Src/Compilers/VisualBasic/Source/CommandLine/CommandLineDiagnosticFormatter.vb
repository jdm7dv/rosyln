﻿' Copyright (c) Microsoft Open Technologies, Inc.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

Imports System.Text
Imports Microsoft.CodeAnalysis.Text

Namespace Microsoft.CodeAnalysis.VisualBasic
    Friend NotInheritable Class CommandLineDiagnosticFormatter
        Inherits VisualBasicDiagnosticFormatter

        Private ReadOnly m_baseDirectory As String

        Friend Sub New(baseDirectory As String)
            m_baseDirectory = baseDirectory
        End Sub

        ' Returns a diagnostic message in string.
        ' VB has a special implementation that prints out a squiggle under the error span as well as a diagnostic message.
        ' e.g.,
        '   c:\Roslyn\Temp\a.vb(5) : warning BC42024: Unused local variable: 'x'.
        '
        '       Dim x As Integer
        '           ~
        '
        Public Overrides Function Format(diagnostic As Diagnostic, Optional formatter As IFormatProvider = Nothing) As String
            ' Builds a diagnostic message
            ' Dev12 vbc prints raw paths -- relative and not normalized, so we don't need to customize the base implementation.
            Dim baseMessage = MyBase.Format(diagnostic, formatter)
            If Not diagnostic.Location.IsInSource Then

                ' Add "vbc : " command line prefix to the start of the command line diagnostics which do not have a location to match the 
                ' behaviour of native compiler.    This allows MSBuild output to be consistent whether Roslyn is installed or not.      
                If diagnostic.Location.Kind = LocationKind.None Then
                    baseMessage = VisualBasicCompiler.VbcCommandLinePrefix & baseMessage
                End If

                Return baseMessage
            End If

            Dim sb As New StringBuilder()
            sb.AppendLine(baseMessage)

            ' the squiggles are displayed for the original (unmapped) location
            Dim sourceLocation = diagnostic.Location
            Dim text = sourceLocation.SourceTree.GetText()
            Dim sourceSpanStart = sourceLocation.SourceSpan.Start
            Dim sourceSpanEnd = sourceLocation.SourceSpan.End
            Dim linenumber = text.Lines.IndexOf(sourceSpanStart)
            Dim line = text.Lines(linenumber)

            If sourceLocation.SourceSpan.IsEmpty AndAlso line.Start = sourceSpanEnd AndAlso linenumber > 0 Then
                ' Sometimes there is something missing at the end of the line, then the error is reported with an empty span
                ' beyond the end of the line, which makes it appear that the span starts at the beginning of the next line.
                ' Let's go back to the previous line in this case.
                linenumber -= 1
                line = text.Lines(linenumber)
            End If

            While (line.Start < sourceSpanEnd)
                ' Builds the original text line
                sb.AppendLine()
                sb.AppendLine(line.ToString().Replace(vbTab, "    ")) ' normalize tabs with 4 spaces

                ' Builds leading spaces up to the error span
                For position = Math.Min(sourceSpanStart, line.Start) To Math.Min(line.End, sourceSpanStart) - 1
                    If (text(position) = vbTab) Then
                        ' normalize tabs with 4 spaces
                        sb.Append(" "c, 4)
                    Else
                        sb.Append(" ")
                    End If
                Next

                ' Builds squiggles
                If sourceLocation.SourceSpan.IsEmpty Then
                    sb.Append("~")
                Else
                    For position = Math.Max(sourceSpanStart, line.Start) To Math.Min(If(sourceSpanEnd = sourceSpanStart, sourceSpanEnd, sourceSpanEnd - 1), line.End - 1)
                        If (text(position) = vbTab) Then
                            ' normalize tabs with 4 spaces
                            sb.Append("~"c, 4)
                        Else
                            sb.Append("~")
                        End If
                    Next
                End If

                ' Builds tailing spaces up to the end of this line
                For position = Math.Min(sourceSpanEnd, line.End) To line.End - 1
                    If (text(position) = vbTab) Then
                        ' normalize tabs with 4 spaces
                        sb.Append(" "c, 4)
                    Else
                        sb.Append(" ")
                    End If
                Next

                ' The error span can continue over multiple lines
                linenumber = linenumber + 1
                If linenumber >= text.Lines.Count Then
                    ' Exit the loop when we reach the end line (0-based)
                    Exit While
                End If

                line = text.Lines(linenumber)
            End While

            Return sb.ToString()
        End Function

        Friend Overrides Function FormatSourcePath(path As String, basePath As String, formatter As IFormatProvider) As String
            Return If(FileUtilities.NormalizeRelativePath(path, basePath, m_baseDirectory), path)
        End Function
    End Class
End Namespace