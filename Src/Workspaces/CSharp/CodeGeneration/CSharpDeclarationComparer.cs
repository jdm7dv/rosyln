﻿// Copyright (c) Microsoft Open Technologies, Inc.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Symbols;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using Roslyn.Utilities;

namespace Microsoft.CodeAnalysis.CSharp.CodeGeneration
{
    internal class CSharpDeclarationComparer : IComparer<SyntaxNode>
    {
        public static readonly IComparer<SyntaxNode> Instance = new CSharpDeclarationComparer();

        private static readonly Dictionary<SyntaxKind, int> kindPrecedenceMap = new Dictionary<SyntaxKind, int>()
        {
            { SyntaxKind.FieldDeclaration, 0 },
            { SyntaxKind.ConstructorDeclaration, 1 },
            { SyntaxKind.DestructorDeclaration, 2 },
            { SyntaxKind.IndexerDeclaration, 3 },
            { SyntaxKind.PropertyDeclaration, 4 },
            { SyntaxKind.EventFieldDeclaration, 5 },
            { SyntaxKind.EventDeclaration, 6 },
            { SyntaxKind.MethodDeclaration, 7 },
            { SyntaxKind.OperatorDeclaration, 8 },
            { SyntaxKind.ConversionOperatorDeclaration, 9 },
            { SyntaxKind.EnumDeclaration, 10 },
            { SyntaxKind.InterfaceDeclaration, 11 },
            { SyntaxKind.StructDeclaration, 12 },
            { SyntaxKind.ClassDeclaration, 13 },
            { SyntaxKind.DelegateDeclaration, 14 }
        };

        private static readonly Dictionary<SyntaxKind, int> operatorPrecedenceMap = new Dictionary<SyntaxKind, int>()
        {
            { SyntaxKind.PlusToken, 0 },
            { SyntaxKind.MinusToken, 1 },
            { SyntaxKind.ExclamationToken, 2 },
            { SyntaxKind.TildeToken, 3 },
            { SyntaxKind.PlusPlusToken, 4 },
            { SyntaxKind.MinusMinusToken, 5 },
            { SyntaxKind.AsteriskToken, 6 },
            { SyntaxKind.SlashToken, 7 },
            { SyntaxKind.PercentToken, 8 },
            { SyntaxKind.AmpersandToken, 9 },
            { SyntaxKind.BarToken, 10 },
            { SyntaxKind.CaretToken, 11 },
            { SyntaxKind.LessThanLessThanToken, 12 },
            { SyntaxKind.GreaterThanGreaterThanToken, 13 },
            { SyntaxKind.EqualsEqualsToken, 14 },
            { SyntaxKind.ExclamationEqualsToken, 15 },
            { SyntaxKind.LessThanToken, 16 },
            { SyntaxKind.GreaterThanToken, 17 },
            { SyntaxKind.LessThanEqualsToken, 18 },
            { SyntaxKind.GreaterThanEqualsToken, 19 },
            { SyntaxKind.TrueKeyword, 20 },
            { SyntaxKind.FalseKeyword, 21 },
        };

        private CSharpDeclarationComparer()
        {
        }

