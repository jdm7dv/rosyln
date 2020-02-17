﻿' Copyright (c) Microsoft Open Technologies, Inc.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

Imports System.Collections.Immutable
Imports System.Reflection.Metadata
Imports System.Reflection.Metadata.Ecma335
Imports System.Runtime.InteropServices
Imports Microsoft.Cci
Imports Microsoft.CodeAnalysis.CodeGen
Imports Microsoft.CodeAnalysis.Emit
Imports Microsoft.CodeAnalysis.VisualBasic.Symbols
Imports Microsoft.CodeAnalysis.VisualBasic.Symbols.Metadata.PE
Imports Microsoft.CodeAnalysis.VisualBasic.Syntax

Namespace Microsoft.CodeAnalysis.VisualBasic.Emit
    ''' <summary>
    ''' Matches symbols from an assembly in one compilation to
    ''' the corresponding assembly in another. Assumes that only
    ''' one assembly has changed between the two compilations.
    ''' </summary>
    Friend NotInheritable Class VisualBasicDefinitionMap
        Inherits DefinitionMap

        Private ReadOnly metadataDecoder As MetadataDecoder
        Private ReadOnly mapToMetadata As SymbolMatcher
        Private ReadOnly mapToPrevious As SymbolMatcher

        Public Sub New([module] As PEModule,
                       edits As IEnumerable(Of SemanticEdit),
                       metadataDecoder As MetadataDecoder,
                       mapToMetadata As SymbolMatcher,
                       mapToPrevious As SymbolMatcher)

            MyBase.New([module], edits)

            Debug.Assert(metadataDecoder IsNot Nothing)
            Debug.Assert(mapToMetadata IsNot Nothing)

            Me.metadataDecoder = metadataDecoder
            Me.mapToMetadata = mapToMetadata
            Me.mapToPrevious = If(mapToPrevious, mapToMetadata)
        End Sub

        Friend Function TryGetAnonymousTypeName(template As NamedTypeSymbol, <Out> ByRef name As String, <Out> ByRef index As Integer) As Boolean
            Return Me.mapToPrevious.TryGetAnonymousTypeName(template, name, index)
        End Function

        Friend Overrides Function TryGetTypeHandle(def As ITypeDefinition, <Out> ByRef handle As TypeHandle) As Boolean
            Dim other = TryCast(Me.mapToMetadata.MapDefinition(def), PENamedTypeSymbol)
            If other IsNot Nothing Then
                handle = other.Handle
                Return True
            Else
                handle = Nothing
                Return False
            End If
        End Function

        Friend Overrides Function TryGetEventHandle(def As IEventDefinition, <Out> ByRef handle As EventHandle) As Boolean
            Dim other = TryCast(Me.mapToMetadata.MapDefinition(def), PEEventSymbol)
            If other IsNot Nothing Then
                handle = other.Handle
                Return True
            Else
                handle = Nothing
                Return False
            End If
        End Function

        Friend Overrides Function TryGetFieldHandle(def As IFieldDefinition, <Out> ByRef handle As FieldHandle) As Boolean
            Dim other = TryCast(Me.mapToMetadata.MapDefinition(def), PEFieldSymbol)
            If other IsNot Nothing Then
                handle = other.Handle
                Return True
            Else
                handle = Nothing
                Return False
            End If
        End Function

        Friend Overrides Function TryGetMethodHandle(def As IMethodDefinition, <Out> ByRef handle As MethodHandle) As Boolean
            Dim other = TryCast(Me.mapToMetadata.MapDefinition(def), PEMethodSymbol)
            If other IsNot Nothing Then
                handle = other.Handle
                Return True
            Else
                handle = Nothing
                Return False
            End If
        End Function

        Friend Overrides Function TryGetPropertyHandle(def As IPropertyDefinition, <Out> ByRef handle As PropertyHandle) As Boolean
            Dim other = TryCast(Me.mapToMetadata.MapDefinition(def), PEPropertySymbol)
            If other IsNot Nothing Then
                handle = other.Handle
                Return True
            Else
                handle = Nothing
                Return False
            End If
        End Function

        Friend Overrides Function DefinitionExists(def As IDefinition) As Boolean
            Dim previous = Me.mapToPrevious.MapDefinition(def)
            Return previous IsNot Nothing
        End Function

        Friend Overrides Function TryGetPreviousLocals(
                             baseline As EmitBaseline,
                             method As IMethodSymbol,
                             <Out> ByRef previousLocals As ImmutableArray(Of EncLocalInfo),
                             <Out> ByRef getPreviousLocalSlot As GetPreviousLocalSlot) As Boolean

            previousLocals = Nothing
            getPreviousLocalSlot = NoPreviousLocalSlot

            Dim handle As MethodHandle = Nothing
            If Not Me.TryGetMethodHandle(baseline, CType(method, IMethodDefinition), handle) Then
                ' Unrecognized method. Must have been added in the current compilation.
                Return False
            End If

            Dim methodEntry As MethodDefinitionEntry = Nothing
            If Not Me.methodMap.TryGetValue(method, methodEntry) Then
                ' Not part of changeset. No need to preserve locals.
                Return False
            End If

            If Not methodEntry.PreserveLocalVariables Then
                ' Not necessary to preserve locals.
                Return False
            End If

            Dim previousMethod = methodEntry.PreviousMethod
            Dim methodIndex As UInteger = CUInt(MetadataTokens.GetRowNumber(handle))
            Dim map As SymbolMatcher

            ' Check if method has changed previously. If so, we already have a map.
            If baseline.LocalsForMethodsAddedOrChanged.TryGetValue(methodIndex, previousLocals) Then
                map = Me.mapToPrevious
            Else
                ' Method has not changed since initial generation. Generate a map
                ' using the local names provided with the initial metadata.
                Dim localNames As ImmutableArray(Of String) = baseline.LocalNames.Invoke(methodIndex)
                Debug.Assert(Not localNames.IsDefault)

                Dim localInfo As ImmutableArray(Of MetadataDecoder.LocalInfo) = Nothing
                Try
                    Debug.Assert(Me.module.HasIL)
                    Dim methodIL As MethodBodyBlock = Me.module.GetMethodBodyOrThrow(handle)
                    If Not methodIL.LocalSignature.IsNil Then
                        Dim signature = Me.module.MetadataReader.GetLocalSignature(methodIL.LocalSignature)
                        localInfo = Me.metadataDecoder.DecodeLocalSignatureOrThrow(signature)
                    Else
                        localInfo = ImmutableArray(Of MetadataDecoder.LocalInfo).Empty
                    End If
                Catch ex As UnsupportedSignatureContent
                Catch ex As BadImageFormatException
                End Try

                If localInfo.IsDefault Then
                    ' TODO: Report error that metadata is not supported.
                    Return False
                End If

                ' The signature may have more locals than names if trailing locals are unnamed.
                ' (Locals in the middle of the signature may be unnamed too but since localNames
                ' Is indexed by slot, unnamed locals before the last named local will be represented
                ' as null values in the array.)
                Debug.Assert(localInfo.Length >= localNames.Length)
                previousLocals = GetLocalSlots(previousMethod, localNames, localInfo)
                Debug.Assert(previousLocals.Length = localInfo.Length)

                map = Me.mapToMetadata
            End If

            ' Find declarators in previous method syntax.
            ' The locals are indices into this list.
            Dim previousDeclarators As ImmutableArray(Of VisualBasicSyntaxNode) = LocalVariableDeclaratorsCollector.GetDeclarators(previousMethod)

            ' Create a map from declarator to declarator offset.
            Dim previousDeclaratorToOffset = New Dictionary(Of VisualBasicSyntaxNode, Integer)()
            For offset As Integer = 0 To previousDeclarators.Length - 1
                previousDeclaratorToOffset.Add(previousDeclarators(offset), offset)
            Next

            ' Create a map from local info to slot.
            Dim previousLocalInfoToSlot As Dictionary(Of EncLocalInfo, Integer) = New Dictionary(Of EncLocalInfo, Integer)()
            For slot As Integer = 0 To previousLocals.Length - 1
                Dim localInfo As EncLocalInfo = previousLocals(slot)
                Debug.Assert(Not localInfo.IsDefault)
                If localInfo.IsInvalid Then
                    ' Unrecognized or deleted local.
                    Continue For
                End If
                previousLocalInfoToSlot.Add(localInfo, slot)
            Next

            Dim syntaxMap As Func(Of SyntaxNode, SyntaxNode) = methodEntry.SyntaxMap
            If syntaxMap Is Nothing Then
                ' If there was no syntax map, the syntax structure has not changed,
                ' so we can map from current to previous syntax by declarator index.
                Debug.Assert(methodEntry.PreserveLocalVariables)
                ' Create a map from declarator to declarator index.

                Dim currentDeclarators As ImmutableArray(Of VisualBasicSyntaxNode) = LocalVariableDeclaratorsCollector.GetDeclarators(method)
                Dim currentDeclaratorToIndex = CreateDeclaratorToIndexMap(currentDeclarators)
                syntaxMap = Function(currentSyntax As SyntaxNode)
                                Dim currentIndex As Integer = currentDeclaratorToIndex(DirectCast(currentSyntax, VisualBasicSyntaxNode))
                                Return previousDeclarators(currentIndex)
                            End Function
            End If

            getPreviousLocalSlot = Function(identity As Object, typeRef As ITypeReference, constraints As LocalSlotConstraints)
                                       Dim local = DirectCast(identity, LocalSymbol)
                                       Dim syntaxRefs = local.DeclaringSyntaxReferences
                                       Debug.Assert(Not syntaxRefs.IsDefault)

                                       If Not syntaxRefs.IsDefaultOrEmpty Then
                                           Dim currentSyntax As SyntaxNode = syntaxRefs(0).GetSyntax(Nothing)
                                           Dim previousSyntax = DirectCast(syntaxMap(currentSyntax), VisualBasicSyntaxNode)

                                           Dim offset As Integer = Nothing
                                           If previousSyntax IsNot Nothing AndAlso previousDeclaratorToOffset.TryGetValue(previousSyntax, offset) Then
                                               Dim previousType = map.MapReference(typeRef)
                                               If previousType IsNot Nothing Then
                                                   Dim localKey = New EncLocalInfo(offset, previousType, constraints, CInt(local.TempKind))
                                                   Dim slot As Integer
                                                   If previousLocalInfoToSlot.TryGetValue(localKey, slot) Then
                                                       Return slot
                                                   End If
                                               End If
                                           End If
                                       End If

                                       Return -1
                                   End Function
            Return True
        End Function

        Private Overloads Function TryGetMethodHandle(baseline As EmitBaseline, def As IMethodDefinition, <Out> ByRef handle As MethodHandle) As Boolean
            If Me.TryGetMethodHandle(def, handle) Then
                Return True
            End If

            def = DirectCast(Me.mapToPrevious.MapDefinition(def), IMethodDefinition)
            If def IsNot Nothing Then
                Dim methodIndex As UInteger = 0
                If baseline.MethodsAdded.TryGetValue(def, methodIndex) Then
                    handle = MetadataTokens.MethodHandle(CInt(methodIndex))
                    Return True
                End If
            End If

            handle = Nothing
            Return False
        End Function

        Friend Overrides Function GetLocalInfo(methodDef As IMethodDefinition, localDefs As ImmutableArray(Of LocalDefinition)) As ImmutableArray(Of EncLocalInfo)
            If localDefs.IsEmpty Then
                Return ImmutableArray(Of EncLocalInfo).Empty
            End If

            ' Find declarators in current method syntax.
            Dim declarators As ImmutableArray(Of VisualBasicSyntaxNode) = LocalVariableDeclaratorsCollector.GetDeclarators(DirectCast(methodDef, MethodSymbol))

            ' Create a map from declarator to declarator index.
            Dim declaratorToIndex As IReadOnlyDictionary(Of SyntaxNode, Integer) = CreateDeclaratorToIndexMap(declarators)

            Return localDefs.SelectAsArray(Function(localDef) GetLocalInfo(declaratorToIndex, localDef))
        End Function

        Private Overloads Shared Function GetLocalInfo(declaratorToIndex As IReadOnlyDictionary(Of SyntaxNode, Integer), localDef As LocalDefinition) As EncLocalInfo
            ' Local symbol will be null for short-lived temporaries.
            Dim local = DirectCast(localDef.Identity, LocalSymbol)
            If local IsNot Nothing Then
                Dim syntaxRefs = local.DeclaringSyntaxReferences
                Debug.Assert(Not syntaxRefs.IsDefault)

                If Not syntaxRefs.IsDefaultOrEmpty Then
                    Dim syntax As SyntaxNode = syntaxRefs(0).GetSyntax()
                    Return New EncLocalInfo(declaratorToIndex(syntax), localDef.Type, localDef.Constraints, CInt(local.TempKind))
                End If
            End If
            Return New EncLocalInfo(localDef.Type, localDef.Constraints)
        End Function

        Private Shared Function CreateDeclaratorToIndexMap(declarators As ImmutableArray(Of VisualBasicSyntaxNode)) As IReadOnlyDictionary(Of SyntaxNode, Integer)
            Dim declaratorToIndex As Dictionary(Of SyntaxNode, Integer) = New Dictionary(Of SyntaxNode, Integer)()
            For i As Integer = 0 To declarators.Length - 1
                declaratorToIndex.Add(declarators(i), i)
            Next
            Return declaratorToIndex
        End Function

        ''' <summary>
        ''' Match local declarations to names to generate a map from
        ''' declaration to local slot. The names are indexed by slot and the
        ''' assumption is that declarations are in the same order as slots.
        ''' </summary>
        Private Shared Function GetLocalSlots(method As IMethodSymbol,
                                              localNames As ImmutableArray(Of String),
                                              localInfo As ImmutableArray(Of MetadataDecoder.LocalInfo)) As ImmutableArray(Of EncLocalInfo)

            Dim syntaxRefs = method.DeclaringSyntaxReferences

            ' No syntax refs for synthesized methods.
            If syntaxRefs.Length = 0 Then
                Return ImmutableArray(Of EncLocalInfo).Empty
            End If

            Dim syntax = syntaxRefs(0).GetSyntax()
            Dim block = syntax.Parent
            Debug.Assert(TypeOf block Is MethodBlockBaseSyntax)

            Dim map = LocalSlotMapBuilder.CreateMap(block, localNames, localInfo)
            Dim locals(localInfo.Length - 1) As EncLocalInfo
            For Each pair In map
                locals(pair.Value) = pair.Key
            Next

            ' Populate any remaining locals that were not matched to source.
            For i = 0 To locals.Length - 1
                If locals(i).IsDefault Then
                    Dim info = localInfo(i)
                    Dim constraints = GetConstraints(info)
                    locals(i) = New EncLocalInfo(DirectCast(info.Type, ITypeReference), constraints)
                End If
            Next

            Return ImmutableArray.Create(locals)
        End Function

        Private Shared Function GetConstraints(info As MetadataDecoder.LocalInfo) As LocalSlotConstraints
            Return If(info.IsPinned, LocalSlotConstraints.Pinned, LocalSlotConstraints.None) Or
                If(info.IsByRef, LocalSlotConstraints.ByRef, LocalSlotConstraints.None)
        End Function
    End Class
End Namespace