        public int Compare(SyntaxNode x, SyntaxNode y)
        {
            if (x.CSharpKind() != y.CSharpKind())
            {
                int xPrecedence, yPrecedence;
                if (!kindPrecedenceMap.TryGetValue(x.CSharpKind(), out xPrecedence) ||
                    !kindPrecedenceMap.TryGetValue(y.CSharpKind(), out yPrecedence))
                {
                    // The containing declaration is malformed and contains a node kind we did not expect.
                    // Ignore comparisons with those unexpected nodes and sort them to the end of the declaration.
                    return 1;
                }

                return xPrecedence < yPrecedence ? -1 : 1;
            }

            switch (x.CSharpKind())
            {
                case SyntaxKind.DelegateDeclaration:
                    return Compare((DelegateDeclarationSyntax)x, (DelegateDeclarationSyntax)y);

                case SyntaxKind.FieldDeclaration:
                case SyntaxKind.EventFieldDeclaration:
                    return Compare((BaseFieldDeclarationSyntax)x, (BaseFieldDeclarationSyntax)y);

                case SyntaxKind.ConstructorDeclaration:
                    return Compare((ConstructorDeclarationSyntax)x, (ConstructorDeclarationSyntax)y);

                case SyntaxKind.DestructorDeclaration:
                    // All destructors are equal since there can only be one per named type
                    return 0;

                case SyntaxKind.MethodDeclaration:
                    return Compare((MethodDeclarationSyntax)x, (MethodDeclarationSyntax)y);

                case SyntaxKind.OperatorDeclaration:
                    return Compare((OperatorDeclarationSyntax)x, (OperatorDeclarationSyntax)y);

                case SyntaxKind.EventDeclaration:
                    return Compare((EventDeclarationSyntax)x, (EventDeclarationSyntax)y);

                case SyntaxKind.IndexerDeclaration:
                    return Compare((IndexerDeclarationSyntax)x, (IndexerDeclarationSyntax)y);

                case SyntaxKind.PropertyDeclaration:
                    return Compare((PropertyDeclarationSyntax)x, (PropertyDeclarationSyntax)y);

                case SyntaxKind.EnumDeclaration:
                    return Compare((EnumDeclarationSyntax)x, (EnumDeclarationSyntax)y);

                case SyntaxKind.InterfaceDeclaration:
                case SyntaxKind.StructDeclaration:
                case SyntaxKind.ClassDeclaration:
                    return Compare((BaseTypeDeclarationSyntax)x, (BaseTypeDeclarationSyntax)y);

                case SyntaxKind.ConversionOperatorDeclaration:
                    return Compare((ConversionOperatorDeclarationSyntax)x, (ConversionOperatorDeclarationSyntax)y);

                case SyntaxKind.IncompleteMember:
                    // Since these are incomplete members they are considered to be equal
                    return 0;
                case SyntaxKind.GlobalStatement:
                    // for REPL, don't mess with order, just put new one at the end.
                    return 1;
                default:
                    Contract.Fail("Syntax nodes x and y are not declarations");
                    return 0;
            }
        }

        private static int Compare(DelegateDeclarationSyntax x, DelegateDeclarationSyntax y)
        {
            int result;
            if (EqualAccessibility(x, x.Modifiers, y, y.Modifiers, out result) &&
                EqualIdentifierName(x.Identifier, y.Identifier, out result))
            {
                EqualTypeParameterCount(x.TypeParameterList, y.TypeParameterList, out result);
            }

            return result;
        }

        private static int Compare(BaseFieldDeclarationSyntax x, BaseFieldDeclarationSyntax y)
        {
            int result;
            if (EqualConstness(x.Modifiers, y.Modifiers, out result) &&
                EqualStaticness(x.Modifiers, y.Modifiers, out result) &&
                EqualAccessibility(x, x.Modifiers, y, y.Modifiers, out result))
            {
                EqualIdentifierName(
                    x.Declaration.Variables.FirstOrDefault().Identifier,
                    y.Declaration.Variables.FirstOrDefault().Identifier,
                    out result);
            }

            return result;
        }

        private static int Compare(ConstructorDeclarationSyntax x, ConstructorDeclarationSyntax y)
        {
            int result;
            if (EqualStaticness(x.Modifiers, y.Modifiers, out result) &&
                EqualAccessibility(x, x.Modifiers, y, y.Modifiers, out result))
            {
                EqualParameterCount(x.ParameterList, y.ParameterList, out result);
            }

            return result;
        }

        private static int Compare(MethodDeclarationSyntax x, MethodDeclarationSyntax y)
        {
            int result;
            if (EqualStaticness(x.Modifiers, y.Modifiers, out result) &&
                EqualAccessibility(x, x.Modifiers, y, y.Modifiers, out result) &&
                EqualIdentifierName(x.Identifier, y.Identifier, out result) &&
                EqualTypeParameterCount(x.TypeParameterList, y.TypeParameterList, out result))
            {
                EqualParameterCount(x.ParameterList, y.ParameterList, out result);
            }

            return result;
        }

        private static int Compare(ConversionOperatorDeclarationSyntax x, ConversionOperatorDeclarationSyntax y)
        {
            int result;
            if (x.ImplicitOrExplicitKeyword.CSharpKind() != y.ImplicitOrExplicitKeyword.CSharpKind())
            {
                return x.ImplicitOrExplicitKeyword.CSharpKind() == SyntaxKind.ImplicitKeyword ? -1 : 1;
            }

            EqualParameterCount(x.ParameterList, y.ParameterList, out result);

            return result;
        }

        private static int Compare(OperatorDeclarationSyntax x, OperatorDeclarationSyntax y)
        {
            int result;
            if (EqualOperatorPrecedence(x.OperatorToken, y.OperatorToken, out result))
            {
                EqualParameterCount(x.ParameterList, y.ParameterList, out result);
            }

            return result;
        }

        private static int Compare(EventDeclarationSyntax x, EventDeclarationSyntax y)
        {
            int result;
            if (EqualStaticness(x.Modifiers, y.Modifiers, out result) &&
                EqualAccessibility(x, x.Modifiers, y, y.Modifiers, out result))
            {
                EqualIdentifierName(x.Identifier, y.Identifier, out result);
            }

            return result;
        }

        private static int Compare(IndexerDeclarationSyntax x, IndexerDeclarationSyntax y)
        {
            int result;
            if (EqualStaticness(x.Modifiers, y.Modifiers, out result) &&
                EqualAccessibility(x, x.Modifiers, y, y.Modifiers, out result))
            {
                EqualParameterCount(x.ParameterList, y.ParameterList, out result);
            }

            return result;
        }

        private static int Compare(PropertyDeclarationSyntax x, PropertyDeclarationSyntax y)
        {
            int result;
            if (EqualStaticness(x.Modifiers, y.Modifiers, out result) &&
                EqualAccessibility(x, x.Modifiers, y, y.Modifiers, out result))
            {
                EqualIdentifierName(x.Identifier, y.Identifier, out result);
            }

            return result;
        }

        private static int Compare(EnumDeclarationSyntax x, EnumDeclarationSyntax y)
        {
            int result;
            if (EqualAccessibility(x, x.Modifiers, y, y.Modifiers, out result))
            {
                EqualIdentifierName(x.Identifier, y.Identifier, out result);
            }

            return result;
        }

        private static int Compare(BaseTypeDeclarationSyntax x, BaseTypeDeclarationSyntax y)
        {
            int result;
            if (EqualStaticness(x.Modifiers, y.Modifiers, out result) &&
                EqualAccessibility(x, x.Modifiers, y, y.Modifiers, out result) &&
                EqualIdentifierName(x.Identifier, y.Identifier, out result))
            {
                if (x.CSharpKind() == SyntaxKind.ClassDeclaration)
                {
                    EqualTypeParameterCount(
                        ((ClassDeclarationSyntax)x).TypeParameterList,
                        ((ClassDeclarationSyntax)y).TypeParameterList,
                        out result);
                }
                else if (x.CSharpKind() == SyntaxKind.StructDeclaration)
                {
                    EqualTypeParameterCount(
                        ((StructDeclarationSyntax)x).TypeParameterList,
                        ((StructDeclarationSyntax)y).TypeParameterList,
                        out result);
                }
                else
                {
                    EqualTypeParameterCount(
                        ((InterfaceDeclarationSyntax)x).TypeParameterList,
                        ((InterfaceDeclarationSyntax)y).TypeParameterList,
                        out result);
                }
            }

            return result;
        }

        private static bool NeitherNull(object x, object y, out int comparisonResult)
        {
            if (x == null && y == null)
            {
                comparisonResult = 0;
                return false;
            }
            else if (x == null)
            {
                // x == null && y != null
                comparisonResult = -1;
                return false;
            }
            else if (y == null)
            {
                // x != null && y == null
                comparisonResult = 1;
                return false;
            }
            else
            {
                // x != null && y != null
                comparisonResult = 0;
                return true;
            }
        }

        private static bool ContainsToken(SyntaxTokenList list, SyntaxKind kind)
        {
            return list.Contains(token => token.CSharpKind() == kind);
        }

        private enum Accessibility
        {
            Public,
            Protected,
            ProtectedInternal,
            Internal,
            Private
        }

        private static int GetAccessibilityPrecedence(SyntaxNode declaration, SyntaxNode parent, SyntaxTokenList modifiers)
        {
            if (ContainsToken(modifiers, SyntaxKind.PublicKeyword))
            {
                return (int)Accessibility.Public;
            }
            else if (ContainsToken(modifiers, SyntaxKind.ProtectedKeyword))
            {
                if (ContainsToken(modifiers, SyntaxKind.InternalKeyword))
                {
                    return (int)Accessibility.ProtectedInternal;
                }

                return (int)Accessibility.Protected;
            }
            else if (ContainsToken(modifiers, SyntaxKind.InternalKeyword))
            {
                return (int)Accessibility.Internal;
            }
            else if (ContainsToken(modifiers, SyntaxKind.PrivateKeyword))
            {
                return (int)Accessibility.Private;
            }

            // Determine default accessibility: This declaration is internal if we traverse up
            // the syntax tree and don't find a containing named type.
            for (var node = parent; node != null; node = node.Parent)
            {
                if (node.CSharpKind() == SyntaxKind.InterfaceDeclaration)
                {
                    // All interface members are public
                    return (int)Accessibility.Public;
                }
                else if (node.CSharpKind() == SyntaxKind.StructDeclaration || node.CSharpKind() == SyntaxKind.ClassDeclaration)
                {
                    // Members and nested types default to private
                    return (int)Accessibility.Private;
                }
            }

            return (int)Accessibility.Internal;
        }

        private static bool BothHaveModifier(SyntaxTokenList x, SyntaxTokenList y, SyntaxKind modifierKind, out int comparisonResult)
        {
            var xHasModifier = ContainsToken(x, modifierKind);
            var yHasModifier = ContainsToken(y, modifierKind);

            if (xHasModifier == yHasModifier)
            {
                comparisonResult = 0;
                return true;
            }

            comparisonResult = xHasModifier ? -1 : 1;
            return false;
        }

        private static bool EqualStaticness(SyntaxTokenList x, SyntaxTokenList y, out int comparisonResult)
        {
            return BothHaveModifier(x, y, SyntaxKind.StaticKeyword, out comparisonResult);
        }

        private static bool EqualConstness(SyntaxTokenList x, SyntaxTokenList y, out int comparisonResult)
        {
            return BothHaveModifier(x, y, SyntaxKind.ConstKeyword, out comparisonResult);
        }

        private static bool EqualAccessibility(SyntaxNode x, SyntaxTokenList xModifiers, SyntaxNode y, SyntaxTokenList yModifiers, out int comparisonResult)
        {
            var xAccessibility = GetAccessibilityPrecedence(x, x.Parent ?? y.Parent, xModifiers);
            var yAccessibility = GetAccessibilityPrecedence(y, y.Parent ?? x.Parent, yModifiers);

            comparisonResult = xAccessibility - yAccessibility;
            return comparisonResult == 0;
        }

        private static bool EqualIdentifierName(SyntaxToken x, SyntaxToken y, out int comparisonResult)
        {
            if (NeitherNull(x, y, out comparisonResult))
            {
                comparisonResult = string.Compare(x.ValueText, y.ValueText, StringComparison.OrdinalIgnoreCase);
            }

            return comparisonResult == 0;
        }

        private static bool EqualOperatorPrecedence(SyntaxToken x, SyntaxToken y, out int comparisonResult)
        {
            if (NeitherNull(x, y, out comparisonResult))
            {
                int xPrecedence = 0;
                int yPrecedence = 0;
                operatorPrecedenceMap.TryGetValue(x.CSharpKind(), out xPrecedence);
                operatorPrecedenceMap.TryGetValue(y.CSharpKind(), out yPrecedence);

                comparisonResult = xPrecedence - yPrecedence;
            }

            return comparisonResult == 0;
        }

        private static bool EqualParameterCount(BaseParameterListSyntax x, BaseParameterListSyntax y, out int comparisonResult)
        {
            var xParameterCount = x.Parameters.Count;
            var yParameterCount = y.Parameters.Count;

            comparisonResult = xParameterCount - yParameterCount;

            return comparisonResult == 0;
        }

        private static bool EqualTypeParameterCount(TypeParameterListSyntax x, TypeParameterListSyntax y, out int comparisonResult)
        {
            if (NeitherNull(x, y, out comparisonResult))
            {
                var xParameterCount = x.Parameters.Count;
                var yParameterCount = y.Parameters.Count;

                comparisonResult = xParameterCount - yParameterCount;
            }

            return comparisonResult == 0;
        }
    }
}
